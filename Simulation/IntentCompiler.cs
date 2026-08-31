namespace ProxyState.Simulation;

public enum TargetKind : byte { None, Location, Entity }
public enum LocationValue : byte { None, Current, Home, Work }
public enum SortOrder : byte { Ascending, Descending }

public sealed record CompiledUtilityInput(
    CompiledNumericExpression Expression, float Weight, ResponsePoint[] Curve);
public sealed record CompiledTraitModifier(long TraitBit, float Modifier);
public sealed record CompiledEffect(int AttributeIndex, float PerMinute);
public sealed record CompiledTargetRank(CompiledNumericExpression Value, SortOrder Order);
public sealed record CompiledTargetQuery(
    CompiledPredicate[] Requirements, CompiledTargetRank[] RankBy, int? Limit);
public sealed record CompiledTargetSelector(
    TargetKind Kind, LocationValue Location, CompiledTargetQuery? Query);

// This is the only intent representation consumed during simulation ticks.
// Authoring strings have been replaced by compact enums, indexes, and bit values.
public sealed record CompiledIntent(
    string Id,
    string Name,
    int Hash,
    ushort RuntimeIndex,
    ActivityDefinition Activity,
    float BaseUtility,
    CompiledPredicate Eligibility,
    CompiledUtilityInput[] UtilityInputs,
    CompiledTraitModifier[] TraitModifiers,
    ActionControlDefinition Controls,
    CompiledEffect[] Effects,
    CompiledTargetSelector Target,
    ExecutorKind Executor,
    bool Fallback,
    FactDependencyMask Dependencies);

public sealed class CompiledIntentCatalog
{
    private readonly CompiledIntent[] _byIndex;
    private readonly Dictionary<int, ushort> _indexByHash;

    internal CompiledIntentCatalog(CompiledIntent[] intents, CompiledIntent fallback)
    {
        _byIndex = intents;
        Fallback = fallback;
        _indexByHash = intents.ToDictionary(intent => intent.Hash, intent => intent.RuntimeIndex);
        Candidates = IntentCandidateIndex.Build(intents, fallback.RuntimeIndex);
    }

    public IReadOnlyList<CompiledIntent> All => _byIndex;
    public CompiledIntent Fallback { get; }
    public IntentCandidateIndex Candidates { get; }
    public int Count => _byIndex.Length;
    public CompiledIntent this[int runtimeIndex] => _byIndex[runtimeIndex];
    public bool TryGetByHash(int hash, out CompiledIntent? intent)
    {
        if (_indexByHash.TryGetValue(hash, out var index)) { intent = _byIndex[index]; return true; }
        intent = null;
        return false;
    }
}

public static class IntentCompiler
{
    public static CompiledIntentCatalog Compile(
        IReadOnlyList<ActionDefinition> definitions,
        IReadOnlyList<TraitDefinition> traits,
        AgentAttributeSchema attributes)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count > ushort.MaxValue)
            throw Error("actions", $"contains more than {ushort.MaxValue} intents");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<int>();
        var traitBits = traits.ToDictionary(item => item.Id, item => item.Bit, StringComparer.OrdinalIgnoreCase);
        var facts = new FactRegistry(attributes);
        var compiled = new CompiledIntent[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            var path = $"actions[{index}]";
            if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
                throw Error($"{path}.id", "must be non-empty and unique");
            if (definition.Hash == 0 || !hashes.Add(definition.Hash))
                throw Error($"{path}.hash", "must be non-zero and unique");

            compiled[index] = CompileOne(definition, (ushort)index, path, traitBits, facts, attributes);
        }

        var fallbacks = compiled.Where(intent => intent.Fallback).ToArray();
        if (fallbacks.Length != 1)
            throw Error("actions", $"must designate exactly one fallback intent; found {fallbacks.Length}");
        return new CompiledIntentCatalog(compiled, fallbacks[0]);
    }

    private static CompiledIntent CompileOne(ActionDefinition definition, ushort index, string path,
        IReadOnlyDictionary<string, long> traitBits, FactRegistry facts, AgentAttributeSchema attributes)
    {
        CompiledPredicate eligibility;
        try { eligibility = CompiledPredicate.Compile(definition.Eligibility, facts); }
        catch (InvalidDataException exception) { throw Error($"{path}.eligibility", exception.Message, exception); }

        var inputs = definition.UtilityInputs.Select((input, inputIndex) =>
        {
            try { return new CompiledUtilityInput(CompiledNumericExpression.Compile(input.Expression, facts),
                input.Weight, input.Curve.ToArray()); }
            catch (InvalidDataException exception) { throw Error($"{path}.utilityInputs[{inputIndex}].expression", exception.Message, exception); }
        }).ToArray();
        var modifiers = definition.TraitModifiers.Select((modifier, modifierIndex) =>
        {
            if (!traitBits.TryGetValue(modifier.Trait, out var bit))
                throw Error($"{path}.traitModifiers[{modifierIndex}].trait", $"unknown trait '{modifier.Trait}'");
            return new CompiledTraitModifier(bit, modifier.Modifier);
        }).ToArray();
        var effects = definition.Effects.Select((effect, effectIndex) =>
        {
            try { return new CompiledEffect(attributes.GetIndex(effect.Attribute), effect.PerMinute); }
            catch (KeyNotFoundException exception) { throw Error($"{path}.effects[{effectIndex}].attribute", exception.Message, exception); }
        }).ToArray();
        var target = CompileTarget(definition.Target, path, facts);
        var executor = CompileExecutor(definition.Execution, target.Kind, path);
        if (definition.Fallback && (target.Kind != TargetKind.None || executor != ExecutorKind.Wait))
            throw Error(path, "fallback intent must use target kind 'none' and executor 'wait'");
        var dependencies = eligibility.Dependencies;
        foreach (var input in inputs) dependencies |= input.Expression.Dependencies;
        if (modifiers.Length > 0) dependencies |= new FactDependencyMask(FactDependencyCategory.Traits);
        dependencies |= TargetDependencies(target);
        return new CompiledIntent(definition.Id, definition.Name, definition.Hash, index, definition.Activity,
            definition.BaseUtility, eligibility, inputs, modifiers, definition.Controls, effects, target, executor,
            definition.Fallback, dependencies);
    }

    private static FactDependencyMask TargetDependencies(CompiledTargetSelector target)
    {
        if (target.Kind == TargetKind.None) return FactDependencyMask.None;
        if (target.Kind == TargetKind.Location) return new(FactDependencyCategory.Location);
        var mask = new FactDependencyMask(FactDependencyCategory.SocialTargets | FactDependencyCategory.TargetLocation);
        foreach (var requirement in target.Query!.Requirements) mask |= requirement.Dependencies;
        foreach (var rank in target.Query.RankBy) mask |= rank.Value.Dependencies;
        return mask;
    }

    private static CompiledTargetSelector CompileTarget(TargetDefinition target, string path, FactRegistry facts)
    {
        switch (target.Kind?.ToLowerInvariant())
        {
            case "none" when target.Value is null && target.Query is null:
                return new(TargetKind.None, LocationValue.None, null);
            case "location" when target.Query is null:
                var location = target.Value switch
                {
                    "agent.location.current" => LocationValue.Current,
                    "agent.location.home" => LocationValue.Home,
                    "agent.location.work" => LocationValue.Work,
                    _ => throw Error($"{path}.target.value", $"unsupported location reference '{target.Value}'")
                };
                return new(TargetKind.Location, location, null);
            case "entity" when target.Value is null && target.Query is not null:
                var query = target.Query;
                if (!string.Equals(query.Relation, "social", StringComparison.OrdinalIgnoreCase))
                    throw Error($"{path}.target.query.relation", $"unsupported relation '{query.Relation}'");
                if (query.Limit is <= 0) throw Error($"{path}.target.query.limit", "must be positive when provided");
                var requirements = query.Requirements.Select((requirement, i) =>
                {
                    try { return CompiledPredicate.Compile(requirement, facts); }
                    catch (InvalidDataException exception) { throw Error($"{path}.target.query.requirements[{i}]", exception.Message, exception); }
                }).ToArray();
                var ranks = query.RankBy.Select((rank, i) =>
                {
                    var order = rank.Order switch { "ascending" => SortOrder.Ascending, "descending" => SortOrder.Descending,
                        _ => throw Error($"{path}.target.query.rankBy[{i}].order", "must be 'ascending' or 'descending'") };
                    try { return new CompiledTargetRank(CompiledNumericExpression.Compile(rank.Value, facts), order); }
                    catch (InvalidDataException exception) { throw Error($"{path}.target.query.rankBy[{i}].value", exception.Message, exception); }
                }).ToArray();
                if (ranks.Length == 0) throw Error($"{path}.target.query.rankBy", "must contain at least one ranking");
                return new(TargetKind.Entity, LocationValue.None, new(requirements, ranks, query.Limit));
            default:
                throw Error($"{path}.target", $"invalid target kind '{target.Kind}' or incompatible fields");
        }
    }

    private static ExecutorKind CompileExecutor(ExecutorDefinition execution, TargetKind target, string path)
    {
        var executor = execution.Executor?.ToLowerInvariant() switch
        {
            "performhere" => ExecutorKind.PerformHere, "performatlocation" => ExecutorKind.PerformAtLocation,
            "performwithentity" => ExecutorKind.PerformWithEntity, "wait" => ExecutorKind.Wait,
            _ => throw Error($"{path}.execution.executor", $"unsupported executor '{execution.Executor}'")
        };
        var needsTarget = executor is ExecutorKind.PerformAtLocation or ExecutorKind.PerformWithEntity;
        if (needsTarget != (execution.Destination == "intent.target"))
            throw Error($"{path}.execution.destination", needsTarget ? "must be 'intent.target'" : "must be omitted");
        if (executor == ExecutorKind.PerformAtLocation && target != TargetKind.Location ||
            executor == ExecutorKind.PerformWithEntity && target != TargetKind.Entity ||
            executor is ExecutorKind.PerformHere or ExecutorKind.Wait && target != TargetKind.None)
            throw Error($"{path}.execution.executor", $"is incompatible with target kind '{target}'");
        return executor;
    }

    private static InvalidDataException Error(string path, string message, Exception? inner = null) =>
        new($"actions.json:{path}: {message}", inner);
}
