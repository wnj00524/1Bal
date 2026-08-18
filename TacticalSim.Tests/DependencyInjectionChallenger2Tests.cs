using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Adversarial stress tests and empirical verification for Milestone 3 (Dependency Injection & Multi-Service Concert).
    /// Authored by Challenger 2.
    /// </summary>
    public class DependencyInjectionChallenger2Tests
    {
        #region Multi-Threaded DI Concurrency & Thread-Safety

        [Fact]
        public void DI_MultiThreadedConcurrentResolution_ThreadSafetyAndLifetimeIntegrity()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            const int threadCount = 64;
            var resolvedRegistries = new ConcurrentBag<IMaterialRegistry>();
            var resolvedPenetrations = new ConcurrentBag<IMaterialPenetrationSystem>();
            var resolvedResolvers = new ConcurrentBag<ITurnResolver>();
            var resolvedDrags = new ConcurrentBag<IDragModel>();
            var resolvedEnvironments = new ConcurrentBag<IEnvironmentModel>();

            Parallel.For(0, threadCount, _ =>
            {
                var reg = provider.GetRequiredService<IMaterialRegistry>();
                var pen = provider.GetRequiredService<IMaterialPenetrationSystem>();
                var turn = provider.GetRequiredService<ITurnResolver>();
                var drag = provider.GetRequiredService<IDragModel>();
                var env = provider.GetRequiredService<IEnvironmentModel>();

                resolvedRegistries.Add(reg);
                resolvedPenetrations.Add(pen);
                resolvedResolvers.Add(turn);
                resolvedDrags.Add(drag);
                resolvedEnvironments.Add(env);
            });

            // Singletons: all instances must be strictly identical reference
            var firstRegistry = resolvedRegistries.First();
            Assert.All(resolvedRegistries, r => Assert.Same(firstRegistry, r));

            var firstDrag = resolvedDrags.First();
            Assert.All(resolvedDrags, d => Assert.Same(firstDrag, d));

            var firstEnv = resolvedEnvironments.First();
            Assert.All(resolvedEnvironments, e => Assert.Same(firstEnv, e));

            // Transients: all instances must be distinct references across all threads
            Assert.Equal(threadCount, resolvedPenetrations.Distinct().Count());
            Assert.Equal(threadCount, resolvedResolvers.Distinct().Count());
        }

        [Fact]
        public void DI_CustomOverrides_RespectsSpecializedImplementations()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();

            // Override DragModel with custom high-drag profile
            var customDrag = new StandardDragCurve(0.75f);
            services.AddSingleton<IDragModel>(customDrag);

            var provider = services.BuildServiceProvider();

            var resolvedDrag = provider.GetRequiredService<IDragModel>();
            Assert.Same(customDrag, resolvedDrag);
            Assert.Equal(0.75f, resolvedDrag.GetDragCoefficient(0.5f));
        }

        [Fact]
        public void DI_IndependentNestedScopes_PreservesScopeIsolationForTurnResolvers()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            using var scopeA = provider.CreateScope();
            using var scopeB = provider.CreateScope();

            var resolverA = scopeA.ServiceProvider.GetRequiredService<ITurnResolver>();
            var resolverB = scopeB.ServiceProvider.GetRequiredService<ITurnResolver>();

            Assert.NotSame(resolverA, resolverB);

            var actorA = Guid.NewGuid();
            var actorB = Guid.NewGuid();

            resolverA.ScheduleAction(new GenericTacticalAction(actorA, 5.0f));
            resolverB.ScheduleAction(new GenericTacticalAction(actorB, 10.0f));

            resolverA.Tick(2.5f);

            Assert.Equal(2.5f, resolverA.GlobalTime, 3);
            Assert.Equal(0.0f, resolverB.GlobalTime, 3);
            Assert.True(resolverA.HasActiveActions);
            Assert.True(resolverB.HasActiveActions);
            Assert.Null(resolverA.GetCurrentAction(actorB));
            Assert.Null(resolverB.GetCurrentAction(actorA));
        }

        #endregion

        #region Multi-Service Concert & Complex Ballistic Integration

        [Fact]
        public void DI_Concert_MultiActorBreachFirefight_FullSimulationIntegration()
        {
            // Arrange DI container
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var turnResolver = provider.GetRequiredService<ITurnResolver>();
            var materialRegistry = provider.GetRequiredService<IMaterialRegistry>();
            var penetrationSystem = provider.GetRequiredService<IMaterialPenetrationSystem>();
            var dragModel = provider.GetRequiredService<IDragModel>();
            var environmentModel = provider.GetRequiredService<IEnvironmentModel>();

            var shooterId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var runnerId = Guid.NewGuid();

            var shotFired = false;
            PenetrationResult? wallPenResult = null;
            PenetrationResult? steelPenResult = null;

            // Define ballistic projectile: 7.62mm (9.7g, 830 m/s)
            var bulletMass = 0.0097f;
            var bulletArea = 0.0000456f;
            var profile = new BallisticProfile
            {
                Mass = bulletMass,
                CrossSectionalArea = bulletArea,
                DragModel = dragModel
            };

            // Setup Actor 1: Aim for 0.4 TU, then Fire through Layered Cover (Drywall + Wood + Steel)
            var aimAction = new AimTacticalAction(shooterId, targetId, tuCost: 0.4f, maxAimBonus: 0.8f);
            var fireAction = new GenericTacticalAction(shooterId, tuCost: 0.1f, onComplete: () =>
            {
                shotFired = true;

                // 1. Projectile starts at shooter position (0, 1.5, 0) flying along +Z at 830 m/s
                var state = new ProjectileState
                {
                    Position = new Vector3(0, 1.5f, 0),
                    Velocity = new Vector3(0, 0, 830f),
                    Time = 0f
                };

                // 2. Trajectory step 10 meters in air using RK4 solver with DI Environment & Drag
                float flightTime = 10f / 830f;
                state = BallisticSolver.StepRK4(state, profile, environmentModel, flightTime);

                // 3. Impact Layer 1: Drywall (1.5 cm)
                var drywall = materialRegistry.GetMaterial("Drywall");
                var drywallRes = penetrationSystem.CalculatePenetration(state, profile, drywall, 0.015f, new Vector3(0, 0, -1));
                Assert.Equal(PenetrationOutcome.Perforated, drywallRes.Outcome);
                state = drywallRes.ExitState;

                // 4. Impact Layer 2: Wood Barricade (5 cm)
                var wood = materialRegistry.GetMaterial("Wood");
                var woodRes = penetrationSystem.CalculatePenetration(state, profile, wood, 0.05f, new Vector3(0, 0, -1));
                wallPenResult = woodRes;
                Assert.Equal(PenetrationOutcome.Perforated, woodRes.Outcome);
                state = woodRes.ExitState;

                // 5. Impact Layer 3: Steel Armor Plate (2.5 cm) - should stop
                var steel = materialRegistry.GetMaterial("Steel");
                steelPenResult = penetrationSystem.CalculatePenetration(state, profile, steel, 0.025f, new Vector3(0, 0, -1));
            });

            // Setup Actor 2: Runner moving 20 meters across the field
            var runAction = new MoveTacticalAction(runnerId, new Vector3(10, 0, 0), new Vector3(10, 0, 20), tuCost: 1.0f);

            turnResolver.ScheduleAction(aimAction);
            turnResolver.ScheduleAction(fireAction);
            turnResolver.ScheduleAction(runAction);

            // Act: Step 1 -> 0.4 TU (Aim completes, Runner at 40%)
            turnResolver.Tick(0.4f);
            Assert.Equal(TacticalActionState.Completed, aimAction.State);
            Assert.Equal(0.8f, aimAction.CurrentAimBonus, 4);
            Assert.False(shotFired);
            Assert.Equal(0.4f, runAction.NormalizedProgress, 4);

            // Act: Step 2 -> 0.1 TU (Fire completes and resolves terminal ballistics, Runner at 50%)
            turnResolver.Tick(0.1f);
            Assert.Equal(TacticalActionState.Completed, fireAction.State);
            Assert.True(shotFired);
            Assert.NotNull(wallPenResult);
            Assert.NotNull(steelPenResult);

            var woodPen = wallPenResult.Value;
            var steelPen = steelPenResult.Value;

            // Assert Wood penetration properties
            Assert.Equal(PenetrationOutcome.Perforated, woodPen.Outcome);
            Assert.True(woodPen.ExitVelocity < 830f);
            Assert.True(woodPen.ExitVelocity > 500f);
            Assert.True(MathF.Abs(woodPen.InitialKineticEnergy - (woodPen.RemainingKineticEnergy + woodPen.TransferredKineticEnergy)) < 0.1f);

            // Assert Steel barrier stopped the projectile
            Assert.Equal(PenetrationOutcome.Stopped, steelPen.Outcome);
            Assert.Equal(0f, steelPen.ExitVelocity);
            Assert.Equal(0f, steelPen.RemainingKineticEnergy);
            Assert.True(MathF.Abs(steelPen.InitialKineticEnergy - steelPen.TransferredKineticEnergy) < 0.1f);

            // Act: Step 3 -> 0.5 TU (Runner finishes movement)
            turnResolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, runAction.State);
            Assert.Equal(new Vector3(10, 0, 20), runAction.CurrentPosition);
            Assert.False(turnResolver.HasActiveActions);
            Assert.Equal(1.0f, turnResolver.GlobalTime, 4);
        }

        [Fact]
        public void DI_Concert_HighVolumeConcurrentShots_StrictKinematicOracles()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IMaterialRegistry>();
            var penetration = provider.GetRequiredService<IMaterialPenetrationSystem>();
            var drag = provider.GetRequiredService<IDragModel>();
            var env = provider.GetRequiredService<IEnvironmentModel>();
            var resolver = provider.GetRequiredService<ITurnResolver>();

            var materialNames = new[] { "Wood", "Concrete", "Steel", "Glass", "Drywall", "Sand", "Kevlar" };
            var rng = new Random(1337);

            int shotCount = 100;
            var actors = Enumerable.Range(0, shotCount).Select(_ => Guid.NewGuid()).ToList();
            var results = new ConcurrentBag<PenetrationResult>();

            for (int i = 0; i < shotCount; i++)
            {
                var actorId = actors[i];
                var matName = materialNames[i % materialNames.Length];
                var mass = 0.005f + (float)rng.NextDouble() * 0.040f; // 5g to 45g
                var velocity = 300f + (float)rng.NextDouble() * 800f; // 300 to 1100 m/s
                var thickness = 0.005f + (float)rng.NextDouble() * 0.20f; // 5mm to 20cm

                var action = new GenericTacticalAction(actorId, 0.05f, onComplete: () =>
                {
                    var mat = registry.GetMaterial(matName);
                    var state = new ProjectileState
                    {
                        Position = Vector3.Zero,
                        Velocity = new Vector3(0, 0, velocity),
                        Time = 0f
                    };

                    var profile = new BallisticProfile
                    {
                        Mass = mass,
                        CrossSectionalArea = 0.00005f,
                        DragModel = drag
                    };

                    // Step in environment
                    state = BallisticSolver.StepRK4(state, profile, env, 0.001f);

                    var res = penetration.CalculatePenetration(state, profile, mat, thickness, new Vector3(0, 0, -1));
                    results.Add(res);
                });

                resolver.ScheduleAction(action);
            }

            // Tick 0.05 TU to complete all 100 actions simultaneously
            resolver.Tick(0.05f);

            Assert.Equal(shotCount, results.Count);
            foreach (var res in results)
            {
                // Energy conservation oracle: Initial == Remaining + Transferred
                float energyDelta = MathF.Abs(res.InitialKineticEnergy - (res.RemainingKineticEnergy + res.TransferredKineticEnergy));
                Assert.True(energyDelta < 0.1f, $"Energy conservation violated: Initial={res.InitialKineticEnergy}, Rem={res.RemainingKineticEnergy}, Trans={res.TransferredKineticEnergy}");

                if (res.Outcome == PenetrationOutcome.Perforated)
                {
                    Assert.True(res.ExitVelocity > 0f);
                    Assert.True(res.ExitVelocity <= res.InitialVelocity);
                    Assert.True(res.RemainingKineticEnergy > 0f);
                }
                else if (res.Outcome == PenetrationOutcome.Stopped)
                {
                    Assert.Equal(0f, res.ExitVelocity);
                    Assert.Equal(0f, res.RemainingKineticEnergy);
                    Assert.True(MathF.Abs(res.InitialKineticEnergy - res.TransferredKineticEnergy) < 0.1f);
                }
            }
        }

        [Fact]
        public void DI_Concert_RicochetKinematicsAndDeflectedFlight_CollaboratesWithSolver()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IMaterialRegistry>();
            var penetration = provider.GetRequiredService<IMaterialPenetrationSystem>();
            var drag = provider.GetRequiredService<IDragModel>();
            var env = provider.GetRequiredService<IEnvironmentModel>();

            var steel = registry.GetMaterial("Steel");

            // Angle of incidence: grazing incidence: normal is (0, 1, 0), velocity is at 10 deg to plate
            var incomingVel = new Vector3(500f, -50f, 0f);
            var initialSpeed = incomingVel.Length();

            var projectile = new ProjectileState
            {
                Position = new Vector3(0, 1f, 0),
                Velocity = incomingVel,
                Time = 0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = drag
            };

            var result = penetration.CalculatePenetration(projectile, profile, steel, 0.05f, new Vector3(0, 1, 0));

            Assert.Equal(PenetrationOutcome.Ricochet, result.Outcome);
            Assert.True(result.ExitVelocity > 0f);
            Assert.True(result.ExitVelocity < initialSpeed, "Ricochet should lose kinetic energy to impact work");
            Assert.True(result.ExitVelocityVector.Y > 0f, "Reflected velocity vector should point upward (+Y)");

            // Continue ricochet flight via BallisticSolver RK4
            var deflectedState = result.ExitState;
            var nextState = BallisticSolver.StepRK4(deflectedState, profile, env, 0.02f);

            Assert.True(nextState.Position.X > deflectedState.Position.X);
            Assert.True(nextState.Position.Y > deflectedState.Position.Y);
        }

        #endregion

        #region Modular Registration Isolated Capabilities

        [Fact]
        public void DI_ModularRegistration_AddMaterialPenetration_WorksIndependently()
        {
            var services = new ServiceCollection();
            services.AddMaterialPenetration();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IMaterialRegistry>();
            var penetration = provider.GetRequiredService<IMaterialPenetrationSystem>();

            var wood = registry.GetMaterial("Wood");
            var state = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 700f),
                Time = 0f
            };
            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = null!
            };

            var result = penetration.CalculatePenetration(state, profile, wood, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, result.Outcome);
            Assert.True(result.ExitVelocity > 0f);
        }

        [Fact]
        public void DI_ModularRegistration_AddSimulationServices_WorksIndependently()
        {
            var services = new ServiceCollection();
            services.AddSimulationServices();
            var provider = services.BuildServiceProvider();

            var resolver = provider.GetRequiredService<ITurnResolver>();
            var actor = Guid.NewGuid();
            var action = new GenericTacticalAction(actor, 1.0f);

            resolver.ScheduleAction(action);
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(1.0f, resolver.GlobalTime);
        }

        #endregion
    }
}
