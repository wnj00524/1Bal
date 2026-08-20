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
            ShearStrength = 0.5f,
            PainReceptorDensity = 1.0f
        };

        public static readonly TissueProperties Bone = new TissueProperties
        {
            Density = 1900f,
            Elasticity = 0.1f, // Brittle
            ShearStrength = 5.0f, // Shatters under direct rifle fire but stops pistols
            PainReceptorDensity = 2.0f
        };

        public static readonly TissueProperties Lung = new TissueProperties
        {
            Density = 300f, // Mostly air
            Elasticity = 0.9f, // Highly spongy
            ShearStrength = 0.2f,
            PainReceptorDensity = 0.5f
        };

        public static readonly TissueProperties Liver = new TissueProperties
        {
            Density = 1050f,
            Elasticity = 0.3f, // Less elastic than muscle, tears easily
            ShearStrength = 0.15f,
            PainReceptorDensity = 0.5f
        };
        
        public static readonly TissueProperties Brain = new TissueProperties
        {
            Density = 1040f,
            Elasticity = 0.1f, // Very low elasticity
            ShearStrength = 0.05f,
            PainReceptorDensity = 0.0f
        };
        public static readonly TissueProperties Heart = new TissueProperties
        {
            Density = 1060f,
            Elasticity = 0.6f, // Muscular but fluid-filled
            ShearStrength = 0.4f,
            PainReceptorDensity = 0.5f
        };

        public static readonly TissueProperties Stomach = new TissueProperties
        {
            Density = 1000f,
            Elasticity = 0.5f,
            ShearStrength = 0.2f,
            PainReceptorDensity = 1.5f
        };

        public static readonly TissueProperties Spleen = new TissueProperties
        {
            Density = 1060f,
            Elasticity = 0.2f, // Very fragile
            ShearStrength = 0.1f,
            PainReceptorDensity = 0.5f
        };

        public static readonly TissueProperties Kidney = new TissueProperties
        {
            Density = 1050f,
            Elasticity = 0.4f,
            ShearStrength = 0.2f,
            PainReceptorDensity = 0.5f
        };

        public static readonly TissueProperties Intestines = new TissueProperties
        {
            Density = 1000f,
            Elasticity = 0.6f, // Somewhat stretchy
            ShearStrength = 0.15f,
            PainReceptorDensity = 1.5f
        };

        public static readonly TissueProperties Airway = new TissueProperties
        {
            Density = 600f, // Mix of cartilage and air
            Elasticity = 0.5f,
            ShearStrength = 0.3f, // Cartilage is tough
            PainReceptorDensity = 1.0f
        };

        public static readonly TissueProperties Mouth = new TissueProperties
        {
            Density = 1040f,
            Elasticity = 0.7f,
            ShearStrength = 0.2f,
            PainReceptorDensity = 1.0f
        };

        public static readonly TissueProperties Eye = new TissueProperties
        {
            Density = 1000f, // Vitreous humor
            Elasticity = 0.8f,
            ShearStrength = 0.05f, // Pops easily
            PainReceptorDensity = 2.0f
        };
    }
}
