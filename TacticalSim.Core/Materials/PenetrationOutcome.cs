namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Represents the terminal ballistic outcome of a projectile interacting with a material barrier.
    /// </summary>
    public enum PenetrationOutcome
    {
        /// <summary>
        /// Projectile successfully passed through the barrier with remaining velocity > 0.
        /// </summary>
        Perforated,

        /// <summary>
        /// Projectile was arrested/stopped inside the barrier; velocity reduced to 0.
        /// </summary>
        Stopped,

        /// <summary>
        /// Projectile glanced off the surface due to high angle of incidence obliquity.
        /// </summary>
        Ricochet,

        /// <summary>
        /// Trajectory did not intersect the barrier.
        /// </summary>
        Miss
    }
}
