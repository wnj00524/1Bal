using System.Numerics;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Physiology;

/// <summary>
/// Read/write compatibility boundary used while tactical actions still consume
/// <see cref="IActorPhysiology"/>. All medical state and progression are owned by
/// <see cref="ActorMedicalState"/>; voxels remain collision/material data only.
/// </summary>
public interface IIntegratedMedicalStateTarget
{
    ActorMedicalState MedicalState { get; }
}

public sealed class IntegratedActorPhysiology :
    IActorPhysiology,
    IAnatomicalInjuryTarget,
    IIntegratedMedicalStateTarget,
    IMusculoskeletalFunctionalTarget,
    INeurologicalFunctionalTarget
{
    private float _analgesicLevel;

    public IntegratedActorPhysiology(BodyPart rootBodyPart, ActorMedicalState medicalState)
    {
        RootBodyPart = rootBodyPart ?? throw new ArgumentNullException(nameof(rootBodyPart));
        MedicalState = medicalState ?? throw new ArgumentNullException(nameof(medicalState));
    }

    public ActorMedicalState MedicalState { get; }
    public BodyPart RootBodyPart { get; }
    public IAnatomicalStructureCatalog Anatomy => MedicalState.Anatomy;
    public ILesionRepository LesionRepository => MedicalState.LesionRepository;
    public float TotalBloodVolume => MedicalState.Hemorrhage.Blood.CirculatingMilliliters;
    public float ConsciousnessLevel => MedicalState.CasualtyState >= CasualtyState.Unconscious
        ? 0f
        : MedicalState.CasualtyState == CasualtyState.Incapacitated
            ? MathF.Min(.5f, MedicalState.Neurological.ConsciousnessLevel)
            : MedicalState.Neurological.ConsciousnessLevel;
    public float HeartRateBpm => MedicalState.Hemorrhage.Cardiovascular.HeartRateBpm;
    public float MeanArterialPressureMmhg => MedicalState.Hemorrhage.Cardiovascular.MeanArterialPressureMmhg;
    public float SystemicBleedRateMlPerSecond => MedicalState.Hemorrhage.CurrentBleedRateMlPerSecond;
    public float BreathingRatePerMinute => 12f * VentilationEffectiveness;
    public float AutonomicDrive => MedicalState.Neurological.BrainstemFunction;
    public float BrainstemFunction => MedicalState.Neurological.BrainstemFunction;
    public float AutonomicNerveFunction => MedicalState.Neurological.BrainstemFunction;
    public HemorrhageClass CurrentHemorrhageClass => LostBloodFraction switch
    {
        < .15f => HemorrhageClass.Class1,
        < .30f => HemorrhageClass.Class2,
        < .40f => HemorrhageClass.Class3,
        < .50f => HemorrhageClass.Class4,
        _ => HemorrhageClass.Fatal
    };

    public float BloodOxygenation => MedicalState.Hemorrhage.OxygenDelivery.ArterialSaturation;
    public float AirwayObstruction => Math.Clamp(1f - AirwayPatency, 0f, 1f);
    public float AirwayPatency => 1f - MaximumLesionSeverity(LesionKind.AirwayDisruption);
    public float VentilationEffectiveness => MedicalState.Hemorrhage.OxygenDelivery.VentilationEffectiveness;
    public float AlveolarBloodAccumulation => MedicalState.Hemorrhage.Blood.LostByDestination[BloodDestination.Airway];
    public float TensionPneumothoraxLevel => Math.Clamp(
        MathF.Max(MedicalState.Thoracic.Left.PressureKpa, MedicalState.Thoracic.Right.PressureKpa) / 2.5f,
        0f,
        1f);
    public bool HasChestSeal => MedicalState.Thoracic.Left.SealState != ChestSealState.None
        || MedicalState.Thoracic.Right.SealState != ChestSealState.None;
    public float CirculationEffectiveness => MedicalState.Hemorrhage.Cardiovascular.PerfusionEffectiveness;
    public float CerebralOxygenation => MedicalState.Hemorrhage.OxygenDelivery.CerebralDeliveryIndex;
    public float BrainHypoxiaSeconds => MedicalState.Hemorrhage.LowCerebralDeliverySeconds;
    public bool IsDead => MedicalState.CasualtyState == CasualtyState.Dead;
    public float PainLevel => Math.Clamp(
        LesionRepository.Lesions.Count == 0 ? 0f : LesionRepository.Lesions.Max(x => x.Severity) - _analgesicLevel,
        0f,
        1f);
    public float ShockLevel => MedicalState.CasualtyState switch
    {
        CasualtyState.Effective => 0f,
        CasualtyState.Incapacitated => .6f,
        _ => 1f
    };
    public float AnalgesicLevel => _analgesicLevel;
    public float MobilityLevel => MedicalState.Capability[TacticalCapability.Movement];
    public float WeaponHandlingLevel => MathF.Min(
        MedicalState.Capability[TacticalCapability.Aiming],
        MedicalState.Capability[TacticalCapability.Firing]);
    public bool CanStand => MedicalState.Capability[TacticalCapability.Posture] > 0f;
    public MusculoskeletalFunctionalState MusculoskeletalFunctionalState => MedicalState.Musculoskeletal;
    public NeurologicalFunctionalState NeurologicalFunctionalState => MedicalState.Neurological;

    public bool ApplyImpact(string impactId, IEnumerable<Lesion> lesions) =>
        MedicalState.ApplyImpact(impactId, lesions);

    public void TickPhysiology(float dt) => MedicalState.Tick(dt);

    public void RefreshMusculoskeletalFunctionalState() { }

    public void RefreshNeurologicalFunctionalState() { }

    public void AdministerAnalgesic(float strength) =>
        _analgesicLevel = Math.Clamp(_analgesicLevel + MathF.Max(0f, strength), 0f, 1f);

    public void ApplyChestSeal()
    {
        MedicalState.Thoracic.ApplyChestSeal(ThoracicSide.Left, ChestSealState.Effective);
        MedicalState.Thoracic.ApplyChestSeal(ThoracicSide.Right, ChestSealState.Effective);
    }

    public void PerformNeedleDecompression()
    {
        ThoracicSide side = MedicalState.Thoracic.Left.PressureKpa >= MedicalState.Thoracic.Right.PressureKpa
            ? ThoracicSide.Left
            : ThoracicSide.Right;
        MedicalState.Thoracic.NeedleDecompress(side);
    }

    public bool ApplyTourniquet(BodyPartType extremity) =>
        ApplyBleedingControl(extremity, BleedingControlState.Tourniquet, requireCompressible: true);

    public bool PackExternalWound(BodyPartType bodyPart) =>
        ApplyBleedingControl(bodyPart, BleedingControlState.Packed, requireCompressible: true);

    public void ProcessLegacyImpact(
        Vector3 trajectory,
        Energy kineticEnergy,
        Vector3 hitPoint,
        DamageModelVersion modelVersion) =>
        throw new InvalidOperationException(
            "Integrated actors reject legacy point-trauma mutation. Construct a legacy actor for model comparison.");

    private float LostBloodFraction => 1f - TotalBloodVolume / MedicalState.Hemorrhage.Blood.BaselineMilliliters;

    private float MaximumLesionSeverity(LesionKind kind) => LesionRepository.Lesions
        .Where(x => x.Kind == kind)
        .Select(x => x.Severity)
        .DefaultIfEmpty(0f)
        .Max();

    private bool ApplyBleedingControl(
        BodyPartType region,
        BleedingControlState control,
        bool requireCompressible)
    {
        bool changed = false;
        foreach (BleedingSource source in MedicalState.Hemorrhage.Sources)
        {
            if (requireCompressible && !source.Compressible)
                continue;

            Lesion? lesion = LesionRepository.Lesions.FirstOrDefault(x => x.Id == source.LesionId);
            if (lesion is null)
                continue;

            AnatomicalStructure structure;
            try { structure = Anatomy.GetRequired(lesion.StructureId); }
            catch (KeyNotFoundException) { continue; }
            if (structure.Region != region)
                continue;

            changed |= MedicalState.Hemorrhage.TryControlSource(source.LesionId, control);
        }
        return changed;
    }
}
