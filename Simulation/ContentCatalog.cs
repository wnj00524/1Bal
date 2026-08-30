using System.Text.Json;

namespace ProxyState.Simulation;

public sealed record TraitDefinition(string Id, string Name, long Bit, float Prevalence);
public sealed record ResponsePoint(float X, float Y);
public sealed record UtilityInputDefinition(NumericExpressionDefinition Expression, float Weight, List<ResponsePoint> Curve)
{
    public CompiledNumericExpression? CompiledExpression { get; internal set; }
}
public sealed record TraitUtilityModifier(string Trait, float Modifier);
public sealed record ActionControlDefinition(
    int MinimumCommitmentMinutes,
    float SwitchingThreshold,
    int CooldownMinutes,
    float UrgentPreemptionThreshold,
    bool CooldownOnExit = true);
public sealed record ActionEffectDefinition(string Attribute, float PerMinute);
public sealed record TargetRankDefinition(NumericExpressionDefinition Value, string Order)
{
    public CompiledNumericExpression? CompiledValue { get; internal set; }
}
public sealed record TargetQueryDefinition(
    string Relation,
    List<PredicateDefinition> Requirements,
    List<TargetRankDefinition> RankBy,
    int? Limit);
public sealed record TargetDefinition(string Kind, string? Value, TargetQueryDefinition? Query);
public sealed record ActionDefinition(
    string Id,
    string Name,
    int Hash,
    float BaseUtility,
    PredicateDefinition Eligibility,
    List<UtilityInputDefinition> UtilityInputs,
    List<TraitUtilityModifier> TraitModifiers,
    ActionControlDefinition Controls,
    List<ActionEffectDefinition> Effects,
    TargetDefinition Target);
public sealed record SecretStateDefinition(string Id, string Name, int Hash);
public sealed record FactionDefinition(string Id, string Name, byte FactionId);
public sealed record AgentAttributeDefinition(string Id, float Min, float Max, float Average);
public sealed record JobDefinition(
    string Id,
    string Name,
    int Hash,
    int WorkStartMinute,
    int WorkEndMinute,
    List<int> WorkDays,
    string WorkplaceType);
public sealed record WorldLocationDefinition(string Id, string Name, int Hash, string Type);
public sealed record WorldConnectionDefinition(string From, string To, int TravelMinutes);

public sealed class AgentAttributeSchema
{
    private readonly Dictionary<string, int> _indices;

    internal AgentAttributeSchema(IReadOnlyList<AgentAttributeDefinition> definitions)
    {
        Definitions = definitions;
        _indices = definitions
            .Select((definition, index) => new { definition.Id, index })
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AgentAttributeDefinition> Definitions { get; }

    public int Count => Definitions.Count;

    public int GetIndex(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _indices.TryGetValue(id, out var index)
            ? index
            : throw new KeyNotFoundException($"Agent attribute '{id}' is not defined in the schema.");
    }
}

public sealed class AgentSchemaDocument
{
    public List<AgentAttributeDefinition>? Attributes { get; init; }
}

public sealed class WorldDocument
{
    public List<WorldLocationDefinition>? Locations { get; init; }
    public List<WorldConnectionDefinition>? Connections { get; init; }
}

public sealed class ContentCatalog
{
    private ContentCatalog(
        IReadOnlyList<TraitDefinition> traits,
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<SecretStateDefinition> secretStates,
        IReadOnlyList<FactionDefinition> factions,
        AgentAttributeSchema agentAttributes,
        IReadOnlyList<JobDefinition> jobs,
        WorldTopology world,
        AgentNetworkCatalog networks)
    {
        Traits = traits;
        Actions = actions;
        SecretStates = secretStates;
        Factions = factions;
        AgentAttributes = agentAttributes;
        Jobs = jobs;
        World = world;
        Networks = networks;
        AllTraitBits = traits.Aggregate(0L, (mask, trait) => mask | trait.Bit);
    }

    public IReadOnlyList<TraitDefinition> Traits { get; }
    public IReadOnlyList<ActionDefinition> Actions { get; }
    public IReadOnlyList<SecretStateDefinition> SecretStates { get; }
    public IReadOnlyList<FactionDefinition> Factions { get; }
    public AgentAttributeSchema AgentAttributes { get; }
    public IReadOnlyList<JobDefinition> Jobs { get; }
    public WorldTopology World { get; }
    public AgentNetworkCatalog Networks { get; }
    public long AllTraitBits { get; }

    public static ContentCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var traits = LoadFile<TraitDefinition>(directory, "traits.json", options);
        var actions = LoadFile<ActionDefinition>(directory, "actions.json", options);
        var secretStates = LoadFile<SecretStateDefinition>(directory, "secret-states.json", options);
        var factions = LoadFile<FactionDefinition>(directory, "factions.json", options);
        var schemaDocument = LoadObject<AgentSchemaDocument>(directory, "agent-schema.json", options);
        var jobs = LoadFile<JobDefinition>(directory, "jobs.json", options);
        var worldDocument = LoadObject<WorldDocument>(directory, "world.json", options);
        var networksPath = Path.Combine(directory, "networks.json");
        if (!File.Exists(networksPath))
            throw new FileNotFoundException($"Required content file was not found: {networksPath}", networksPath);

        ValidateSecretStates(secretStates);
        var agentAttributes = Validate(traits, actions, factions, schemaDocument.Attributes);
        var world = ValidateWorld(jobs, worldDocument.Locations, worldDocument.Connections);
        var networks = AgentNetworkCatalog.Load(networksPath, options);
        return new ContentCatalog(traits, actions, secretStates, factions, agentAttributes, jobs, world, networks);
    }

    private static IReadOnlyList<T> LoadFile<T>(
        string directory,
        string fileName,
        JsonSerializerOptions options)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required content file was not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<T>>(stream, options)
            ?? throw new InvalidDataException($"Content file is empty or invalid: {path}");
    }

    private static T LoadObject<T>(
        string directory,
        string fileName,
        JsonSerializerOptions options)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required content file was not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, options)
            ?? throw new InvalidDataException($"Content file is empty or invalid: {path}");
    }

    private static AgentAttributeSchema Validate(
        IReadOnlyList<TraitDefinition> traits,
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<FactionDefinition> factions,
        IReadOnlyList<AgentAttributeDefinition>? attributeDefinitions)
    {
        if (traits.Count == 0 || actions.Count == 0 || factions.Count == 0)
        {
            throw new InvalidDataException("Traits, actions, and factions must each contain at least one definition.");
        }

        if (attributeDefinitions is null || attributeDefinitions.Count == 0)
        {
            throw new InvalidDataException("The agent attribute schema must contain at least one attribute.");
        }

        var traitBits = new HashSet<long>();
        var traitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trait in traits)
        {
            if (string.IsNullOrWhiteSpace(trait.Id) || !traitIds.Add(trait.Id))
            {
                throw new InvalidDataException($"Trait IDs must be non-empty and unique; '{trait.Id}' is invalid or duplicated.");
            }

            if (trait.Bit <= 0 || (trait.Bit & (trait.Bit - 1)) != 0 || !traitBits.Add(trait.Bit))
            {
                throw new InvalidDataException($"Trait '{trait.Id}' must have a unique positive single-bit value.");
            }

            if (!float.IsFinite(trait.Prevalence) || trait.Prevalence is < 0f or > 1f)
            {
                throw new InvalidDataException($"Trait '{trait.Id}' prevalence must be a finite value between 0 and 1.");
            }
        }

        if (factions.Select(faction => faction.FactionId).Distinct().Count() != factions.Count)
        {
            throw new InvalidDataException("Faction IDs must be unique.");
        }

        if (actions.Select(action => action.Hash).Distinct().Count() != actions.Count)
        {
            throw new InvalidDataException("Action hashes must be unique.");
        }

        ValidateActions(actions, traits, attributeDefinitions);

        var attributeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in attributeDefinitions)
        {
            if (string.IsNullOrWhiteSpace(attribute.Id) || !attributeIds.Add(attribute.Id))
            {
                throw new InvalidDataException($"Agent attribute IDs must be non-empty and unique; '{attribute.Id}' is invalid or duplicated.");
            }

            if (!float.IsFinite(attribute.Min) || !float.IsFinite(attribute.Max) || !float.IsFinite(attribute.Average))
            {
                throw new InvalidDataException($"Agent attribute '{attribute.Id}' must contain only finite numeric values.");
            }

            if (attribute.Min > attribute.Max || attribute.Average < attribute.Min || attribute.Average > attribute.Max)
            {
                throw new InvalidDataException($"Agent attribute '{attribute.Id}' must satisfy min <= average <= max.");
            }
        }

        var schema = new AgentAttributeSchema(attributeDefinitions);
        _ = schema.GetIndex("fatigue");
        _ = schema.GetIndex("stress");
        CompileDecisionExpressions(actions, schema);
        return schema;
    }

    private static void CompileDecisionExpressions(IReadOnlyList<ActionDefinition> actions, AgentAttributeSchema schema)
    {
        var facts = new FactRegistry(schema);
        foreach (var action in actions)
        {
            try
            {
                action.Eligibility.CompiledPredicate = CompiledPredicate.Compile(action.Eligibility, facts);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"Action '{action.Id}' has an invalid eligibility predicate: {exception.Message}", exception);
            }
            foreach (var input in action.UtilityInputs)
            {
                try
                {
                    input.CompiledExpression = CompiledNumericExpression.Compile(input.Expression, facts);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException($"Action '{action.Id}' has an invalid numeric expression: {exception.Message}", exception);
                }
            }
            if (action.Target.Query is not null)
            {
                foreach (var requirement in action.Target.Query.Requirements)
                    try
                    {
                        requirement.CompiledPredicate = CompiledPredicate.Compile(requirement, facts);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new InvalidDataException($"Action '{action.Id}' has an invalid target requirement: {exception.Message}", exception);
                    }
                foreach (var rank in action.Target.Query.RankBy)
                    try
                    {
                        rank.CompiledValue = CompiledNumericExpression.Compile(rank.Value, facts);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new InvalidDataException($"Action '{action.Id}' has an invalid target ranking: {exception.Message}", exception);
                    }
            }
        }
    }

    private static void ValidateActions(
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<TraitDefinition> traits,
        IReadOnlyList<AgentAttributeDefinition> attributes)
    {
        var required = new[] { "work", "rest", "socialize" };
        if (required.Any(id => actions.Count(action => string.Equals(action.Id, id, StringComparison.OrdinalIgnoreCase)) != 1))
            throw new InvalidDataException("Actions must define exactly one work, rest, and socialize candidate.");

        var attributeIds = attributes.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var traitIds = traits.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id) || string.IsNullOrWhiteSpace(action.Name) ||
                !float.IsFinite(action.BaseUtility) || action.Eligibility is null ||
                action.UtilityInputs is null || action.TraitModifiers is null || action.Controls is null || action.Effects is null || action.Target is null)
                throw new InvalidDataException($"Action '{action.Id}' has an invalid decision definition.");
            ValidateTarget(action);
            if (action.Controls.MinimumCommitmentMinutes < 0 || action.Controls.CooldownMinutes < 0 ||
                !float.IsFinite(action.Controls.SwitchingThreshold) || action.Controls.SwitchingThreshold < 0 ||
                !float.IsFinite(action.Controls.UrgentPreemptionThreshold))
                throw new InvalidDataException($"Action '{action.Id}' has invalid controls.");
            foreach (var input in action.UtilityInputs)
            {
                if (input.Expression is null || !float.IsFinite(input.Weight) || input.Curve is null || input.Curve.Count < 2 ||
                    input.Curve.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)) ||
                    input.Curve.Select(point => point.X).Zip(input.Curve.Skip(1), (x, next) => next.X > x).Any(increasing => !increasing))
                    throw new InvalidDataException($"Action '{action.Id}' has an invalid utility input.");
            }
            if (action.TraitModifiers.Any(modifier => !traitIds.Contains(modifier.Trait) || !float.IsFinite(modifier.Modifier)))
                throw new InvalidDataException($"Action '{action.Id}' references an invalid trait modifier.");
            if (action.Effects.Any(effect => !attributeIds.Contains(effect.Attribute) || !float.IsFinite(effect.PerMinute)))
                throw new InvalidDataException($"Action '{action.Id}' references an invalid effect attribute.");
        }
    }

    private static void ValidateTarget(ActionDefinition action)
    {
        var target = action.Target;
        switch (target.Kind?.ToLowerInvariant())
        {
            case "none":
                if (target.Value is not null || target.Query is not null)
                    throw new InvalidDataException($"Action '{action.Id}' target kind 'none' cannot define value or query.");
                break;
            case "location":
                if (target.Value is not ("agent.location.home" or "agent.location.work" or "agent.location.current") || target.Query is not null)
                    throw new InvalidDataException($"Action '{action.Id}' location target must use a supported direct agent location value.");
                break;
            case "entity":
                if (target.Value is not null || target.Query is null ||
                    !string.Equals(target.Query.Relation, "social", StringComparison.OrdinalIgnoreCase) ||
                    target.Query.Requirements is null || target.Query.RankBy is null ||
                    target.Query.RankBy.Count == 0 || target.Query.Limit is <= 0)
                    throw new InvalidDataException($"Action '{action.Id}' entity target must define a valid social query, ranking, and optional positive limit.");
                if (target.Query.RankBy.Any(rank => rank.Value is null ||
                    rank.Order is not ("ascending" or "descending")))
                    throw new InvalidDataException($"Action '{action.Id}' target ranking must use ascending or descending order.");
                break;
            default:
                throw new InvalidDataException($"Action '{action.Id}' has unsupported target kind '{target.Kind}'.");
        }
    }

    private static void ValidateSecretStates(IReadOnlyList<SecretStateDefinition> secretStates)
    {
        if (secretStates.Count == 0)
        {
            throw new InvalidDataException("At least one secret-state definition is required.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<int>();
        foreach (var secretState in secretStates)
        {
            if (string.IsNullOrWhiteSpace(secretState.Id) || !ids.Add(secretState.Id))
            {
                throw new InvalidDataException(
                    $"Secret-state IDs must be non-empty and unique; '{secretState.Id}' is invalid or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(secretState.Name))
            {
                throw new InvalidDataException($"Secret state '{secretState.Id}' must have a name.");
            }

            if (!hashes.Add(secretState.Hash))
            {
                throw new InvalidDataException(
                    $"Secret-state hashes must be unique; '{secretState.Hash}' is duplicated.");
            }
        }

        // Hash zero keeps a default-initialized AgentState safe even before a
        // system or content assignment supplies a covert activity.
        var noneStates = secretStates
            .Where(secretState => string.Equals(secretState.Id, "none", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (noneStates.Length != 1 || noneStates[0].Hash != 0)
        {
            throw new InvalidDataException("A unique 'none' secret-state definition with hash 0 is required.");
        }
    }

    private static WorldTopology ValidateWorld(
        IReadOnlyList<JobDefinition> jobs,
        IReadOnlyList<WorldLocationDefinition>? locations,
        IReadOnlyList<WorldConnectionDefinition>? connections)
    {
        if (jobs.Count == 0)
        {
            throw new InvalidDataException("At least one job definition is required.");
        }

        var jobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobHashes = new HashSet<int>();
        foreach (var job in jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Id) || !jobIds.Add(job.Id))
            {
                throw new InvalidDataException($"Job IDs must be non-empty and unique; '{job.Id}' is invalid or duplicated.");
            }

            if (!jobHashes.Add(job.Hash))
            {
                throw new InvalidDataException($"Job hashes must be unique; '{job.Hash}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.WorkplaceType))
            {
                throw new InvalidDataException($"Job '{job.Id}' must have a name and workplace type.");
            }

            if (job.WorkStartMinute < 0 || job.WorkEndMinute > SimulationDefaults.SimulationMinutesPerDay ||
                job.WorkStartMinute >= job.WorkEndMinute)
            {
                throw new InvalidDataException($"Job '{job.Id}' must define a non-overnight interval within a day.");
            }

            if (job.WorkDays is null || job.WorkDays.Count == 0 ||
                job.WorkDays.Any(day => day < 1 || day > SimulationDefaults.DaysPerWeek) ||
                job.WorkDays.Distinct().Count() != job.WorkDays.Count)
            {
                throw new InvalidDataException($"Job '{job.Id}' must define unique workdays from 1 through 7.");
            }
        }

        if (locations is null || locations.Count == 0)
        {
            throw new InvalidDataException("The world must contain at least one location.");
        }

        var locationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locationHashes = new HashSet<int>();
        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location.Id) || !locationIds.Add(location.Id))
            {
                throw new InvalidDataException($"Location IDs must be non-empty and unique; '{location.Id}' is invalid or duplicated.");
            }

            if (!locationHashes.Add(location.Hash))
            {
                throw new InvalidDataException($"Location hashes must be unique; '{location.Hash}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(location.Name) || string.IsNullOrWhiteSpace(location.Type))
            {
                throw new InvalidDataException($"Location '{location.Id}' must have a name and type.");
            }
        }

        if (!locations.Any(location => string.Equals(
                location.Type,
                SimulationDefaults.ResidentialLocationType,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The world must contain at least one residential location.");
        }

        var locationTypes = locations
            .Select(location => location.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            if (!locationTypes.Contains(job.WorkplaceType))
            {
                throw new InvalidDataException($"Job '{job.Id}' requires unavailable workplace type '{job.WorkplaceType}'.");
            }
        }

        if (connections is null || connections.Count == 0)
        {
            throw new InvalidDataException("The world must contain at least one connection.");
        }

        var locationById = locations.ToDictionary(location => location.Id, StringComparer.OrdinalIgnoreCase);
        var connectionPairs = new HashSet<(int From, int To)>();
        foreach (var connection in connections)
        {
            if (string.IsNullOrWhiteSpace(connection.From) || string.IsNullOrWhiteSpace(connection.To) ||
                string.Equals(connection.From, connection.To, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("World connections must connect two different locations.");
            }

            if (!locationById.TryGetValue(connection.From, out var from) ||
                !locationById.TryGetValue(connection.To, out var to))
            {
                throw new InvalidDataException($"World connection '{connection.From}' -> '{connection.To}' references an unknown location.");
            }

            if (connection.TravelMinutes <= 0)
            {
                throw new InvalidDataException("World connection travel durations must be positive.");
            }

            var pair = from.Hash < to.Hash
                ? (from.Hash, to.Hash)
                : (to.Hash, from.Hash);
            if (!connectionPairs.Add(pair))
            {
                throw new InvalidDataException($"World connection '{connection.From}' -> '{connection.To}' is duplicated.");
            }
        }

        return new WorldTopology(locations, connections);
    }
}
