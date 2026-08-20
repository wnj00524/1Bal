using System;

namespace TacticalSim.Core.Ballistics;

/// <summary>
/// User-facing measurements derived from a projectile's current flight state.
/// </summary>
public readonly record struct ProjectileTelemetry(float Velocity, float KineticEnergy, float Height)
{
    public static ProjectileTelemetry From(ProjectileState state, BallisticProfile profile)
    {
        float velocity = state.Velocity.Length();
        return new ProjectileTelemetry(
            velocity,
            0.5f * profile.Mass * velocity * velocity,
            state.Position.Y);
    }
}
