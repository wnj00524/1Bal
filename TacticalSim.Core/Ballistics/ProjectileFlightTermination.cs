using System.Numerics;
using TacticalSim.Core.World;

namespace TacticalSim.Core.Ballistics;

/// <summary>Describes why a projectile is no longer tracked by the simulation.</summary>
public enum ProjectileTerminationReason
{
    None,
    EnergyDepleted,
    GroundImpact,
    WorldBoundary
}

/// <summary>Centralizes the world-space terminal conditions for a projectile flight.</summary>
public static class ProjectileFlightTermination
{
    public const float MinimumKineticEnergyJoules = 0.01f;

    public static ProjectileTerminationReason Evaluate(
        in ProjectileState state,
        in BallisticProfile profile,
        in WorldBounds bounds,
        float groundHeight = 0f)
    {
        if (profile.Mass <= 0f || !float.IsFinite(profile.Mass))
            throw new ArgumentOutOfRangeException(nameof(profile), "Projectile mass must be finite and positive.");
        if (!float.IsFinite(groundHeight))
            throw new ArgumentOutOfRangeException(nameof(groundHeight));

        float kineticEnergy = 0.5f * profile.Mass * state.Velocity.LengthSquared();
        if (!float.IsFinite(kineticEnergy) || kineticEnergy <= MinimumKineticEnergyJoules)
            return ProjectileTerminationReason.EnergyDepleted;

        // Ground collisions are terminal only while travelling into the surface. A
        // projectile already moving upward has ricocheted and remains in flight.
        if (state.Position.Y <= groundHeight && state.Velocity.Y <= 0f)
            return ProjectileTerminationReason.GroundImpact;

        return bounds.Contains(state.Position)
            ? ProjectileTerminationReason.None
            : ProjectileTerminationReason.WorldBoundary;
    }
}
