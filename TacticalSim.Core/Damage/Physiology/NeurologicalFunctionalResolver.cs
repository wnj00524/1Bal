using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core.Damage.Physiology;

/// <summary>Per-limb motor capacity derived from persistent nerve lesions.</summary>
public sealed record NeurologicalFunctionalState(
    float LeftUpperLimbCapacity,
    float RightUpperLimbCapacity,
    float LeftLowerLimbCapacity,
    float RightLowerLimbCapacity,
    float CognitiveCapacity = 1f,
    float BrainstemFunction = 1f,
    CasualtyState DirectCasualtyState = CasualtyState.Effective)
{
    public static NeurologicalFunctionalState Healthy { get; } = new(1f, 1f, 1f, 1f, 1f, 1f, CasualtyState.Effective);
    public float UpperLimbCapacity => MathF.Min(LeftUpperLimbCapacity, RightUpperLimbCapacity);
    public float LowerLimbCapacity => MathF.Min(LeftLowerLimbCapacity, RightLowerLimbCapacity);
    public float ConsciousnessLevel => DirectCasualtyState >= CasualtyState.Unconscious
        ? 0f
        : DirectCasualtyState == CasualtyState.Incapacitated
            ? MathF.Min(.5f, CognitiveCapacity)
            : CognitiveCapacity;
}

/// <summary>
/// Deterministic lesion-to-neurological-state parameters. These thresholds are
/// provisional gameplay calibration, not clinical prognostic cut-offs.
/// </summary>
public sealed record NeurologicalModelParameters(
    float IncapacitationSeverity = .15f,
    float UnconsciousSeverity = .30f,
    float FatalSeverity = .85f,
    float CognitiveLossMultiplier = 2.5f,
    float BrainstemLossMultiplier = .5f)
{
    public void Validate()
    {
        if (!float.IsFinite(IncapacitationSeverity)
            || !float.IsFinite(UnconsciousSeverity)
            || !float.IsFinite(FatalSeverity)
            || IncapacitationSeverity is < 0f or > 1f
            || UnconsciousSeverity is < 0f or > 1f
            || FatalSeverity is < 0f or > 1f
            || IncapacitationSeverity >= UnconsciousSeverity
            || UnconsciousSeverity >= FatalSeverity
            || !float.IsFinite(CognitiveLossMultiplier)
            || CognitiveLossMultiplier < 0f
            || !float.IsFinite(BrainstemLossMultiplier)
            || BrainstemLossMultiplier < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(NeurologicalModelParameters));
        }
    }
}

public interface INeurologicalFunctionalTarget
{
    NeurologicalFunctionalState NeurologicalFunctionalState { get; }
    void RefreshNeurologicalFunctionalState();
}

public interface INeurologicalFunctionalResolver
{
    NeurologicalFunctionalState Resolve(IReadOnlyList<Lesion> lesions, IAnatomicalStructureCatalog anatomy);
}

/// <summary>
/// Deterministic M6 nerve-to-motor bridge. Capacity values are provisional
/// gameplay calibration pending the M12 validation milestone.
/// </summary>
public sealed class NeurologicalFunctionalResolver : INeurologicalFunctionalResolver
{
    public const float NeuropraxiaCapacity = .80f;
    public const float PartialDisruptionCapacity = .40f;
    public const float CompleteDisruptionCapacity = 0f;
    private readonly NeurologicalModelParameters _parameters;

    public NeurologicalFunctionalResolver(NeurologicalModelParameters? parameters = null)
    {
        _parameters = parameters ?? new NeurologicalModelParameters();
        _parameters.Validate();
    }

    public NeurologicalFunctionalState Resolve(IReadOnlyList<Lesion> lesions, IAnatomicalStructureCatalog anatomy)
    {
        ArgumentNullException.ThrowIfNull(lesions);
        ArgumentNullException.ThrowIfNull(anatomy);
        float leftUpper=1f,rightUpper=1f,leftLower=1f,rightLower=1f;
        float maximumBrainSeverity = 0f;

        foreach (Lesion lesion in lesions)
        {
            if (IsBrainLesion(lesion, anatomy))
                maximumBrainSeverity = MathF.Max(maximumBrainSeverity, lesion.Severity);
        }

        foreach (NerveLesion lesion in lesions.OfType<NerveLesion>())
        {
            float capacity = CapacityFor(lesion.Grade);
            FunctionalRole role = TryGetRole(anatomy, lesion.StructureId);
            if (role == FunctionalRole.SpinalCord)
            {
                bool upper = string.Equals(lesion.NeurologicalLevel, "cervical", StringComparison.OrdinalIgnoreCase);
                ApplyBySide(lesion.Laterality, capacity, ref leftLower, ref rightLower);
                if (upper) ApplyBySide(lesion.Laterality, capacity, ref leftUpper, ref rightUpper);
            }
            else if (role == FunctionalRole.UpperLimbMotor)
                ApplyBySide(lesion.Laterality, capacity, ref leftUpper, ref rightUpper);
            else if (role == FunctionalRole.LowerLimbMotor)
                ApplyBySide(lesion.Laterality, capacity, ref leftLower, ref rightLower);
        }
        float cognitiveCapacity = Math.Clamp(
            1f - maximumBrainSeverity * _parameters.CognitiveLossMultiplier,
            0f,
            1f);
        float brainstemFunction = Math.Clamp(
            1f - maximumBrainSeverity * _parameters.BrainstemLossMultiplier,
            0f,
            1f);
        CasualtyState directState = maximumBrainSeverity >= _parameters.FatalSeverity
            ? CasualtyState.Dead
            : maximumBrainSeverity >= _parameters.UnconsciousSeverity
                ? CasualtyState.Unconscious
                : maximumBrainSeverity >= _parameters.IncapacitationSeverity
                    ? CasualtyState.Incapacitated
                    : CasualtyState.Effective;

        return new(leftUpper,rightUpper,leftLower,rightLower,
            cognitiveCapacity,brainstemFunction,directState);
    }

    public static float CapacityFor(NerveDamageGrade grade) => grade switch
    {
        NerveDamageGrade.Neuropraxia => NeuropraxiaCapacity,
        NerveDamageGrade.PartialDisruption => PartialDisruptionCapacity,
        NerveDamageGrade.CompleteDisruption => CompleteDisruptionCapacity,
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Unknown nerve damage grade.")
    };

    private static void ApplyBySide(string? side,float capacity,ref float left,ref float right)
    {
        if (string.Equals(side,"left",StringComparison.OrdinalIgnoreCase)) left=MathF.Min(left,capacity);
        else if (string.Equals(side,"right",StringComparison.OrdinalIgnoreCase)) right=MathF.Min(right,capacity);
        else { left=MathF.Min(left,capacity); right=MathF.Min(right,capacity); }
    }

    private static FunctionalRole TryGetRole(IAnatomicalStructureCatalog anatomy,string id)
    {
        try { return anatomy.GetRequired(id).FunctionalRole; }
        catch (KeyNotFoundException) { return FunctionalRole.None; }
    }

    private static bool IsBrainLesion(Lesion lesion, IAnatomicalStructureCatalog anatomy)
    {
        if (lesion.Kind != LesionKind.BrainOrSpinalInjury)
            return false;

        try
        {
            AnatomicalStructure structure = anatomy.GetRequired(lesion.StructureId);
            return structure.Region == BodyPartType.Head
                && structure.Type == AnatomicalStructureType.Organ;
        }
        catch (KeyNotFoundException)
        {
            return string.Equals(lesion.StructureId, "organ.brain", StringComparison.Ordinal);
        }
    }
}
