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
        new(FactDependencyCategory.SocialTargets | FactDependencyCategory.TargetAffinity | FactDependencyCategory.TargetLocation));
}

// Target resolution and utility scoring operate entirely from compiled content.
// The winner application consequently copies a generic result into ECS state.
public sealed class AgentDecisionSystem : QuerySystem<Identity, AgentAttributes, Psychology, AgentLocation, AgentTravel>
{
    private readonly EntityStore _store;
    private readonly Entity _clock;
    private readonly Dictionary<int, JobDefinition> _jobs;
    private readonly CandidateEvaluator[] _candidates;
    private readonly CompiledIntent _fallback;

    public AgentDecisionSystem(EntityStore store, ContentCatalog catalog, Entity clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _jobs = catalog.Jobs.ToDictionary(job => job.Hash);
        _fallback = catalog.Intents.Fallback;
        _candidates = catalog.Intents.All.Where(intent => !intent.Fallback).Select(intent => new CandidateEvaluator(intent)).ToArray();
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var time = _clock.GetComponent<WorldTime>();
        var minute = (long)Math.Floor(time.ElapsedSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        var targets = new TargetResolver(_store);

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
            var active = Array.Find(_candidates, item => item.Definition.Hash == currentActionHash);
            if (active is not null)
            {
                var selected = targets.Resolve(entity.Id, active.Definition.Target, context);
                if (selected.EntityId != intention.TargetEntityId || selected.LocationId != intention.TargetLocationId)
                    DecisionInvalidation.Signal(ref decision, new(FactDependencyCategory.SocialTargets |
                        FactDependencyCategory.TargetLocation));
            }
            if (!decision.Dirty && decision.LastConsideredMinute >= minute) return;

            EnsureCache(ref decision);
            var fullPass = decision.LastConsideredMinute < minute || decision.ChangedFacts == FactDependencyMask.None;
            var changed = fullPass ? FactDependencyMask.All : decision.ChangedFacts;
            decision.Dirty = false;
            decision.ChangedFacts = FactDependencyMask.None;
            decision.LastConsideredMinute = minute;
            foreach (var candidate in _candidates)
            {
                if (!fullPass && !candidate.Definition.Dependencies.Intersects(changed)) continue;
                var result = candidate.Evaluate(context, targets.Resolve(entity.Id, candidate.Definition.Target, context));
                var index = candidate.Definition.RuntimeIndex;
                decision.CachedScores[index] = result.Score;
                decision.CachedEligibility[index] = result.Eligible;
                decision.CachedTargetEntityIds[index] = result.TargetEntityId;
                decision.CachedTargetLocationIds[index] = result.TargetLocationId;
                decision.EvaluationCount++;
            }
            var snapshot = decision;
            var eligible = _candidates
                .Select(candidate => CachedResult(candidate.Definition, snapshot))
                .Where(result => result.Eligible && !IsCoolingDown(result.Action.Hash, minute, snapshot))
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Action.Hash)
                .ToArray();
            if (eligible.Length == 0)
                eligible = new[] { new DecisionResult(_fallback.RuntimeIndex, _fallback, true, _fallback.BaseUtility, 0, 0) };

            var winner = eligible[0];
            var current = eligible.FirstOrDefault(result => result.Action.Hash == currentActionHash);
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
        var count = _candidates.Length + 1;
        if (state.CachedScores?.Length == count && state.CachedEligibility?.Length == count &&
            state.CachedTargetEntityIds?.Length == count && state.CachedTargetLocationIds?.Length == count) return;
        state.CachedScores = new float[count]; state.CachedEligibility = new bool[count];
        state.CachedTargetEntityIds = new int[count]; state.CachedTargetLocationIds = new int[count];
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
    private readonly record struct TargetSelection(int EntityId, int LocationId, float Affinity);
    private readonly record struct DecisionContext(WorldTime Time, JobDefinition Job, float[] Attributes,
        long TraitMask, AgentLocation Location, AgentTravel Travel);

    private sealed class TargetResolver
    {
        private readonly Dictionary<int, int> _locations;
        private readonly Dictionary<int, List<(int Id, float Affinity)>> _social;

        public TargetResolver(EntityStore store)
        {
            _locations = store.Query<AgentLocation>().Entities.ToDictionary(
                entity => entity.Id, entity => entity.GetComponent<AgentLocation>().CurrentLocationId);
            _social = new();
            foreach (var edgeEntity in store.Query<EdgeData>().Entities)
            {
                var edge = edgeEntity.GetComponent<EdgeData>();
                if (!_social.TryGetValue(edge.Source.Id, out var edges)) _social[edge.Source.Id] = edges = new();
                edges.Add((edge.Target.Id, Math.Clamp((edge.Affinity + 100f) / 200f, 0f, 1f)));
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
            if (!_social.TryGetValue(actorId, out var edges)) return default;

            var query = definition.Query!;
            (TargetSelection Target, float[] Ranks)? best = null;
            foreach (var edge in edges)
            {
                if (!_locations.TryGetValue(edge.Id, out var targetLocation)) continue;
                var facts = new DecisionFactContext(context.Time, context.Job, context.Attributes,
                    context.Location, context.Travel, edge.Id, edge.Affinity, targetLocation);
                if (query.Requirements.Any(requirement => !requirement.Evaluate(facts))) continue;
                var ranks = query.RankBy.Select(rank => rank.Value.Evaluate(facts)).ToArray();
                var target = new TargetSelection(edge.Id, context.Location.CurrentLocationId, edge.Affinity);
                if (best is null || Compare(ranks, edge.Id, best.Value.Ranks, best.Value.Target.EntityId, query.RankBy) < 0)
                    best = (target, ranks);
            }
            return best?.Target ?? default;
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

        public DecisionResult Evaluate(DecisionContext context, TargetSelection target)
        {
            var facts = new DecisionFactContext(context.Time, context.Job, context.Attributes, context.Location,
                context.Travel, target.EntityId, target.Affinity, target.LocationId);
            if (!Definition.Eligibility.Evaluate(facts))
                return new DecisionResult(Definition.RuntimeIndex, Definition, false, float.NegativeInfinity, target.EntityId, target.LocationId);
            var score = Definition.BaseUtility;
            foreach (var input in Definition.UtilityInputs)
                score += input.Weight * Curve(input.Curve, input.Expression.Evaluate(facts));
            foreach (var modifier in Definition.TraitModifiers)
                if ((context.TraitMask & modifier.TraitBit) != 0) score += modifier.Modifier;
            return new DecisionResult(Definition.RuntimeIndex, Definition, true, score, target.EntityId, target.LocationId);
        }

        private static float Curve(IReadOnlyList<ResponsePoint> points, float value)
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
}

// Effects are rates, not decisions. They consume simulation time and the
// currently performed public activity, never rendering-frame count.
public sealed class ActivityEffectsSystem : QuerySystem<AgentAttributes, ActivityState, DecisionState>
{
    private readonly Entity _clock;
    private readonly AgentAttributeSchema _schema;
    private readonly Dictionary<(int ActionHash, int ActivityTypeHash), (int Index, float Rate)[]> _effects;

    public ActivityEffectsSystem(ContentCatalog catalog, Entity clock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _schema = catalog.AgentAttributes;
        _effects = catalog.Intents.All.ToDictionary(intent => (intent.Hash, intent.Activity.Hash), intent => intent.Effects
            .Select(effect => (effect.AttributeIndex, effect.PerMinute)).ToArray());
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var minutes = (float)(_clock.GetComponent<WorldTime>().DeltaSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        if (minutes <= 0f) return;
        Query.ForEachEntity((ref AgentAttributes attributes, ref ActivityState activity, ref DecisionState decision, Entity _) =>
        {
            if (activity.Phase != ActivityPhase.Performing) return;
            if (!_effects.TryGetValue((activity.ActionHash, activity.ActivityTypeHash), out var effects)) return;
            foreach (var (index, rate) in effects)
            {
                var definition = _schema.Definitions[index];
                var previous = attributes.Values[index];
                attributes.Values[index] = Math.Clamp(previous + rate * minutes, definition.Min, definition.Max);
                if (attributes.Values[index] != previous) DecisionInvalidation.SignalAttribute(ref decision, index);
            }
        });
    }
}
