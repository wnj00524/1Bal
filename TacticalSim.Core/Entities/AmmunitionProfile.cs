using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Entities
{
    public record AmmunitionProfile
    {
        public string Name { get; init; } = string.Empty;
        public float MuzzleVelocity { get; init; } // m/s
        public BallisticProfile Ballistics { get; init; }
    }
}
