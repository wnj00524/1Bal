using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;

namespace TacticalSim.Core.Damage.Physiology;

/// <summary>Per-limb motor capacity derived from persistent nerve lesions.</summary>
public sealed record NeurologicalFunctionalState(
    float LeftUpperLimbCapacity,
    float RightUpperLimbCapacity,
    float LeftLowerLimbCapacity,
    float RightLowerLimbCapacity)
{
    public static NeurologicalFunctionalState Healthy { get; } = new(1f, 1f, 1f, 1f);
    public float UpperLimbCapacity => MathF.Min(LeftUpperLimbCapacity, RightUpperLimbCapacity);
    public float LowerLimbCapacity => MathF.Min(LeftLowerLimbCapacity, RightLowerLimbCapacity);
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

    public NeurologicalFunctionalState Resolve(IReadOnlyList<Lesion> lesions, IAnatomicalStructureCatalog anatomy)
    {
        ArgumentNullException.ThrowIfNull(lesions);
        ArgumentNullException.ThrowIfNull(anatomy);
        float leftUpper=1f,rightUpper=1f,leftLower=1f,rightLower=1f;

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
        return new(leftUpper,rightUpper,leftLower,rightLower);
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
}
