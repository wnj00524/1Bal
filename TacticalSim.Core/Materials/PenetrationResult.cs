using System.Numerics;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Encapsulates the complete kinematic, energy, and geometric result of a projectile barrier penetration interaction.
    /// </summary>
    public struct PenetrationResult
    {
        /// <summary>
        /// Terminal ballistics outcome classification.
        /// </summary>
        public PenetrationOutcome Outcome { get; set; }

        /// <summary>
        /// Point where the projectile struck or entered the barrier.
        /// </summary>
        public Vector3 EntryPoint { get; set; }

        /// <summary>
        /// Point where the projectile exited or stopped within the barrier.
        /// </summary>
        public Vector3 ExitPoint { get; set; }

        /// <summary>
        /// Total material distance traversed through the barrier along the trajectory vector in meters.
        /// </summary>
        public float EffectiveThickness { get; set; }

        /// <summary>
        /// Angle between the incoming trajectory vector and the barrier normal in radians.
        /// </summary>
        public float AngleOfIncidence { get; set; }

        /// <summary>
        /// Impact speed prior to entering the material in m/s.
        /// </summary>
        public float InitialVelocity { get; set; }

        /// <summary>
        /// Residual projectile speed upon exiting or ricocheting in m/s (0 if stopped).
        /// </summary>
        public float ExitVelocity { get; set; }

        /// <summary>
        /// Initial kinetic energy prior to impact in Joules.
        /// </summary>
        public float InitialKineticEnergy { get; set; }

        /// <summary>
        /// Residual kinetic energy after penetration or ricochet in Joules.
        /// </summary>
        public float RemainingKineticEnergy { get; set; }

        /// <summary>
        /// Work done / kinetic energy transferred to the barrier in Joules.
        /// </summary>
        public float TransferredKineticEnergy { get; set; }

        /// <summary>
        /// Residual velocity vector in m/s.
        /// </summary>
        public Vector3 ExitVelocityVector { get; set; }

        /// <summary>
        /// Kinematic projectile state after exiting the barrier or ricocheting.
        /// </summary>
        public ProjectileState ExitState { get; set; }
    }
}
