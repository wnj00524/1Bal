namespace TacticalSim.Core.Materials
{
    using TacticalSim.Core.Units;
    using MaterialDensity = TacticalSim.Core.Units.Density;

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
        /// Relative resistance to localized deformation. Values are expressed on a
        /// dimensionless engineering scale so that material data can be calibrated
        /// without coupling the registry to a particular hardness test.
        /// </summary>
        public float Hardness { get; set; }

        /// <summary>
        /// Compressive yield strength in megapascals (MPa).
        /// </summary>
        public float YieldStrength { get; set; }

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

        /// <summary>Typed view of <see cref="Density" />, stored in kg/m³.</summary>
        public MaterialDensity MassDensity => MaterialDensity.FromKilogramsPerCubicMeter(Density);

        /// <summary>Typed view of <see cref="YieldStrength" />; the legacy field is in MPa.</summary>
        public Pressure YieldStrengthPressure => Pressure.FromMegapascals(YieldStrength);

        /// <summary>Typed view of <see cref="YieldEnergyThreshold" />, stored in joules.</summary>
        public Energy YieldEnergy => Energy.FromJoules(YieldEnergyThreshold);

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
            Hardness = resistanceCoefficient;
            // Preserve compatibility with the original energy-threshold profiles
            // while exposing a positive strength value to geometry consumers.
            YieldStrength = yieldEnergyThreshold;
        }

        public MaterialProperties(
            string name,
            MaterialType type,
            float density,
            float hardness,
            float yieldStrength)
            : this(name, type, density, hardness, MathF.PI / 2f, 0f)
        {
            YieldStrength = yieldStrength;
        }
    }
}
