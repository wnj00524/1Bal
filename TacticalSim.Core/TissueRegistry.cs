using System;

namespace TacticalSim.Core.Physiology
{
    /// <summary>
    /// Central registry for predefined physiological tissue properties.
    /// Values are approximations for ballistic simulation purposes.
    /// </summary>
    public static class TissueRegistry
    {
        // Density in kg/m^3
        // Elasticity (higher means more resistant to stretch tearing)
        // ShearStrength in MPa

        public static readonly TissueProperties Muscle = new TissueProperties
        {
            Density = 1060f,
            Elasticity = 0.8f,
            ShearStrength = 0.5f
        };

        public static readonly TissueProperties Bone = new TissueProperties
        {
            Density = 1900f,
            Elasticity = 0.1f, // Brittle
            ShearStrength = 5.0f // Shatters under direct rifle fire but stops pistols
        };

        public static readonly TissueProperties Lung = new TissueProperties
        {
            Density = 300f, // Mostly air
            Elasticity = 0.9f, // Highly spongy
            ShearStrength = 0.2f
        };

        public static readonly TissueProperties Liver = new TissueProperties
        {
            Density = 1050f,
            Elasticity = 0.3f, // Less elastic than muscle, tears easily
            ShearStrength = 0.15f
        };
        
        public static readonly TissueProperties Brain = new TissueProperties
        {
            Density = 1040f,
            Elasticity = 0.1f, // Very low elasticity
            ShearStrength = 0.05f
        };
        public static readonly TissueProperties Heart = new TissueProperties
        {
            Density = 1060f,
            Elasticity = 0.6f, // Muscular but fluid-filled
            ShearStrength = 0.4f
        };

        public static readonly TissueProperties Stomach = new TissueProperties
        {
            Density = 1000f,
            Elasticity = 0.5f,
            ShearStrength = 0.2f
        };
    }
}
