using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace ProxyState.Simulation;

// Utility decisions are made from Ground Truth, but only the resulting ECS
// intention is exposed to downstream activity systems.
public sealed class AgentDecisionSystem : QuerySystem<Identity, AgentAttributes, Psychology, AgentLocation, AgentTravel>
{
    private readonly EntityStore _store;
    private readonly Entity _clock;
    private readonly Dictionary<int, JobDefinition> _jobs;
    private readonly CandidateEvaluator[] _candidates;
    private readonly Dictionary<string, long> _traitBits;
    private readonly int _socializeActionHash;

    public AgentDecisionSystem(EntityStore store, ContentCatalog catalog, Entity clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _jobs = catalog.Jobs.ToDictionary(job => job.Hash);
        _traitBits = catalog.Traits.ToDictionary(trait => trait.Id, trait => trait.Bit, StringComparer.OrdinalIgnoreCase);
        _candidates = catalog.Actions.Select(action => new CandidateEvaluator(action)).ToArray();
        _socializeActionHash = catalog.Actions.Single(action =>
            string.Equals(action.Id, "socialize", StringComparison.OrdinalIgnoreCase)).Hash;
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var time = _clock.GetComponent<WorldTime>();
        var minute = (long)Math.Floor(time.ElapsedSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        var locations = _store.Query<AgentLocation>().Entities.ToDictionary(entity => entity.Id, entity => entity.GetComponent<AgentLocation>().CurrentLocationId);
        var peers = BuildAvailablePeers(locations);

        Query.ForEachEntity((ref Identity identity, ref AgentAttributes attributes, ref Psychology psychology,
            ref AgentLocation location, ref AgentTravel travel, Entity entity) =>
        {
            ref var intention = ref entity.GetComponent<IntentionState>();
            ref var activity = ref entity.GetComponent<ActivityState>();
            ref var decision = ref entity.GetComponent<DecisionState>();
            // Target loss is an event, not a reason to wait for the next minute.
            if (intention.ActionHash == _socializeActionHash &&
                (!peers.TryGetValue(entity.Id, out var currentPeer) || currentPeer.TargetId != intention.TargetEntityId))
                decision.Dirty = true;
            if (!decision.Dirty && decision.LastConsideredMinute >= minute)
                return;

            decision.Dirty = false;
            decision.LastConsideredMinute = minute;
            if (!_jobs.TryGetValue(identity.OccupationId, out var job))
                return;

            peers.TryGetValue(entity.Id, out var peer);
            var context = new DecisionContext(time, job, attributes.Values, psychology.TraitMask,
                location, travel, peer.TargetId, peer.Affinity);
            var decisionSnapshot = decision;
            var currentActionHash = intention.ActionHash;
            var eligible = _candidates
                .Select(candidate => candidate.Evaluate(context, _traitBits))
                .Where(result => result.Eligible && !IsCoolingDown(result.Action!.Hash, minute, decisionSnapshot))
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Action!.Hash)
                .ToArray();
            if (eligible.Length == 0)
                return;

            var winner = eligible[0];
            var current = eligible.FirstOrDefault(result => result.Action!.Hash == currentActionHash);
            if (currentActionHash != 0 && winner.Action!.Hash != currentActionHash)
            {
                var currentDefinition = _candidates.FirstOrDefault(item => item.Definition.Hash == currentActionHash)?.Definition;
                var committed = currentDefinition is not null && minute - intention.SelectedAtMinute < currentDefinition.Controls.MinimumCommitmentMinutes;
                var currentScore = current.Action is null ? float.NegativeInfinity : current.Score;
                var urgent = winner.Score >= winner.Action.Controls.UrgentPreemptionThreshold;
                var switchingMargin = currentDefinition?.Controls.SwitchingThreshold ?? winner.Action.Controls.SwitchingThreshold;
                if (!urgent && (committed || winner.Score < currentScore + switchingMargin))
                    return;
                if (currentDefinition?.Controls.CooldownOnExit == true)
                    SetCooldown(currentDefinition, minute, ref decision);
            }

            if (winner.Action!.Hash == intention.ActionHash)
                return;
            intention.ActionHash = winner.Action.Hash;
            intention.TargetEntityId = winner.Action.Id.Equals("socialize", StringComparison.OrdinalIgnoreCase) ? peer.TargetId : 0;
            intention.TargetLocationId = winner.Action.Id.Equals("work", StringComparison.OrdinalIgnoreCase)
                ? location.WorkLocationId : location.HomeLocationId;
            intention.SelectedAtMinute = minute;
            intention.Utility = winner.Score;
        });
    }

    private Dictionary<int, PeerContext> BuildAvailablePeers(IReadOnlyDictionary<int, int> locations)
    {
        var peers = new Dictionary<int, PeerContext>();
        foreach (var edgeEntity in _store.Query<EdgeData>().Entities)
        {
            var edge = edgeEntity.GetComponent<EdgeData>();
            if (!locations.TryGetValue(edge.Source.Id, out var sourceLocation) ||
                !locations.TryGetValue(edge.Target.Id, out var targetLocation) || sourceLocation != targetLocation)
                continue;
            var candidate = new PeerContext(edge.Target.Id, Math.Clamp((edge.Affinity + 100f) / 200f, 0f, 1f));
            if (!peers.TryGetValue(edge.Source.Id, out var existing) || candidate.Affinity > existing.Affinity ||
                (candidate.Affinity == existing.Affinity && candidate.TargetId < existing.TargetId))
                peers[edge.Source.Id] = candidate;
        }
        return peers;
    }

    private static bool IsCoolingDown(int hash, long minute, DecisionState state)
    {
        if (state.CooldownActionHashes is null || state.CooldownUntilMinutes is null) return false;
        for (var index = 0; index < state.CooldownActionHashes.Length; index++)
            if (state.CooldownActionHashes[index] == hash && state.CooldownUntilMinutes[index] > minute) return true;
        return false;
    }

    private static void SetCooldown(ActionDefinition action, long minute, ref DecisionState state)
    {
        if (action.Controls.CooldownMinutes == 0) return;
        var index = Array.IndexOf(state.CooldownActionHashes, action.Hash);
        if (index < 0) index = Array.IndexOf(state.CooldownActionHashes, 0);
        if (index < 0) return;
        state.CooldownActionHashes[index] = action.Hash;
        state.CooldownUntilMinutes[index] = minute + action.Controls.CooldownMinutes;
    }

    private readonly record struct PeerContext(int TargetId, float Affinity);
    private readonly record struct DecisionResult(ActionDefinition? Action, bool Eligible, float Score);
    private readonly record struct DecisionContext(WorldTime Time, JobDefinition Job, float[] Attributes, long TraitMask,
        AgentLocation Location, AgentTravel Travel, int PeerId, float PeerAffinity);

    private sealed class CandidateEvaluator
    {
        public CandidateEvaluator(ActionDefinition definition) => Definition = definition;
        public ActionDefinition Definition { get; }

        public DecisionResult Evaluate(DecisionContext context, IReadOnlyDictionary<string, long> traits)
        {
            if (!Eligible(context)) return new DecisionResult(Definition, false, float.NegativeInfinity);
            var score = Definition.BaseUtility;
            var facts = new DecisionFactContext(context.Time, context.Job, context.Attributes, context.PeerAffinity);
            foreach (var input in Definition.UtilityInputs)
                score += input.Weight * Curve(input.Curve, input.CompiledExpression!.Evaluate(facts));
            foreach (var modifier in Definition.TraitModifiers)
                if ((context.TraitMask & traits[modifier.Trait]) != 0) score += modifier.Modifier;
            return new DecisionResult(Definition, true, score);
        }

        private bool Eligible(DecisionContext context) => Definition.Eligibility.Gate.ToLowerInvariant() switch
        {
            "workschedule" => context.Job.WorkDays.Contains(context.Time.DayOfWeek) &&
                context.Time.MinuteOfDay >= context.Job.WorkStartMinute + Definition.Eligibility.ScheduleStartOffsetMinutes &&
                context.Time.MinuteOfDay < context.Job.WorkEndMinute + Definition.Eligibility.ScheduleEndOffsetMinutes,
            "homereachable" => context.Location.CurrentLocationId == context.Location.HomeLocationId || context.Travel.RouteLocationIds.Length > 0,
            "availablepeer" => context.PeerId != 0,
            _ => false
        };

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
public sealed class ActivityEffectsSystem : QuerySystem<AgentAttributes, ActivityState>
{
    private readonly Entity _clock;
    private readonly AgentAttributeSchema _schema;
    private readonly Dictionary<int, (int Index, float Rate)[]> _effects;

    public ActivityEffectsSystem(ContentCatalog catalog, Entity clock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _clock = clock;
        _schema = catalog.AgentAttributes;
        _effects = catalog.Actions.ToDictionary(action => action.Hash, action => action.Effects
            .Select(effect => (_schema.GetIndex(effect.Attribute), effect.PerMinute)).ToArray());
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var minutes = (float)(_clock.GetComponent<WorldTime>().DeltaSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);
        if (minutes <= 0f) return;
        Query.ForEachEntity((ref AgentAttributes attributes, ref ActivityState activity, Entity _) =>
        {
            if (activity.Kind is ActivityKind.Idle or ActivityKind.Commuting) return;
            if (!_effects.TryGetValue(activity.CurrentActionHash, out var effects)) return;
            foreach (var (index, rate) in effects)
            {
                var definition = _schema.Definitions[index];
                attributes.Values[index] = Math.Clamp(attributes.Values[index] + rate * minutes, definition.Min, definition.Max);
            }
        });
    }
}
