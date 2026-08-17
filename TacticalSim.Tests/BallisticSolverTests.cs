using System;
using System.Numerics;
using Xunit;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Tests
{
    // A simple vacuum environment mock
    public class VacuumEnvironment : IEnvironmentModel
    {
        public EnvironmentState GetConditionsAt(Vector3 position)
        {
            return new EnvironmentState
            {
                WindVelocity = Vector3.Zero,
                Gravity = new Vector3(0, -9.81f, 0),
                AirDensity = 0f,
                SpeedOfSound = 343f // irrelevant in vacuum, but prevents div by zero
            };
        }
    }

    public class BallisticSolverTests
    {
        [Fact]
        public void VacuumTrajectory_FollowsParabolicKinematics()
        {
            // Arrange
            float initialVelocityZ = 800f; // m/s
            var state = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, initialVelocityZ),
                Time = 0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.01f, // 10 grams
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.5f)
            };

            var environment = new VacuumEnvironment();

            float dt = 0.01f;
            float totalTime = 1.0f; // simulate for 1 second

            // Act
            int steps = (int)(totalTime / dt);
            for (int i = 0; i < steps; i++)
            {
                state = BallisticSolver.StepRK4(state, profile, environment, dt);
            }

            // Assert
            Assert.Equal(800f, state.Position.Z, 1);
            Assert.Equal(-4.905f, state.Position.Y, 2);
            Assert.Equal(800f, state.Velocity.Z, 1); 
        }

        [Fact]
        public void AtmosphericTrajectory_ExhibitsNonLinearDrag()
        {
            // Arrange
            float initialVelocityZ = 800f; // m/s (~Mach 2.3)
            var stateVacuum = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, initialVelocityZ),
                Time = 0f
            };
            
            var stateAtmo = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, initialVelocityZ),
                Time = 0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.01f, 
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.5f)
            };

            var envVacuum = new VacuumEnvironment();
            var envAtmo = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.81f, 0));

            float dt = 0.01f;
            float totalTime = 1.0f;

            // Act
            int steps = (int)(totalTime / dt);
            for (int i = 0; i < steps; i++)
            {
                stateVacuum = BallisticSolver.StepRK4(stateVacuum, profile, envVacuum, dt);
                stateAtmo = BallisticSolver.StepRK4(stateAtmo, profile, envAtmo, dt);
            }

            // Assert
            Assert.True(stateAtmo.Position.Z < stateVacuum.Position.Z, "Drag did not reduce total travel distance.");
            Assert.True(stateAtmo.Velocity.Z < stateVacuum.Velocity.Z, "Drag did not reduce projectile velocity.");
        }
    }
}
