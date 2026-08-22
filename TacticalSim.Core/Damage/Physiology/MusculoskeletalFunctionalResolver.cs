using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;

namespace TacticalSim.Core.Damage.Physiology;

/// <summary>
/// The bounded M6 functional state derived from bone lesions. Broader physiology-
/// to-capability decisions remain the responsibility of DM-207.
/// </summary>
public sealed record MusculoskeletalFunctionalState(
    float StandingCapacity,
    float MovementCapacity,
    float UpperLimbCapacity,
    bool CanStand)
{
    public static MusculoskeletalFunctionalState Healthy { get; } = new(1f, 1f, 1f, true);
}

/// <summary>
/// Separate capability-input boundary for actors that consume musculoskeletal
/// lesion consequences. Anatomical injury targets need not implement it.
/// </summary>
public interface IMusculoskeletalFunctionalTarget
{
    MusculoskeletalFunctionalState MusculoskeletalFunctionalState { get; }
    void RefreshMusculoskeletalFunctionalState();
}

/// <summary>Pure, deterministic translation from fractures and anatomy roles to function.</summary>
public interface IMusculoskeletalFunctionalResolver
{
    MusculoskeletalFunctionalState Resolve(
        IReadOnlyList<Lesion> lesions,
        IAnatomicalStructureCatalog anatomy);
}

/// <summary>
/// Resolves only direct musculoskeletal effects introduced by DM-104. Capacity
/// values are provisional gameplay calibration pending validation in M12.
/// </summary>
public sealed class MusculoskeletalFunctionalResolver : IMusculoskeletalFunctionalResolver
{
    public const float HealthyCapacity = 1f;
    public const float LimitedUseCapacity = 0.75f;
    public const float SevereRestrictionCapacity = 0.40f;
    public const float StructuralFunctionLostCapacity = 0f;

    public MusculoskeletalFunctionalState Resolve(
        IReadOnlyList<Lesion> lesions,
        IAnatomicalStructureCatalog anatomy)
    {
        ArgumentNullException.ThrowIfNull(lesions);
        ArgumentNullException.ThrowIfNull(anatomy);

        float standingCapacity = HealthyCapacity;
        float movementCapacity = HealthyCapacity;
        float upperLimbCapacity = HealthyCapacity;

        foreach (FractureLesion fracture in lesions.OfType<FractureLesion>())
        {
            float fractureCapacity = CapacityFor(fracture.FunctionalConsequence);
            bool hasCanonicalRole = TryGetFunctionalRole(
                anatomy,
                fracture.StructureId,
                out FunctionalRole role);

            // The anatomy role is the current source of truth. WeightBearing is
            // retained as a compatibility fallback for older/custom catalogs and
            // fracture payloads only when the structure is absent from the catalog.
            bool affectsWeightBearing = role == FunctionalRole.WeightBearing
                || (!hasCanonicalRole && fracture.WeightBearing);

            if (affectsWeightBearing)
            {
                standingCapacity = MathF.Min(standingCapacity, fractureCapacity);
                movementCapacity = MathF.Min(movementCapacity, fractureCapacity);
            }

            if (role == FunctionalRole.LowerLimbMotor)
                movementCapacity = MathF.Min(movementCapacity, fractureCapacity);

            if (role == FunctionalRole.UpperLimbMotor)
                upperLimbCapacity = MathF.Min(upperLimbCapacity, fractureCapacity);
        }

        return new MusculoskeletalFunctionalState(
            standingCapacity,
            movementCapacity,
            upperLimbCapacity,
            standingCapacity > StructuralFunctionLostCapacity);
    }

    public static float CapacityFor(FractureFunctionalConsequence consequence) => consequence switch
    {
        FractureFunctionalConsequence.LimitedUse => LimitedUseCapacity,
        FractureFunctionalConsequence.SevereRestriction => SevereRestrictionCapacity,
        FractureFunctionalConsequence.StructuralFunctionLost => StructuralFunctionLostCapacity,
        _ => throw new ArgumentOutOfRangeException(nameof(consequence), consequence, "Unknown fracture consequence.")
    };

    private static bool TryGetFunctionalRole(
        IAnatomicalStructureCatalog anatomy,
        string structureId,
        out FunctionalRole role)
    {
        try
        {
            role = anatomy.GetRequired(structureId).FunctionalRole;
            return true;
        }
        catch (KeyNotFoundException)
        {
            role = FunctionalRole.None;
            return false;
        }
    }
}
