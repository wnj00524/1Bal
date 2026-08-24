using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Treatment;
using TacticalSim.Core.Damage.Variation;
using TacticalSim.Core.Randomness;

namespace TacticalSim.Core.Damage.Physiology;

/// <summary>Immutable projection of all medically relevant actor state.</summary>
public sealed record ActorMedicalSnapshot(
    string ModelVersion, float SimulationTimeSeconds, CasualtyProfile Profile,
    PhysiologicalVariation RealizedVariation, IReadOnlyList<Lesion> Lesions,
    float CirculatingBloodMl, IReadOnlyDictionary<BloodDestination, float> BloodLostByDestination,
    CardiovascularState Cardiovascular, OxygenDeliveryState OxygenDelivery,
    ThoracicSnapshot Thoracic, MusculoskeletalFunctionalState Musculoskeletal,
    NeurologicalFunctionalState Neurological, CasualtyState Casualty,
    CapabilityState Capability, IReadOnlyDictionary<string, int> Inventory,
    DeterministicRandomMetadataSnapshot RandomMetadata);

public sealed record ThoracicSnapshot(float LeftGasMl, float RightGasMl, float LeftBloodMl,
    float RightBloodMl, float VentilationEffectiveness, float CardiacOutputModifier,
    float PericardialBloodMl, float TamponadeSeverity);

/// <summary>
/// Core-owned composition root for the integrated damage model. Lesions are the
/// sole injury input and one monotonic clock advances every mechanism exactly once.
/// </summary>
public sealed class ActorMedicalState : IAnatomicalInjuryTarget
{
    private readonly IDeterministicRandomStreamProvider _random;
    private readonly HashSet<string> _processedImpactIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _synchronizedLesions = new(StringComparer.Ordinal);
    private readonly MusculoskeletalFunctionalResolver _musculoskeletalResolver = new();
    private readonly NeurologicalFunctionalResolver _neurologicalResolver = new();
    private readonly PhysiologyCapabilityResolver _capabilityResolver = new();

    public ActorMedicalState(string actorId, CasualtyProfile profile, IAnatomicalStructureCatalog anatomy,
        IDeterministicRandomStreamProvider random, PhysiologicalUncertaintyOptions? uncertainty = null,
        TreatmentInventory? inventory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Profile = profile ?? throw new ArgumentNullException(nameof(profile)); Profile.Validate();
        Anatomy = anatomy ?? throw new ArgumentNullException(nameof(anatomy));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        RealizedVariation = PhysiologicalVariationSampler.Sample(random, actorId, uncertainty);
        LesionRepository = new LesionRepository();
        Hemorrhage = new(profile.BloodVolumeMilliliters * RealizedVariation.BloodVolumeMultiplier);
        Thoracic = new(Hemorrhage); Inventory = inventory ?? new TreatmentInventory();
        Musculoskeletal = MusculoskeletalFunctionalState.Healthy;
        Neurological = NeurologicalFunctionalState.Healthy;
        CasualtyState = CasualtyState.Effective;
        Capability = _capabilityResolver.Resolve(Hemorrhage.Cardiovascular, Hemorrhage.OxygenDelivery,
            CasualtyState, Musculoskeletal, Neurological);
    }

    public CasualtyProfile Profile { get; }
    public PhysiologicalVariation RealizedVariation { get; }
    public IAnatomicalStructureCatalog Anatomy { get; }
    public ILesionRepository LesionRepository { get; }
    public HemorrhagePhysiologyModel Hemorrhage { get; }
    public ThoracicInjuryModel Thoracic { get; }
    public TreatmentInventory Inventory { get; }
    public float SimulationTimeSeconds { get; private set; }
    public MusculoskeletalFunctionalState Musculoskeletal { get; private set; }
    public NeurologicalFunctionalState Neurological { get; private set; }
    public CapabilityState Capability { get; private set; }
    public CasualtyState CasualtyState { get; private set; }

    /// <summary>Adds an impact atomically. A repeated impact ID is a deterministic no-op.</summary>
    public bool ApplyImpact(string impactId, IEnumerable<Lesion> lesions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(impactId); ArgumentNullException.ThrowIfNull(lesions);
        if (_processedImpactIds.Contains(impactId)) return false;
        Lesion[] materialized = lesions.ToArray();
        if (materialized.Any(x => x.OriginImpactId != impactId)) throw new ArgumentException("Every lesion must belong to the supplied impact.", nameof(lesions));
        DateTimeOffset timestamp = DateTimeOffset.UnixEpoch.AddSeconds(SimulationTimeSeconds);
        LesionRepository.AddRange(materialized.Select(x => x with { CreatedAt = timestamp }));
        _processedImpactIds.Add(impactId);
        SynchronizeLesions();
        RefreshFunctionalState();
        RefreshCasualtyAndCapability();
        return true;
    }

    public void Tick(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
        SynchronizeLesions(); RefreshFunctionalState();
        Thoracic.NeurologicalVentilationModifier = Neurological.BrainstemFunction;
        Thoracic.NeurologicalCardiacModifier = Neurological.BrainstemFunction;
        Thoracic.Tick(seconds); SimulationTimeSeconds += seconds;
        RefreshCasualtyAndCapability();
    }

    public ActorMedicalSnapshot CaptureSnapshot()
    {
        ThoracicState t = Thoracic.State;
        return new(DamageModelVersion.IntegratedV3.ToIdentifier(), SimulationTimeSeconds, Profile, RealizedVariation,
            Array.AsReadOnly(LesionRepository.Lesions.ToArray()), Hemorrhage.Blood.CirculatingMilliliters,
            new Dictionary<BloodDestination,float>(Hemorrhage.Blood.LostByDestination), Hemorrhage.Cardiovascular, Hemorrhage.OxygenDelivery,
            new(t.Left.GasMilliliters, t.Right.GasMilliliters, t.Left.BloodMilliliters, t.Right.BloodMilliliters,
                t.VentilationEffectiveness, t.CardiacOutputModifier, t.PericardialBloodMilliliters, t.TamponadeSeverity),
            Musculoskeletal, Neurological, CasualtyState,
            new CapabilityState(new Dictionary<TacticalCapability,float>(Capability.Capacity), Array.AsReadOnly(Capability.Reasons.ToArray())),
            new Dictionary<string,int>(Inventory.Available, StringComparer.Ordinal), _random.CaptureSnapshot());
    }

    private void SynchronizeLesions()
    {
        foreach (Lesion lesion in LesionRepository.Lesions.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (!_synchronizedLesions.Add(lesion.Id)) continue;
            BleedingSource? source = BleedingSourceFactory.FromLesion(lesion, Anatomy);
            if (source is not null) Hemorrhage.AddSource(source);
            if (lesion.Kind is LesionKind.PleuralBreach or LesionKind.ParenchymalInjury)
                Thoracic.AddPleuralLesion(lesion, Anatomy, lesion.Severity * 12f,
                    lesion.Kind == LesionKind.PleuralBreach ? lesion.Geometry.Radius.Meters * 2f : 0f,
                    lesion.Kind == LesionKind.PleuralBreach);
        }
    }

    private void RefreshFunctionalState()
    {
        Musculoskeletal = _musculoskeletalResolver.Resolve(LesionRepository.Lesions, Anatomy);
        Neurological = _neurologicalResolver.Resolve(LesionRepository.Lesions, Anatomy);
    }

    private void RefreshCasualtyAndCapability()
    {
        CasualtyState = (CasualtyState)Math.Max(
            (int)Hemorrhage.CasualtyState,
            (int)Neurological.DirectCasualtyState);
        Capability = _capabilityResolver.Resolve(Hemorrhage.Cardiovascular, Hemorrhage.OxygenDelivery,
            CasualtyState, Musculoskeletal, Neurological);
    }
}
