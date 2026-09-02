using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace ProxyState.Simulation;

// Mutation-owning systems use these helpers instead of duplicating knowledge
// about which decision facts an ECS field represents.
public static class DecisionInvalidation
{
    public static void Signal(ref DecisionState state, FactDependencyMask changed)
    {
        state.ChangedFacts |= changed;
        state.Dirty = true;
    }

    public static void SignalAttribute(ref DecisionState state, int attributeIndex) => Signal(ref state,
        new(FactDependencyCategory.Attributes, attributeIndex is >= 0 and < 64 ? 1UL << attributeIndex : ulong.MaxValue));
    public static void SignalLocation(ref DecisionState state) => Signal(ref state,
        new(FactDependencyCategory.Location | FactDependencyCategory.Travel | FactDependencyCategory.TargetLocation));
    public static void SignalTargetAvailability(ref DecisionState state) => Signal(ref state,
        new(FactDependencyCategory.SocialTargets | FactDependencyCategory.NetworkTargets |
            FactDependencyCategory.TargetAffinity | FactDependencyCategory.TargetAttributes |
            FactDependencyCategory.TargetLocation | FactDependencyCategory.Coordination));
}

internal static class DecisionUtility
{
    public static float Evaluate(float baseUtility, IReadOnlyList<CompiledUtilityInput> inputs,
        IReadOnlyList<CompiledTraitModifier> modifiers, long traitMask, in DecisionFactContext facts)
    {
        var score = baseUtility;
        foreach (var input in inputs)
            score += input.Weight * Curve(input.Curve, input.Expression.Evaluate(facts));
        foreach (var modifier in modifiers)
            if ((traitMask & modifier.TraitBit) != 0) score += modifier.Modifier;
        return score;
    }

    public static float Curve(IReadOnlyList<ResponsePoint> points, float value)
    {
        if (value <= points[0].X) return points[0].Y;
        for (var index = 1; index < points.Count; index++)
        {
            if (value > points[index].X) continue;
            var previous = points[index - 1];
            var amount = (value - previous.X) / (points[index].X - previous.X);
            return previous.Y + amount * (points[index].Y - previous.Y);
        }
        return points[^1].Y;
    }
}

// Target resolution and utility scoring operate entirely from compiled content.
// The winner application consequently copies a generic result into ECS state.
public sealed class AgentDecisionSystem : QuerySystem<Identity, AgentAttributes, Psychology, AgentLocation, AgentTravel>
{
    private readonly EntityStore _store;
    private readonly Entity _clock;
    private readonly Dictionary<int, JobDefinition> _jobs;
    private readonly CandidateEvaluator?[] _candidatesByIndex;
    private readonly Dictionary<int, CandidateEvaluator> _candidatesByHash;
    private readonly IntentCandidateIndex _candidateIndex;
    private readonly CompiledIntent _fallback;
    private readonly bool _captureDiagnostics;
    private readonly SimulationWorkDiagnostics? _workDiagnostics;

    public AgentDecisionSystem(EntityStore store, ContentCatalog catalog, Entity clock, bool captureDiagnostics = false,
        SimulationWorkDiagnostics? workDiagnostics = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _jobs = catalog.Jobs.ToDictionary(job => job.Hash);
        _fallback = catalog.Intents.Fallback;
        _captureDiagnostics = captureDiagnostics;
        _workDiagnostics = workDiagnostics;
        _candidateIndex = catalog.Intents.Candidates;
        _candidatesByIndex = new CandidateEvaluator?[catalog.Intents.Count];
        _candidatesByHash = new();
        foreach (var intent in catalog.Intents.All.Where(intent => !intent.Fallback))
        {
            var evaluator = new CandidateEvaluator(intent);
            _candidatesByIndex[intent.RuntimeIndex] = evaluator;
            _candidatesByHash.Add(intent.Hash, evaluator);
        }
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var time = _clock.GetComponent<WorldTime>();
        var minute = (long)Math.Floor(time.ElapsedSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        var targets = new TargetResolver(_store, _workDiagnostics);

        Query.ForEachEntity((ref Identity identity, ref AgentAttributes attributes, ref Psychology psychology,
            ref AgentLocation location, ref AgentTravel travel, Entity entity) =>
        {
            if (!_jobs.TryGetValue(identity.OccupationId, out var job)) return;
            ref var intention = ref entity.GetComponent<IntentionState>();
            ref var decision = ref entity.GetComponent<DecisionState>();
            var currentActionHash = intention.ActionHash;
            var context = new DecisionContext(time, job, attributes.Values, psychology.TraitMask, location, travel);

            // Re-resolving only the active definition makes target loss an immediate,
            // data-driven invalidation rather than a special case for an action ID.
            _candidatesByHash.TryGetValue(currentActionHash, out var active);
            if (active is not null)
            {
                var selected = targets.Resolve(entity.Id, active.Definition.Target, context);
                if (selected.EntityId != intention.TargetEntityId || selected.LocationId != intention.TargetLocationId)
                    DecisionInvalidation.Signal(ref decision, new(FactDependencyCategory.SocialTargets |
                        FactDependencyCategory.TargetLocation));
            }
            if (!decision.Dirty && decision.LastConsideredMinute >= minute) return;

            EnsureCache(ref decision);
            if (_captureDiagnostics) EnsureDiagnosticCache(ref decision);
            var fullPass = decision.LastConsideredMinute < minute || decision.ChangedFacts == FactDependencyMask.None;
            var changed = fullPass ? FactDependencyMask.All : decision.ChangedFacts;
            _workDiagnostics?.RecordDecisionPass();
            var candidateContext = new IntentCandidateContext(true, location.HomeLocationId != 0,
                location.WorkLocationId != 0, targets.HasSocialRelations(entity.Id),
                targets.HasNetworkRelations(entity.Id));
            decision.Dirty = false;
            decision.ChangedFacts = FactDependencyMask.None;
            decision.LastConsideredMinute = minute;
            foreach (var runtimeIndex in _candidateIndex.EnumerateCandidates(candidateContext))
            {
                var candidate = _candidatesByIndex[runtimeIndex]!;
                if (!fullPass && !candidate.Definition.Dependencies.Intersects(changed)) continue;
                var result = candidate.Evaluate(context, targets.Resolve(entity.Id, candidate.Definition.Target, context),
                    _captureDiagnostics ? decision.CachedUtilityContributions[candidate.Definition.RuntimeIndex] : null,
                    _captureDiagnostics ? decision.CachedTraitContributions[candidate.Definition.RuntimeIndex] : null);
                _workDiagnostics?.RecordCandidateEvaluation();
                var index = candidate.Definition.RuntimeIndex;
                decision.CachedScores[index] = result.Score;
                decision.CachedEligibility[index] = result.Eligible;
                decision.CachedTargetEntityIds[index] = result.TargetEntityId;
                decision.CachedTargetLocationIds[index] = result.TargetLocationId;
                if (_captureDiagnostics)
                    decision.CachedRejectedPredicates[index] = result.Eligible
                        ? string.Empty : $"actions.json:intent '{candidate.Definition.Id}':eligibility";
                decision.EvaluationCount++;
            }
            var winner = new DecisionResult(_fallback.RuntimeIndex, _fallback, true, _fallback.BaseUtility, 0, 0);
            DecisionResult current = default;
            foreach (var runtimeIndex in _candidateIndex.EnumerateCandidates(candidateContext))
            {
                var result = CachedResult(_candidatesByIndex[runtimeIndex]!.Definition, decision);
                if (!result.Eligible || IsCoolingDown(result.Action.Hash, minute, decision)) continue;
                if (result.Action.Hash == currentActionHash) current = result;
                if (winner.Action.Fallback || result.Score > winner.Score ||
                    result.Score == winner.Score && result.Action.Hash < winner.Action.Hash) winner = result;
            }

            if (entity.HasComponent<CoordinationState>())
            {
                ref var coordination = ref entity.GetComponent<CoordinationState>();
                if (coordination.Active)
                {
                    var elapsed = coordination.StartedAtMinute < 0 ? 0 : minute - coordination.StartedAtMinute;
                    var minimumElapsed = coordination.StartedAtMinute >= 0 &&
                        elapsed >= coordination.MinimumDurationMinutes;
                    var alternative = winner.Action.Hash != coordination.ActionHash;
                    var urgent = winner.Score >= winner.Action.Controls.UrgentPreemptionThreshold;
                    var beatsCoordination = winner.Score >= coordination.Utility +
                        winner.Action.Controls.SwitchingThreshold;
                    if (elapsed >= coordination.MaximumDurationMinutes ||
                        minimumElapsed && alternative && (urgent || beatsCoordination))
                        coordination.ReleaseRequested = true;
                    return;
                }
            }
            if (currentActionHash != 0 && winner.Action.Hash != currentActionHash)
            {
                var currentDefinition = active?.Definition;
                var committed = currentDefinition is not null && minute - intention.SelectedAtMinute < currentDefinition.Controls.MinimumCommitmentMinutes;
                var currentScore = current.Action is null ? float.NegativeInfinity : current.Score;
                var urgent = winner.Score >= winner.Action.Controls.UrgentPreemptionThreshold;
                var switchingMargin = currentDefinition?.Controls.SwitchingThreshold ?? winner.Action.Controls.SwitchingThreshold;
                if (!urgent && (committed || winner.Score < currentScore + switchingMargin)) return;
                if (currentDefinition?.Controls.CooldownOnExit == true) SetCooldown(currentDefinition, minute, ref decision);
            }

            if (winner.Action.Hash == intention.ActionHash &&
                winner.TargetEntityId == intention.TargetEntityId && winner.TargetLocationId == intention.TargetLocationId) return;
            intention.ActionHash = winner.Action.Hash;
            intention.TargetEntityId = winner.TargetEntityId;
            intention.TargetLocationId = winner.TargetLocationId;
            intention.SelectedAtMinute = minute;
            intention.Utility = winner.Score;
        });
    }

    private void EnsureCache(ref DecisionState state)
    {
        var count = _candidatesByIndex.Length;
        if (state.CachedScores?.Length == count && state.CachedEligibility?.Length == count &&
            state.CachedTargetEntityIds?.Length == count && state.CachedTargetLocationIds?.Length == count) return;
        state.CachedScores = new float[count]; state.CachedEligibility = new bool[count];
        state.CachedTargetEntityIds = new int[count]; state.CachedTargetLocationIds = new int[count];
    }

    private void EnsureDiagnosticCache(ref DecisionState state)
    {
        var count = _candidatesByIndex.Length;
        if (state.CachedUtilityContributions?.Length == count &&
            state.CachedTraitContributions?.Length == count && state.CachedRejectedPredicates?.Length == count) return;
        state.CachedUtilityContributions = new float[count][];
        state.CachedTraitContributions = new float[count][];
        state.CachedRejectedPredicates = new string[count];
        foreach (var candidate in _candidatesByIndex)
        {
            if (candidate is null) continue;
            var index = candidate.Definition.RuntimeIndex;
            state.CachedUtilityContributions[index] = new float[candidate.Definition.UtilityInputs.Length];
            state.CachedTraitContributions[index] = new float[candidate.Definition.TraitModifiers.Length];
        }
    }

    private static DecisionResult CachedResult(CompiledIntent intent, DecisionState state)
    {
        var index = intent.RuntimeIndex;
        return new(index, intent, state.CachedEligibility[index], state.CachedScores[index],
            state.CachedTargetEntityIds[index], state.CachedTargetLocationIds[index]);
    }

    private static bool IsCoolingDown(int hash, long minute, DecisionState state)
    {
        if (state.CooldownActionHashes is null || state.CooldownUntilMinutes is null) return false;
        for (var index = 0; index < state.CooldownActionHashes.Length; index++)
            if (state.CooldownActionHashes[index] == hash && state.CooldownUntilMinutes[index] > minute) return true;
        return false;
    }

    private static void SetCooldown(CompiledIntent action, long minute, ref DecisionState state)
    {
        if (action.Controls.CooldownMinutes == 0) return;
        var index = Array.IndexOf(state.CooldownActionHashes, action.Hash);
        if (index < 0) index = Array.IndexOf(state.CooldownActionHashes, 0);
        if (index < 0) return;
        state.CooldownActionHashes[index] = action.Hash;
        state.CooldownUntilMinutes[index] = minute + action.Controls.CooldownMinutes;
    }

    internal readonly record struct DecisionResult(int IntentIndex, CompiledIntent Action, bool Eligible,
        float Score, int TargetEntityId, int TargetLocationId);
    internal readonly record struct TargetSelection(int EntityId, int LocationId, float Affinity,
        float[]? Attributes = null);
    internal readonly record struct DecisionContext(WorldTime Time, JobDefinition Job, float[] Attributes,
        long TraitMask, AgentLocation Location, AgentTravel Travel);

    internal sealed class TargetResolver
    {
        private readonly Dictionary<int, int> _locations;
        private readonly Dictionary<int, float[]> _attributes;
        private readonly Dictionary<int, List<(int Id, float Affinity)>> _social;
        private readonly Dictionary<(int Source, int Target), float> _affinities;
        private readonly Dictionary<int, List<NetworkLink>> _memberships;
        private readonly Dictionary<int, List<int>> _networkMembers;
        private readonly SimulationWorkDiagnostics? _diagnostics;

        private readonly record struct NetworkLink(int NetworkId, int TypeHash, int SupervisorId);

        public TargetResolver(EntityStore store, SimulationWorkDiagnostics? diagnostics = null)
        {
            _diagnostics = diagnostics;
            _locations = new();
            foreach (var entity in store.Query<AgentLocation>().Entities)
            {
                diagnostics?.RecordTargetPopulationVisit();
                diagnostics?.RecordTransientOperation();
                _locations.Add(entity.Id, entity.GetComponent<AgentLocation>().CurrentLocationId);
            }
            _attributes = new();
            foreach (var entity in store.Query<AgentAttributes>().Entities)
            {
                diagnostics?.RecordTargetPopulationVisit();
                diagnostics?.RecordTransientOperation();
                _attributes.Add(entity.Id, entity.GetComponent<AgentAttributes>().Values);
            }
            _social = new();
            _affinities = new();
            foreach (var edgeEntity in store.Query<EdgeData>().Entities)
            {
                diagnostics?.RecordEdgeVisit();
                var edge = edgeEntity.GetComponent<EdgeData>();
                if (!_social.TryGetValue(edge.Source.Id, out var edges))
                {
                    _social[edge.Source.Id] = edges = new();
                    diagnostics?.RecordTransientOperation();
                }
                var affinity = Math.Clamp((edge.Affinity + 100f) / 200f, 0f, 1f);
                edges.Add((edge.Target.Id, affinity));
                _affinities[(edge.Source.Id, edge.Target.Id)] = affinity;
                diagnostics?.RecordTransientOperation(2);
            }

            _memberships = new();
            _networkMembers = new();
            foreach (var agent in store.Query<Identity>().Entities)
            {
                diagnostics?.RecordTargetPopulationVisit();
                foreach (var membership in agent.GetRelations<AgentNetworkMembership>())
                {
                    if (membership.Network.IsNull ||
                        !membership.Network.TryGetComponent<AgentNetworkData>(out var network)) continue;
                    if (!_memberships.TryGetValue(agent.Id, out var links))
                    {
                        _memberships[agent.Id] = links = new();
                        diagnostics?.RecordTransientOperation();
                    }
                    links.Add(new NetworkLink(membership.Network.Id, network.TypeHash,
                        membership.Supervisor.IsNull ? 0 : membership.Supervisor.Id));
                    if (!_networkMembers.TryGetValue(membership.Network.Id, out var members))
                    {
                        _networkMembers[membership.Network.Id] = members = new();
                        diagnostics?.RecordTransientOperation();
                    }
                    members.Add(agent.Id);
                    diagnostics?.RecordTransientOperation(2);
                }
            }
        }

        public TargetSelection Resolve(int actorId, CompiledTargetSelector definition, DecisionContext context)
        {
            if (definition.Kind == TargetKind.None) return default;
            if (definition.Kind == TargetKind.Location)
                return new TargetSelection(0, definition.Location switch
                {
                    LocationValue.Home => context.Location.HomeLocationId,
                    LocationValue.Work => context.Location.WorkLocationId,
                    LocationValue.Current => context.Location.CurrentLocationId,
                    _ => 0
                }, 0);
            var query = definition.Query!;
            var candidates = Enumerate(actorId, query);
            (TargetSelection Target, float[] Ranks)? best = null;
            foreach (var candidate in candidates)
            {
                if (!_locations.TryGetValue(candidate.Id, out var targetLocation) ||
                    !_attributes.TryGetValue(candidate.Id, out var targetAttributes)) continue;
                var facts = new DecisionFactContext(context.Time, context.Job, context.Attributes,
                    context.Location, context.Travel, candidate.Id, candidate.Affinity, targetLocation,
                    targetAttributes);
                if (query.Requirements.Any(requirement => !requirement.Evaluate(facts))) continue;
                var ranks = query.RankBy.Select(rank => rank.Value.Evaluate(facts)).ToArray();
                _diagnostics?.RecordTransientOperation();
                var target = new TargetSelection(candidate.Id, targetLocation, candidate.Affinity, targetAttributes);
                if (best is null || Compare(ranks, candidate.Id, best.Value.Ranks, best.Value.Target.EntityId, query.RankBy) < 0)
                    best = (target, ranks);
            }
            return best?.Target ?? default;
        }

        public bool HasSocialRelations(int actorId) => _social.ContainsKey(actorId);
        public bool HasNetworkRelations(int actorId) => _memberships.ContainsKey(actorId);

        public bool IsRelated(int actorId, int targetId, CompiledTargetQuery query) =>
            Enumerate(actorId, query).Any(candidate => candidate.Id == targetId);

        public TargetSelection ResolveSpecific(int actorId, int targetId)
        {
            if (!_locations.TryGetValue(targetId, out var location) ||
                !_attributes.TryGetValue(targetId, out var attributes)) return default;
            return new TargetSelection(targetId, location,
                _affinities.TryGetValue((actorId, targetId), out var affinity) ? affinity : 0.5f,
                attributes);
        }

        private IEnumerable<(int Id, float Affinity)> Enumerate(int actorId, CompiledTargetQuery query)
        {
            if (query.Relation == TargetRelationKind.Social)
                return _social.TryGetValue(actorId, out var social) ? social : [];
            if (!_memberships.TryGetValue(actorId, out var memberships)) return [];

            var candidates = new HashSet<int>();
            _diagnostics?.RecordTransientOperation();
            foreach (var membership in memberships)
            {
                if (membership.TypeHash != query.NetworkTypeHash) continue;
                switch (query.Relation)
                {
                    case TargetRelationKind.NetworkMember:
                        if (_networkMembers.TryGetValue(membership.NetworkId, out var members))
                            foreach (var member in members) if (member != actorId) candidates.Add(member);
                        break;
                    case TargetRelationKind.NetworkSupervisor:
                        if (membership.SupervisorId != 0) candidates.Add(membership.SupervisorId);
                        break;
                    case TargetRelationKind.NetworkDirectReport:
                        if (_networkMembers.TryGetValue(membership.NetworkId, out var reports))
                            foreach (var report in reports)
                                if (_memberships.TryGetValue(report, out var reportLinks) && reportLinks.Any(link =>
                                    link.NetworkId == membership.NetworkId && link.SupervisorId == actorId))
                                    candidates.Add(report);
                        break;
                }
            }
            return candidates.OrderBy(id => id).Select(id => (id,
                _affinities.TryGetValue((actorId, id), out var affinity) ? affinity : 0.5f));
        }

        private static int Compare(float[] left, int leftId, float[] right, int rightId, IReadOnlyList<CompiledTargetRank> ranks)
        {
            for (var index = 0; index < left.Length; index++)
            {
                var comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return ranks[index].Order == SortOrder.Descending ? -comparison : comparison;
            }
            return leftId.CompareTo(rightId);
        }
    }

    private sealed class CandidateEvaluator
    {
        public CandidateEvaluator(CompiledIntent definition) { Definition = definition; }
        public CompiledIntent Definition { get; }

        public DecisionResult Evaluate(DecisionContext context, TargetSelection target,
            float[]? utilityDiagnostics = null, float[]? traitDiagnostics = null)
        {
            var facts = new DecisionFactContext(context.Time, context.Job, context.Attributes, context.Location,
                context.Travel, target.EntityId, target.Affinity, target.LocationId, target.Attributes);
            if (!Definition.Eligibility.Evaluate(facts))
            {
                if (utilityDiagnostics is not null) Array.Clear(utilityDiagnostics);
                if (traitDiagnostics is not null) Array.Clear(traitDiagnostics);
                return new DecisionResult(Definition.RuntimeIndex, Definition, false, float.NegativeInfinity, target.EntityId, target.LocationId);
            }
            var score = Definition.BaseUtility;
            for (var index = 0; index < Definition.UtilityInputs.Length; index++)
            {
                var input = Definition.UtilityInputs[index];
                var contribution = input.Weight * DecisionUtility.Curve(input.Curve, input.Expression.Evaluate(facts));
                score += contribution;
                if (utilityDiagnostics is not null) utilityDiagnostics[index] = contribution;
            }
            for (var index = 0; index < Definition.TraitModifiers.Length; index++)
            {
                var modifier = Definition.TraitModifiers[index];
                var contribution = (context.TraitMask & modifier.TraitBit) != 0 ? modifier.Modifier : 0;
                score += contribution;
                if (traitDiagnostics is not null) traitDiagnostics[index] = contribution;
            }
            return new DecisionResult(Definition.RuntimeIndex, Definition, true, score, target.EntityId, target.LocationId);
        }
    }
}

// Effects are rates, not decisions. They consume simulation time and the
// currently performed public activity, never rendering-frame count.
public sealed class ActivityEffectsSystem : QuerySystem<AgentAttributes, ActivityState, DecisionState>
{
    private readonly Entity _clock;
    private readonly AgentAttributeSchema _schema;
    private readonly Dictionary<(int ActionHash, int ActivityTypeHash), (int Index, float Rate, EffectSubject Subject)[]> _effects;

    public ActivityEffectsSystem(ContentCatalog catalog, Entity clock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _schema = catalog.AgentAttributes;
        _effects = catalog.Intents.All.ToDictionary(intent => (intent.Hash, intent.Activity.Hash), intent => intent.Effects
            .Select(effect => (effect.AttributeIndex, effect.PerMinute, effect.Subject)).ToArray());
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var minutes = (float)(_clock.GetComponent<WorldTime>().DeltaSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        if (minutes <= 0f) return;
        Query.ForEachEntity((ref AgentAttributes attributes, ref ActivityState activity, ref DecisionState decision, Entity entity) =>
        {
            if (activity.Phase != ActivityPhase.Performing) return;
            if (!_effects.TryGetValue((activity.ActionHash, activity.ActivityTypeHash), out var effects)) return;
            var role = entity.HasComponent<CoordinationState>()
                ? entity.GetComponent<CoordinationState>().Role : CoordinationRole.None;
            foreach (var (index, rate, subject) in effects)
            {
                if (subject == EffectSubject.Participant && role != CoordinationRole.Participant ||
                    subject == EffectSubject.Initiator && role == CoordinationRole.Participant) continue;
                var definition = _schema.Definitions[index];
                var previous = attributes.Values[index];
                attributes.Values[index] = Math.Clamp(previous + rate * minutes, definition.Min, definition.Max);
                if (attributes.Values[index] != previous) DecisionInvalidation.SignalAttribute(ref decision, index);
            }
        });
    }
}
