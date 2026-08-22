using System;
using System.Numerics;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core.Ballistics;

public enum BoneImpactOutcome
{
    Shattered,
    Ricocheted
}

public readonly record struct BoneImpactResult(
    BoneImpactOutcome Outcome,
    Vector3 Velocity,
    float TransferredEnergy,
    float ShatterThreshold);

/// <summary>
/// Resolves deterministic impacts between a projectile and a dense bone surface.
/// </summary>
public static class InternalRicochetSolver
{
    private const float ReferenceBoneDensity = 1_900f;

    public static BoneImpactResult Resolve(
        Vector3 velocity,
        in BallisticProfile profile,
        Vector3 surfaceNormal,
        in TissueProperties bone,
        float boneThickness)
    {
        if (profile.Mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(profile), "Projectile mass must be positive.");
        if (profile.CrossSectionalArea <= 0f)
            throw new ArgumentOutOfRangeException(nameof(profile), "Projectile area must be positive.");
        if (boneThickness <= 0f)
            throw new ArgumentOutOfRangeException(nameof(boneThickness));
        if (velocity.LengthSquared() <= 0f)
            throw new ArgumentOutOfRangeException(nameof(velocity));
        if (surfaceNormal.LengthSquared() <= 0f)
            throw new ArgumentOutOfRangeException(nameof(surfaceNormal));

        Vector3 direction = Vector3.Normalize(velocity);
        Vector3 normal = Vector3.Normalize(surfaceNormal);
        if (Vector3.Dot(direction, normal) > 0f)
            normal = -normal;

        float speedSquared = velocity.LengthSquared();
        float kineticEnergy = 0.5f * profile.Mass * speedSquared;
        float incidence = MathF.Abs(Vector3.Dot(direction, normal));

        // Work required to shear a projectile-width channel through the bone. Density
        // scales the nominal shear work so cortical bone resists more than porous bone.
        float densityScale = MathF.Max(0f, bone.Density) / ReferenceBoneDensity;
        float shatterThreshold = MathF.Max(0f, bone.ShearStrengthPressure.Pascals)
            * profile.CrossSectionalArea * boneThickness * densityScale;
        float normalImpactEnergy = kineticEnergy * incidence * incidence;

        if (normalImpactEnergy >= shatterThreshold)
        {
            return new BoneImpactResult(
                BoneImpactOutcome.Shattered,
                velocity,
                MathF.Min(kineticEnergy, shatterThreshold),
                shatterThreshold);
        }

        // A grazing impact loses less energy than a near-normal impact. Keeping the
        // formula deterministic makes identical simulation inputs replay identically.
        float lossFraction = 0.15f + 0.30f * incidence;
        float retainedEnergy = kineticEnergy * (1f - lossFraction);
        float exitSpeed = MathF.Sqrt(2f * retainedEnergy / profile.Mass);
        Vector3 reflectedDirection = Vector3.Normalize(Vector3.Reflect(direction, normal));

        return new BoneImpactResult(
            BoneImpactOutcome.Ricocheted,
            reflectedDirection * exitSpeed,
            kineticEnergy - retainedEnergy,
            shatterThreshold);
    }
}
