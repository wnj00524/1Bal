using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// Immutable command for one body-local projectile interaction. Position components
/// are meters, velocity components are meters per second, and maximum traversal is
/// a typed distance. The target physiology is the state mutated by the resolver.
/// </summary>
public sealed class ProjectileInteractionRequest
{
    public ProjectileInteractionRequest(
        string impactId,
        string projectileProfileId,
        IActorPhysiology targetPhysiology,
        ProjectileState projectileState,
        BallisticProfile projectileProfile,
        Distance maximumTraversalDistance,
        DamageModelVersion? modelVersion = null,
        Guid? shooterId = null,
        Guid? targetId = null,
        IDeterministicRandomStreamProvider? randomStreams = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(impactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectileProfileId);
        ArgumentNullException.ThrowIfNull(targetPhysiology);

        ValidateVector(projectileState.Position, nameof(projectileState));
        ValidateVector(projectileState.Velocity, nameof(projectileState));
        if (projectileState.Velocity.LengthSquared() <= 0f)
            throw new ArgumentOutOfRangeException(nameof(projectileState), "Projectile velocity must be non-zero.");
        if (!float.IsFinite(projectileState.Time) || projectileState.Time < 0f)
            throw new ArgumentOutOfRangeException(nameof(projectileState), "Projectile time must be finite and non-negative.");
        if (projectileProfile.MassKilograms.Kilograms <= 0f)
            throw new ArgumentOutOfRangeException(nameof(projectileProfile), "Projectile mass must be positive.");
        if (projectileProfile.CrossSectionalAreaSquareMeters.SquareMeters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(projectileProfile), "Projectile area must be positive.");
        ArgumentNullException.ThrowIfNull(projectileProfile.DragModel);
        if (maximumTraversalDistance.Meters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumTraversalDistance), "Maximum traversal distance must be positive.");
        if (modelVersion.HasValue && !Enum.IsDefined(modelVersion.Value))
            throw new ArgumentOutOfRangeException(nameof(modelVersion));

        ImpactId = impactId;
        ProjectileProfileId = projectileProfileId;
        TargetPhysiology = targetPhysiology;
        ProjectileState = projectileState;
        ProjectileProfile = projectileProfile;
        MaximumTraversalDistance = maximumTraversalDistance;
        ModelVersion = modelVersion;
        ShooterId = shooterId;
        TargetId = targetId;
        RandomStreams = randomStreams;
    }

    public string ImpactId { get; }
    public string ProjectileProfileId { get; }
    public IActorPhysiology TargetPhysiology { get; }
    public ProjectileState ProjectileState { get; }
    public BallisticProfile ProjectileProfile { get; }
    public Distance MaximumTraversalDistance { get; }

    /// <summary>
    /// Optional per-impact override used by migration comparisons. When absent,
    /// the service's configured feature-flag value is used.
    /// </summary>
    public DamageModelVersion? ModelVersion { get; }

    public Guid? ShooterId { get; }
    public Guid? TargetId { get; }
    /// <summary>
    /// Optional request-scoped random context. When omitted, the service uses its
    /// injected scenario context. The resolver records metadata from the same
    /// provider it uses, so callers cannot attach an unrelated seed snapshot.
    /// </summary>
    public IDeterministicRandomStreamProvider? RandomStreams { get; }

    private static void ValidateVector(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }
}

/// <summary>Serializable snapshot of immediate physiology around one impact.</summary>
public sealed record PhysiologyDebugSnapshot(
    Volume BloodVolume,
    FlowRate SystemicBleedRate,
    float MeanArterialPressureMillimetersMercury,
    float BloodOxygenation,
    float Consciousness,
    float Pain,
    float Shock,
    SimulationTime BrainHypoxiaDuration,
    bool IsDead)
{
    internal static PhysiologyDebugSnapshot Capture(IActorPhysiology physiology) => new(
        Volume.FromCubicCentimeters(physiology.TotalBloodVolume),
        FlowRate.FromMillilitersPerSecond(physiology.SystemicBleedRateMlPerSecond),
        physiology.MeanArterialPressureMmhg,
        physiology.BloodOxygenation,
        physiology.ConsciousnessLevel,
        physiology.PainLevel,
        physiology.ShockLevel,
        SimulationTime.FromSeconds(physiology.BrainHypoxiaSeconds),
        physiology.IsDead);
}

/// <summary>
/// M5 snapshot of existing tactical-capability values. A richer resolver-owned
/// capability contract is deliberately deferred to M7.
/// </summary>
public sealed record CapabilityDebugSnapshot(
    float Mobility,
    float WeaponHandling,
    float Consciousness,
    bool CanAct)
{
    internal static CapabilityDebugSnapshot Capture(IActorPhysiology physiology) => new(
        physiology.MobilityLevel,
        physiology.WeaponHandlingLevel,
        physiology.ConsciousnessLevel,
        !physiology.IsDead && physiology.ConsciousnessLevel > 0f);
}

/// <summary>Immutable debug representation of a direct temporary-cavity effect.</summary>
public sealed record CavitationDebugSnapshot(
    string StructureId,
    Vector3 Origin,
    Distance Radius,
    Energy SourceEnergy);

/// <summary>
/// Stable, omniscient debug trace emitted for every resolved impact. Later roadmap
/// layers populate lesion, blood-destination, and treatment detail without changing
/// the M5 projectile/wound/energy boundary.
/// </summary>
public sealed class ImpactDebugTrace
{
    private readonly ReadOnlyCollection<string> _generatedLesions;
    private readonly ReadOnlyCollection<string> _bleedingSources;
    private readonly ReadOnlyCollection<string> _bloodDestinations;
    private readonly ReadOnlyCollection<string> _activeTreatments;
    private readonly ReadOnlyCollection<string> _numericalWarnings;

    public ImpactDebugTrace(
        string impactId,
        string projectileProfileId,
        DamageModelVersion modelVersion,
        Guid? shooterId,
        Guid? targetId,
        WoundTrack woundTrack,
        PhysiologyDebugSnapshot physiologyBefore,
        PhysiologyDebugSnapshot physiologyAfter,
        CapabilityDebugSnapshot capabilityBefore,
        CapabilityDebugSnapshot capabilityAfter,
        DeterministicRandomMetadataSnapshot randomMetadata,
        IEnumerable<string>? generatedLesions = null,
        IEnumerable<string>? bleedingSources = null,
        IEnumerable<string>? bloodDestinations = null,
        IEnumerable<string>? activeTreatments = null,
        IEnumerable<string>? numericalWarnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(impactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectileProfileId);
        ArgumentNullException.ThrowIfNull(woundTrack);
        ArgumentNullException.ThrowIfNull(physiologyBefore);
        ArgumentNullException.ThrowIfNull(physiologyAfter);
        ArgumentNullException.ThrowIfNull(capabilityBefore);
        ArgumentNullException.ThrowIfNull(capabilityAfter);
        ArgumentNullException.ThrowIfNull(randomMetadata);

        ImpactId = impactId;
        ProjectileProfileId = projectileProfileId;
        ModelVersion = modelVersion;
        ShooterId = shooterId;
        TargetId = targetId;
        WoundTrack = woundTrack;
        EnergyLedger = woundTrack.EnergyLedger;
        PhysiologyBefore = physiologyBefore;
        PhysiologyAfter = physiologyAfter;
        CapabilityBefore = capabilityBefore;
        CapabilityAfter = capabilityAfter;
        RandomMetadata = randomMetadata;
        _generatedLesions = CopyStrings(generatedLesions);
        _bleedingSources = CopyStrings(bleedingSources);
        _bloodDestinations = CopyStrings(bloodDestinations);
        _activeTreatments = CopyStrings(activeTreatments);
        _numericalWarnings = CopyStrings(numericalWarnings);
    }

    public string ImpactId { get; }
    public string ProjectileProfileId { get; }
    public DamageModelVersion ModelVersion { get; }
    public Guid? ShooterId { get; }
    public Guid? TargetId { get; }
    public WoundTrack WoundTrack { get; }
    public EnergyLedger EnergyLedger { get; }
    public PhysiologyDebugSnapshot PhysiologyBefore { get; }
    public PhysiologyDebugSnapshot PhysiologyAfter { get; }
    public CapabilityDebugSnapshot CapabilityBefore { get; }
    public CapabilityDebugSnapshot CapabilityAfter { get; }
    public DeterministicRandomMetadataSnapshot RandomMetadata { get; }
    public IReadOnlyList<string> GeneratedLesions => _generatedLesions;
    public IReadOnlyList<string> BleedingSources => _bleedingSources;
    public IReadOnlyList<string> BloodDestinations => _bloodDestinations;
    public IReadOnlyList<string> ActiveTreatments => _activeTreatments;
    public IReadOnlyList<string> NumericalWarnings => _numericalWarnings;

    private static ReadOnlyCollection<string> CopyStrings(IEnumerable<string>? values)
    {
        string[] copy = values?.Select(static value =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return value;
        }).ToArray() ?? [];
        return Array.AsReadOnly(copy);
    }
}

/// <summary>Authoritative result returned to tests, console tools, and clients.</summary>
public sealed class ProjectileInteractionResult
{
    private readonly ReadOnlyCollection<CavitationDebugSnapshot> _cavitationEffects;

    public ProjectileInteractionResult(
        ProjectileState finalProjectileState,
        WoundTrack woundTrack,
        ImpactDebugTrace debugTrace,
        IEnumerable<CavitationDebugSnapshot>? cavitationEffects = null)
    {
        ArgumentNullException.ThrowIfNull(woundTrack);
        ArgumentNullException.ThrowIfNull(debugTrace);
        if (!ReferenceEquals(woundTrack, debugTrace.WoundTrack))
            throw new ArgumentException("The result and debug trace must reference the same wound track.", nameof(debugTrace));

        FinalProjectileState = finalProjectileState;
        WoundTrack = woundTrack;
        DebugTrace = debugTrace;
        _cavitationEffects = Array.AsReadOnly(cavitationEffects?.ToArray() ?? []);
    }

    public ProjectileState FinalProjectileState { get; }
    public WoundTrack WoundTrack { get; }
    public EnergyLedger EnergyLedger => WoundTrack.EnergyLedger;
    public ImpactDebugTrace DebugTrace { get; }
    public IReadOnlyList<CavitationDebugSnapshot> CavitationEffects => _cavitationEffects;
}
