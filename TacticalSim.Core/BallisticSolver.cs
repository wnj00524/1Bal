using System;
using System.Numerics;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;
using ProjectileMass = TacticalSim.Core.Units.Mass;

namespace TacticalSim.Core.Ballistics
{
    /// <summary>
    /// Represents the kinematic state of a projectile at a given time.
    /// </summary>
    public struct ProjectileState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Time;

        /// <summary>Typed view of <see cref="Time" />, stored in seconds.</summary>
        public SimulationTime TimeSeconds
        {
            readonly get => SimulationTime.FromSeconds(Time);
            set => Time = value.Seconds;
        }
    }

    /// <summary>
    /// Represents the properties of a projectile.
    /// </summary>
    public struct BallisticProfile
    {
        public float Mass; // kg
        public float CrossSectionalArea; // m^2
        public IDragModel DragModel;

        /// <summary>Typed compatibility view of <see cref="Mass" />, stored in kilograms.</summary>
        public readonly ProjectileMass MassKilograms => ProjectileMass.FromKilograms(Mass);

        /// <summary>Typed compatibility view of <see cref="CrossSectionalArea" />, stored in m².</summary>
        public readonly Area CrossSectionalAreaSquareMeters => Area.FromSquareMeters(CrossSectionalArea);
    }

    /// <summary>
    /// Solves projectile trajectories using 4th Order Runge-Kutta (RK4) integration.
    /// </summary>
    public static class BallisticSolver
    {
        /// <summary>
        /// Calculates the derivative [velocity, acceleration] for a given state.
        /// </summary>
        private static (Vector3 velocity, Vector3 acceleration) ComputeDerivatives(
            Vector3 position,
            Vector3 velocity,
            in BallisticProfile profile,
            IEnvironmentModel environment)
        {
            // Get local environmental conditions at the current integration step position
            EnvironmentState env = environment.GetConditionsAt(position);

            // Relative velocity of the projectile with respect to the wind
            Vector3 relativeVelocity = velocity - env.WindVelocity;
            float speedSquared = relativeVelocity.LengthSquared();
            float speed = MathF.Sqrt(speedSquared);

            // Calculate Mach number and get dynamic drag coefficient
            float mach = speed / env.SpeedOfSound;
            float cd = profile.DragModel.GetDragCoefficient(mach);

            // Aerodynamic drag force: F_d = 0.5 * rho * v^2 * Cd * A
            // Drag acceleration: a_d = F_d / m
            float dragFactor = -0.5f * env.AirDensity * cd * profile.CrossSectionalAreaSquareMeters.SquareMeters / profile.MassKilograms.Kilograms;
            
            Vector3 dragAcceleration = Vector3.Zero;
            if (speed > 0.0001f)
            {
                dragAcceleration = dragFactor * speed * relativeVelocity;
            }

            Vector3 totalAcceleration = env.Gravity + dragAcceleration;
            return (velocity, totalAcceleration);
        }

        /// <summary>
        /// Advances the projectile state by a discrete timestep dt using RK4.
        /// </summary>
        public static ProjectileState StepRK4(
            in ProjectileState state, 
            in BallisticProfile profile, 
            IEnvironmentModel environment,
            float dt)
        {
            Vector3 p = state.Position;
            Vector3 v = state.Velocity;

            // k1
            var (dp1, dv1) = ComputeDerivatives(p, v, profile, environment);
            
            // k2
            var (dp2, dv2) = ComputeDerivatives(
                p + dp1 * (dt * 0.5f), 
                v + dv1 * (dt * 0.5f), 
                profile,
                environment);
            
            // k3
            var (dp3, dv3) = ComputeDerivatives(
                p + dp2 * (dt * 0.5f), 
                v + dv2 * (dt * 0.5f), 
                profile,
                environment);
            
            // k4
            var (dp4, dv4) = ComputeDerivatives(
                p + dp3 * dt, 
                v + dv3 * dt, 
                profile,
                environment);

            // Final state
            Vector3 nextP = p + (dt / 6.0f) * (dp1 + 2.0f * dp2 + 2.0f * dp3 + dp4);
            Vector3 nextV = v + (dt / 6.0f) * (dv1 + 2.0f * dv2 + 2.0f * dv3 + dv4);

            return new ProjectileState
            {
                Position = nextP,
                Velocity = nextV,
                Time = (state.TimeSeconds + SimulationTime.FromSeconds(dt)).Seconds
            };
        }
    }
}
