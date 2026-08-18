namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Represents physical properties of an environmental cover or armor material.
    /// </summary>
    public struct MaterialProperties
    {
        /// <summary>
        /// Human-readable material name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Material classification type.
        /// </summary>
        public MaterialType Type { get; set; }

        /// <summary>
        /// Mass density in kg/m^3.
        /// </summary>
        public float Density { get; set; }

        /// <summary>
        /// Dimensionless medium resistance / drag coefficient multiplier.
        /// </summary>
        public float ResistanceCoefficient { get; set; }

        /// <summary>
        /// Angle of incidence threshold in radians above which projectile ricochets off the barrier surface.
        /// </summary>
        public float RicochetAngleThreshold { get; set; }

        /// <summary>
        /// Minimum initial kinetic energy in Joules required to overcome material yield strength and initiate perforation.
        /// </summary>
        public float YieldEnergyThreshold { get; set; }

        public MaterialProperties(
            string name,
            MaterialType type,
            float density,
            float resistanceCoefficient,
            float ricochetAngleThreshold,
            float yieldEnergyThreshold)
        {
            Name = name;
            Type = type;
            Density = density;
            ResistanceCoefficient = resistanceCoefficient;
            RicochetAngleThreshold = ricochetAngleThreshold;
            YieldEnergyThreshold = yieldEnergyThreshold;
        }
    }
}
