using System.Numerics;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.World;

namespace TacticalSim.Core.Tactical;

public enum TacticalActionKind { Move, ChangePosture, Aim, Fire, Reload, Command, SelfAid, Rescue }

/// <summary>Central M10 translation from authoritative capability state to gameplay costs and gates.</summary>
public sealed class CapabilityActionPolicy
{
    public const float SevereImpairmentThreshold = .2f;

    public ActionCapabilityEvaluation Evaluate(TacticalActionKind action, float healthyTuCost, CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!float.IsFinite(healthyTuCost) || healthyTuCost <= 0f)
            throw new ArgumentOutOfRangeException(nameof(healthyTuCost));

        TacticalCapability capability = action switch
        {
            TacticalActionKind.Move or TacticalActionKind.Rescue => TacticalCapability.Movement,
            TacticalActionKind.ChangePosture => TacticalCapability.Posture,
            TacticalActionKind.Aim => TacticalCapability.Aiming,
            TacticalActionKind.Fire => TacticalCapability.Firing,
            TacticalActionKind.Reload => TacticalCapability.Reloading,
            TacticalActionKind.Command => TacticalCapability.Communication,
            TacticalActionKind.SelfAid => TacticalCapability.SelfAid,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        float capacity = Math.Clamp(state[capability], 0f, 1f);
        bool blocked = capacity < SevereImpairmentThreshold;
        float multiplier = blocked ? float.PositiveInfinity : 1f / capacity;
        return new(action, capability, !blocked, blocked ? healthyTuCost : healthyTuCost * multiplier,
            multiplier, action == TacticalActionKind.Fire ? capacity : 1f,
            blocked ? $"{capability} capacity is below {SevereImpairmentThreshold:0.##}" : null);
    }
}

public sealed record ActionCapabilityEvaluation(TacticalActionKind Action, TacticalCapability GoverningCapability,
    bool IsAllowed, float AdjustedTuCost, float CostMultiplier, float Stability, string? BlockReason);

public enum CasualtyTransportMode { Drag, Carry }

/// <summary>A spatial, timed rescue action. It moves both actors and exposes both for its duration.</summary>
public sealed class CasualtyTransportAction : TacticalAction
{
    public const float MaximumReachMeters = 2f;
    private readonly ITacticalWorld _world;
    private readonly Vector3 _rescuerStart;
    private readonly Vector3 _casualtyOffset;

    private readonly Action<Vector3>? _casualtyMoved;
    public CasualtyTransportAction(IEntity rescuer, IEntity casualty, ITacticalWorld world, Vector3 destination,
        CasualtyTransportMode mode, CapabilityState rescuerCapabilities, float healthyMetersPerSecond = 1.4f,
        Action<Vector3>? casualtyMoved = null)
        : base(rescuer?.Id ?? throw new ArgumentNullException(nameof(rescuer)), 1f)
    {
        ArgumentNullException.ThrowIfNull(casualty);
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (rescuer.Id == casualty.Id) throw new ArgumentException("An actor cannot rescue itself.", nameof(casualty));
        if (Vector3.Distance(rescuer.Position, casualty.Position) > MaximumReachMeters)
            throw new InvalidOperationException("The casualty is outside rescue reach.");
        float movement = rescuerCapabilities[TacticalCapability.Movement];
        float handling = rescuerCapabilities[TacticalCapability.Reloading];
        float minimum = mode == CasualtyTransportMode.Carry ? .5f : .2f;
        if (movement < minimum || handling < minimum)
            throw new InvalidOperationException($"The rescuer lacks capability to {mode.ToString().ToLowerInvariant()} the casualty.");
        if (!float.IsFinite(healthyMetersPerSecond) || healthyMetersPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(healthyMetersPerSecond));

        CasualtyId = casualty.Id;
        Destination = world.Bounds.Clamp(destination);
        Mode = mode;
        _rescuerStart = rescuer.Position;
        _casualtyOffset = casualty.Position - rescuer.Position;
        _casualtyMoved = casualtyMoved;
        float burden = mode == CasualtyTransportMode.Drag ? .45f : .35f; // provisional gameplay tuning
        MovementSpeedMetersPerSecond = healthyMetersPerSecond * movement * burden;
        TUCost = MathF.Max(.001f, Vector3.Distance(_rescuerStart, Destination) / MovementSpeedMetersPerSecond);
    }

    public Guid CasualtyId { get; }
    public Vector3 Destination { get; }
    public CasualtyTransportMode Mode { get; }
    public float MovementSpeedMetersPerSecond { get; }
    public bool RescuerWeaponUseBlocked => true;
    public float ExposureSeconds => ExecutionProgress;
    public override void Execute(float dt)
    {
        Vector3 position = Vector3.Lerp(_rescuerStart, Destination, NormalizedProgress);
        _world.SetEntityPosition(ActorId, position);
        _world.SetEntityPosition(CasualtyId, position + _casualtyOffset);
        _casualtyMoved?.Invoke(position + _casualtyOffset);
    }
    public override void OnComplete() { base.OnComplete(); Execute(0f); }
}

public enum CasualtyBehaviorState { FightingEffectively, FightingImpaired, SeekingCover, SelfAiding, CrawlingToSafety, CallingForHelp, Disoriented, Unconscious, Dead }

/// <summary>Deterministic policy over information an AI is permitted to observe.</summary>
public sealed record CasualtyBehaviorContext(CapabilityState Capability, CasualtyState ObservedState,
    bool UnderFire, bool HasCover, bool HasSelfAidEquipment, bool MissionRequiresHoldingPosition);

public sealed class CasualtyBehaviorPolicy
{
    public CasualtyBehaviorState Decide(CasualtyBehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ObservedState == CasualtyState.Dead) return CasualtyBehaviorState.Dead;
        if (context.ObservedState >= CasualtyState.Unconscious) return CasualtyBehaviorState.Unconscious;
        if (context.Capability[TacticalCapability.Communication] < .2f) return CasualtyBehaviorState.Disoriented;
        if (context.UnderFire && !context.HasCover && !context.MissionRequiresHoldingPosition && context.Capability[TacticalCapability.Movement] >= .2f)
            return context.Capability[TacticalCapability.Posture] < .2f ? CasualtyBehaviorState.CrawlingToSafety : CasualtyBehaviorState.SeekingCover;
        if (!context.UnderFire && context.HasSelfAidEquipment && context.Capability[TacticalCapability.SelfAid] >= .2f)
            return CasualtyBehaviorState.SelfAiding;
        if (context.Capability[TacticalCapability.Firing] < .2f)
            return CasualtyBehaviorState.CallingForHelp;
        return context.Capability[TacticalCapability.Firing] < .75f
            ? CasualtyBehaviorState.FightingImpaired : CasualtyBehaviorState.FightingEffectively;
    }
}

public sealed record ObservableCasualty(Guid ActorId, CasualtyOverlayStatus Status, float DistanceMeters, bool CallingForHelp);

/// <summary>Chooses a teammate response from visible status only; it never inspects lesions.</summary>
public sealed class TeammateResponsePolicy
{
    public Guid? SelectRescueTarget(IEnumerable<ObservableCasualty> casualties, bool missionPermitsRescue)
    {
        if (!missionPermitsRescue) return null;
        return casualties.Where(x => x.Status is CasualtyOverlayStatus.Critical or CasualtyOverlayStatus.Unconscious)
            .OrderByDescending(x => x.CallingForHelp)
            .ThenByDescending(x => x.Status == CasualtyOverlayStatus.Critical)
            .ThenBy(x => x.DistanceMeters).ThenBy(x => x.ActorId)
            .Select(x => (Guid?)x.ActorId).FirstOrDefault();
    }
}

public enum CasualtyOverlayStatus { Effective, Impaired, Critical, Unconscious, Dead }
public sealed record CasualtyOverlay(CasualtyOverlayStatus Status, bool IsBeingTreated, bool? BleedingControlled,
    IReadOnlyList<string> DebugDetails);

public sealed class CasualtyOverlayFactory
{
    public CasualtyOverlay Create(CapabilityState capability, CasualtyState casualty, bool isBeingTreated,
        bool? bleedingControlled, bool debugEnabled = false, IEnumerable<string>? authoritativeDebugDetails = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        CasualtyOverlayStatus status = casualty switch
        {
            CasualtyState.Dead => CasualtyOverlayStatus.Dead,
            >= CasualtyState.Unconscious => CasualtyOverlayStatus.Unconscious,
            >= CasualtyState.Incapacitated => CasualtyOverlayStatus.Critical,
            _ when capability.Capacity.Values.Min() < .75f => CasualtyOverlayStatus.Impaired,
            _ => CasualtyOverlayStatus.Effective
        };
        string[] details = debugEnabled ? authoritativeDebugDetails?.ToArray() ?? [] : [];
        return new(status, isBeingTreated, bleedingControlled, Array.AsReadOnly(details));
    }
}

public enum RescueRequirement { Mandatory, Optional, Irrelevant }
public sealed record ScenarioOutcome(bool MissionCompleted, int FriendlySurvivors, int FriendlyDead,
    int EnemiesNeutralized, int CasualtiesEvacuated, float DelaySeconds, float ExposureSeconds, int ResourcesExpended);
public sealed record ScenarioScoreBreakdown(float Mission, float Survival, float Neutralization, float Evacuation,
    float Delay, float Exposure, float Resources) { public float Total => Mission + Survival + Neutralization + Evacuation + Delay + Exposure + Resources; }
public sealed record ScenarioScoringRules(RescueRequirement RescueRequirement = RescueRequirement.Optional,
    float MissionCompletionPoints = 100f, float SurvivorPoints = 20f, float DeathPenalty = 30f,
    float EnemyNeutralizationPoints = 10f, float EvacuationPoints = 15f, float DelayPenaltyPerSecond = .1f,
    float ExposurePenaltyPerSecond = .05f, float ResourcePenalty = 1f);

public sealed class CasualtyScenarioScorer
{
    public ScenarioScoreBreakdown Score(ScenarioOutcome outcome, ScenarioScoringRules rules)
    {
        ArgumentNullException.ThrowIfNull(outcome); ArgumentNullException.ThrowIfNull(rules);
        float evacuation = rules.RescueRequirement == RescueRequirement.Irrelevant ? 0f : outcome.CasualtiesEvacuated * rules.EvacuationPoints;
        float mission = outcome.MissionCompleted ? rules.MissionCompletionPoints : 0f;
        if (rules.RescueRequirement == RescueRequirement.Mandatory && outcome.CasualtiesEvacuated == 0) mission = 0f;
        return new(mission, outcome.FriendlySurvivors * rules.SurvivorPoints - outcome.FriendlyDead * rules.DeathPenalty,
            outcome.EnemiesNeutralized * rules.EnemyNeutralizationPoints, evacuation,
            -MathF.Max(0f, outcome.DelaySeconds) * rules.DelayPenaltyPerSecond,
            -MathF.Max(0f, outcome.ExposureSeconds) * rules.ExposurePenaltyPerSecond,
            -Math.Max(0, outcome.ResourcesExpended) * rules.ResourcePenalty);
    }
}
