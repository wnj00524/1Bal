using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Comprehensive, opaque-box multi-tier End-to-End (E2E) test suite for the TacticalSim simulation engine.
    /// Covers:
    /// - Tier 1: Core Feature Verification (F1 to F10)
    /// - Tier 2: Boundary & Extreme Corner Cases
    /// - Tier 3: Cross-Feature System Combinations
    /// - Tier 4: Real-World Combat Scenarios (Breach & Clear, Layered Barricades, Sniper Interleaving, Suppression, Caliber Curves)
    /// </summary>
    public class E2ETacticalSimulationTests
    {
        #region Helper Test Action Classes

        /// <summary>
        /// Test action representing a ballistic shot fired at a target through environmental cover.
        /// </summary>
        private class BallisticShotTacticalAction : TacticalAction
        {
            private readonly IMaterialPenetrationSystem _penetrationSystem;
            private readonly ProjectileState _initialProjectile;
            private readonly BallisticProfile _profile;
            private readonly MaterialProperties _coverMaterial;
            private readonly float _coverThickness;
            private readonly Vector3 _coverNormal;

            public PenetrationResult? Result { get; private set; }
            public bool ShotFired { get; private set; }

            public BallisticShotTacticalAction(
                Guid actorId,
                float tuCost,
                IMaterialPenetrationSystem penetrationSystem,
                ProjectileState projectile,
                BallisticProfile profile,
                MaterialProperties coverMaterial,
                float coverThickness,
                Vector3 coverNormal)
                : base(actorId, tuCost)
            {
                _penetrationSystem = penetrationSystem;
                _initialProjectile = projectile;
                _profile = profile;
                _coverMaterial = coverMaterial;
                _coverThickness = coverThickness;
                _coverNormal = coverNormal;
            }

            public override void Execute(float dt)
            {
                // Action progress handled by base class
            }

            public override void OnComplete()
            {
                base.OnComplete();
                ShotFired = true;
                Result = _penetrationSystem.CalculatePenetration(
                    _initialProjectile,
                    _profile,
                    _coverMaterial,
                    _coverThickness,
                    _coverNormal);
            }
        }

        /// <summary>
        /// Test action that throws an exception during execution to test fault isolation.
        /// </summary>
        private class FailingTacticalAction : TacticalAction
        {
            public bool FailOnStart { get; set; }
            public bool FailOnExecute { get; set; }
            public bool ExceptionThrown { get; private set; }

            public FailingTacticalAction(Guid actorId, float tuCost, bool failOnExecute = true)
                : base(actorId, tuCost)
            {
                FailOnExecute = failOnExecute;
            }

            public override void OnStart()
            {
                base.OnStart();
                if (FailOnStart)
                {
                    ExceptionThrown = true;
                    throw new InvalidOperationException("Action failed during OnStart initialization.");
                }
            }

            public override void Execute(float dt)
            {
                if (FailOnExecute)
                {
                    ExceptionThrown = true;
                    throw new InvalidOperationException("Action failed during Execute sub-step.");
                }
            }
        }

        /// <summary>
        /// Test action that logs lifecycle callback events.
        /// </summary>
        private class LifecycleTrackerTacticalAction : TacticalAction
        {
            public List<string> CallLog { get; } = new();

            public LifecycleTrackerTacticalAction(Guid actorId, float tuCost)
                : base(actorId, tuCost)
            {
            }

            public override void OnStart()
            {
                base.OnStart();
                CallLog.Add("OnStart");
            }

            public override void Execute(float dt)
            {
                CallLog.Add($"Execute({dt:F2})");
            }

            public override void OnComplete()
            {
                base.OnComplete();
                CallLog.Add("OnComplete");
            }

            public override void OnCancel()
            {
                base.OnCancel();
                CallLog.Add("OnCancel");
            }

            public override void OnFail(Exception ex)
            {
                base.OnFail(ex);
                CallLog.Add($"OnFail({ex.GetType().Name})");
            }
        }

        #endregion

        #region Helper DI Container Setup

        private static IServiceProvider BuildTestServiceProvider()
        {
            var services = new ServiceCollection();

            // Try registering via TacticalSim.Core.DependencyInjection extension methods if available
            var coreAssembly = typeof(ITurnResolver).Assembly;
            var diType = coreAssembly.GetType("TacticalSim.Core.DependencyInjection.ServiceCollectionExtensions");

            if (diType != null)
            {
                var addCoreMethod = diType.GetMethod("AddTacticalSimCore", BindingFlags.Public | BindingFlags.Static);
                if (addCoreMethod != null)
                {
                    addCoreMethod.Invoke(null, new object[] { services });
                    return services.BuildServiceProvider();
                }
            }

            // Standard fallback registration conforming to R3 interface contracts
            services.AddSingleton<IMaterialRegistry, MaterialRegistry>();
            services.AddTransient<IMaterialPenetrationSystem, MaterialPenetrationSystem>();
            services.AddSingleton<IEnvironmentModel>(sp => new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0)));
            services.AddSingleton<IDragModel>(sp => new StandardDragCurve(0.3f));
            services.AddTransient<ITurnResolver, TurnResolver>();

            return services.BuildServiceProvider();
        }

        #endregion

        #region Tier 1: Feature Coverage (F1 - F10)

        [Fact]
        public void Tier1_F1_GlobalSimulationTimeline_AdvancesMonotonically()
        {
            var resolver = new TurnResolver();
            Assert.Equal(0.0f, resolver.GlobalTime);

            float lastTime = resolver.GlobalTime;
            int timeAdvancedEventCount = 0;

            resolver.TimeAdvanced += (sender, args) =>
            {
                timeAdvancedEventCount++;
                Assert.True(args.CurrentGlobalTime > args.PreviousGlobalTime, "Global time must strictly increase.");
                Assert.Equal(args.DeltaTime, args.CurrentGlobalTime - args.PreviousGlobalTime, 5);
            };

            float[] steps = { 0.1f, 0.25f, 0.5f, 1.0f, 0.05f };
            foreach (float dt in steps)
            {
                resolver.Tick(dt);
                Assert.True(resolver.GlobalTime > lastTime, "Timeline must advance monotonically.");
                Assert.Equal(lastTime + dt, resolver.GlobalTime, 4);
                lastTime = resolver.GlobalTime;
            }

            Assert.Equal(steps.Length, timeAdvancedEventCount);

            // Test Reset
            resolver.Reset();
            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
        }

        [Fact]
        public void Tier1_F1_GlobalSimulationTimeline_RejectsInvalidDeltaTime()
        {
            var resolver = new TurnResolver();

            Assert.Throws<ArgumentException>(() => resolver.Tick(0.0f));
            Assert.Throws<ArgumentException>(() => resolver.Tick(-0.5f));
            Assert.Throws<ArgumentException>(() => resolver.Tick(float.NaN));
            Assert.Throws<ArgumentException>(() => resolver.Tick(float.PositiveInfinity));
        }

        [Fact]
        public void Tier1_F2_ConcurrentMultiEntityScheduling_ExecutesSimultaneously()
        {
            var resolver = new TurnResolver();

            var actor1 = Guid.NewGuid();
            var actor2 = Guid.NewGuid();
            var actor3 = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actor1, 2.0f);
            var action2 = new GenericTacticalAction(actor2, 4.0f);
            var action3 = new GenericTacticalAction(actor3, 6.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);
            resolver.ScheduleAction(action3);

            Assert.Equal(3, resolver.ActiveActorCount);
            Assert.True(resolver.HasActiveActions);

            // Tick 2.0 TUs
            resolver.Tick(2.0f);

            // Actor 1 should be complete and removed from active list
            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.True(action1.IsComplete);
            Assert.Equal(2.0f, action1.ExecutionProgress, 4);

            // Actor 2 should be halfway through
            Assert.Equal(TacticalActionState.Executing, action2.State);
            Assert.Equal(2.0f, action2.ExecutionProgress, 4);
            Assert.Equal(0.5f, action2.NormalizedProgress, 4);

            // Actor 3 should be 33.3% through
            Assert.Equal(TacticalActionState.Executing, action3.State);
            Assert.Equal(2.0f, action3.ExecutionProgress, 4);
            Assert.Equal(2.0f / 6.0f, action3.NormalizedProgress, 4);

            Assert.Equal(2, resolver.ActiveActorCount);
        }

        [Fact]
        public void Tier1_F3_FractionatedTUAdvancement_SubSteppingWithCarryover()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actorId, 1.5f);
            var action2 = new GenericTacticalAction(actorId, 2.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);

            Assert.Equal(1, resolver.ActiveActorCount);
            Assert.Single(resolver.GetQueuedActions(actorId));

            // Tick 2.0 TUs: Action 1 needs 1.5 TU (completes), leftover 0.5 TU carries over to Action 2
            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Equal(1.5f, action1.ExecutionProgress, 4);
            Assert.Equal(1.5f, action1.CompletionTime ?? 0f, 4);

            Assert.Equal(TacticalActionState.Executing, action2.State);
            Assert.Equal(0.5f, action2.ExecutionProgress, 4);
            Assert.Equal(1.5f, action2.StartTime, 4);

            Assert.Empty(resolver.GetQueuedActions(actorId));
            Assert.Same(action2, resolver.GetCurrentAction(actorId));
        }

        [Fact]
        public void Tier1_F4_TacticalActionLifecycleStateMachine_TransitionsCorrectly()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var tracker = new LifecycleTrackerTacticalAction(actorId, 1.0f);
            Assert.Equal(TacticalActionState.Pending, tracker.State);

            resolver.ScheduleAction(tracker);
            Assert.Equal(TacticalActionState.Pending, tracker.State);

            // Start execution
            resolver.Tick(0.4f);
            Assert.Equal(TacticalActionState.Executing, tracker.State);
            Assert.Contains("OnStart", tracker.CallLog);
            Assert.Equal(0.4f, tracker.ExecutionProgress, 4);

            // Complete execution
            resolver.Tick(0.6f);
            Assert.Equal(TacticalActionState.Completed, tracker.State);
            Assert.Contains("OnComplete", tracker.CallLog);
            Assert.True(tracker.IsComplete);
        }

        [Fact]
        public void Tier1_F4_TacticalActionLifecycle_CancellationAndFailureIsolation()
        {
            var resolver = new TurnResolver();
            var actorA = Guid.NewGuid();
            var actorB = Guid.NewGuid();

            var failingAction = new FailingTacticalAction(actorA, 1.0f, failOnExecute: true);
            var healthyAction = new GenericTacticalAction(actorB, 1.0f);

            resolver.ScheduleAction(failingAction);
            resolver.ScheduleAction(healthyAction);

            bool failEventFired = false;
            resolver.ActionFailed += (s, e) =>
            {
                failEventFired = true;
                Assert.Same(failingAction, e.Action);
                Assert.NotNull(e.Exception);
            };

            // Tick: failingAction throws inside Execute, healthyAction should complete uninterrupted
            resolver.Tick(1.0f);

            Assert.True(failEventFired);
            Assert.Equal(TacticalActionState.Failed, failingAction.State);
            Assert.NotNull(failingAction.FailureException);

            Assert.Equal(TacticalActionState.Completed, healthyAction.State);
            Assert.Equal(1.0f, healthyAction.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier1_F5_TurnResolverObservabilityEvents_EmitInStrictOrder()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var eventLog = new List<string>();

            resolver.ActionScheduled += (s, e) => eventLog.Add($"Scheduled:{e.Action.Id}");
            resolver.ActionStarted += (s, e) => eventLog.Add($"Started:{e.Action.Id}");
            resolver.ActionProgressed += (s, e) => eventLog.Add($"Progressed:{e.Action.Id}:{e.DeltaTime:F2}:{e.CurrentProgress:F2}");
            resolver.ActionCompleted += (s, e) => eventLog.Add($"Completed:{e.Action.Id}");
            resolver.TimeAdvanced += (s, e) => eventLog.Add($"TimeAdvanced:{e.CurrentGlobalTime:F2}");

            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(0.5f);
            resolver.Tick(0.5f);

            Assert.Equal($"Scheduled:{action.Id}", eventLog[0]);
            Assert.Equal($"Started:{action.Id}", eventLog[1]);
            Assert.Contains($"Progressed:{action.Id}:0.50:0.50", eventLog);
            Assert.Contains($"TimeAdvanced:0.50", eventLog);
            Assert.Contains($"Progressed:{action.Id}:0.50:1.00", eventLog);
            Assert.Contains($"Completed:{action.Id}", eventLog);
            Assert.Contains($"TimeAdvanced:1.00", eventLog);
        }

        [Fact]
        public void Tier1_F6_F7_MaterialRegistry_LookupAndPhysicalPropertiesValidation()
        {
            var registry = new MaterialRegistry();

            // Verify standard materials
            var wood = registry.GetMaterial(MaterialType.Wood);
            Assert.Equal("Wood", wood.Name);
            Assert.Equal(600.0f, wood.Density);
            Assert.Equal(1.0f, wood.ResistanceCoefficient);

            var concrete = registry.GetMaterial(MaterialType.Concrete);
            Assert.Equal("Concrete", concrete.Name);
            Assert.Equal(2400.0f, concrete.Density);
            Assert.Equal(1.8f, concrete.ResistanceCoefficient);

            var steel = registry.GetMaterial(MaterialType.Steel);
            Assert.Equal("Steel", steel.Name);
            Assert.Equal(7850.0f, steel.Density);
            Assert.Equal(2.5f, steel.ResistanceCoefficient);

            var glass = registry.GetMaterial(MaterialType.Glass);
            Assert.Equal("Glass", glass.Name);
            Assert.Equal(2500.0f, glass.Density);

            var drywall = registry.GetMaterial(MaterialType.Drywall);
            Assert.Equal("Drywall", drywall.Name);
            Assert.Equal(800.0f, drywall.Density);

            var sand = registry.GetMaterial(MaterialType.Sand);
            Assert.Equal("Sand", sand.Name);
            Assert.Equal(1600.0f, sand.Density);

            var kevlar = registry.GetMaterial(MaterialType.Kevlar);
            Assert.Equal("Kevlar", kevlar.Name);
            Assert.Equal(1440.0f, kevlar.Density);

            // Lookup by string (case-insensitive)
            Assert.True(registry.TryGetMaterial("concrete", out var concreteByName));
            Assert.Equal(MaterialType.Concrete, concreteByName.Type);

            // Dynamic custom material registration
            var customTitanium = new MaterialProperties(
                name: "TitaniumAlloy",
                type: MaterialType.Custom,
                density: 4500.0f,
                resistanceCoefficient: 2.8f,
                ricochetAngleThreshold: 1.25f,
                yieldEnergyThreshold: 450.0f);

            registry.RegisterMaterial(customTitanium);
            Assert.True(registry.TryGetMaterial("TitaniumAlloy", out var queriedTitanium));
            Assert.Equal(4500.0f, queriedTitanium.Density);
            Assert.Equal(2.8f, queriedTitanium.ResistanceCoefficient);
        }

        [Fact]
        public void Tier1_F8_TerminalBallistics_EffectiveThickness_ObliquityScaling()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var wood = registry.GetMaterial(MaterialType.Wood);

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Normal incidence: theta = 0, cos(theta) = 1.0 => T_eff = 0.1m
            var projNormal = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0f
            };
            var resultNormal = system.CalculatePenetration(projNormal, profile, wood, 0.1f, new Vector3(0, 0, -1));
            Assert.Equal(0.1f, resultNormal.EffectiveThickness, 4);
            Assert.Equal(0.0f, resultNormal.AngleOfIncidence, 4);

            // 60-degree angle of incidence: theta = pi/3, cos(theta) = 0.5 => T_eff = 0.1 / 0.5 = 0.2m
            float angle60 = MathF.PI / 3.0f;
            Vector3 vel60 = new Vector3(800.0f * MathF.Sin(angle60), 0, 800.0f * MathF.Cos(angle60));
            var projAngled = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = vel60,
                Time = 0f
            };
            var resultAngled = system.CalculatePenetration(projAngled, profile, wood, 0.1f, new Vector3(0, 0, -1));
            Assert.Equal(0.2f, resultAngled.EffectiveThickness, 3);
            Assert.Equal(angle60, resultAngled.AngleOfIncidence, 3);

            // Higher effective thickness must cause greater energy transfer
            Assert.True(resultAngled.TransferredKineticEnergy > resultNormal.TransferredKineticEnergy);
            Assert.True(resultAngled.ExitVelocity < resultNormal.ExitVelocity);
        }

        [Fact]
        public void Tier1_F8_TerminalBallistics_EnergyConservationAndKinematics()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var concrete = registry.GetMaterial(MaterialType.Concrete);

            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 6.36e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var projectile = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 750.0f),
                Time = 0f
            };

            var result = system.CalculatePenetration(projectile, profile, concrete, 0.05f, new Vector3(0, 0, -1));

            float expectedEk0 = 0.5f * profile.Mass * 750.0f * 750.0f;
            Assert.Equal(expectedEk0, result.InitialKineticEnergy, 3);

            // Strict energy conservation: Initial == Remaining + Transferred
            Assert.Equal(result.InitialKineticEnergy, result.RemainingKineticEnergy + result.TransferredKineticEnergy, 3);

            if (result.Outcome == PenetrationOutcome.Perforated)
            {
                float expectedExitVel = MathF.Sqrt(2.0f * result.RemainingKineticEnergy / profile.Mass);
                Assert.Equal(expectedExitVel, result.ExitVelocity, 3);
                Assert.True(result.ExitVelocity > 0f);
            }
        }

        [Fact]
        public void Tier1_F9_PenetrationOutcomeClassification_PerforatedStoppedRicochet()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var drywall = registry.GetMaterial(MaterialType.Drywall);
            var steel = registry.GetMaterial(MaterialType.Steel);

            var rifleProfile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // 1. Perforated: High speed bullet through thin drywall
            var rifleRound = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900.0f),
                Time = 0f
            };
            var resultPerforated = system.CalculatePenetration(rifleRound, rifleProfile, drywall, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resultPerforated.Outcome);
            Assert.True(resultPerforated.ExitVelocity > 0f);
            Assert.True(resultPerforated.RemainingKineticEnergy > 0f);

            // 2. Stopped: Slow pistol bullet hitting thick steel
            var pistolProfile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 6.36e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };
            var pistolRound = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 300.0f),
                Time = 0f
            };
            var resultStopped = system.CalculatePenetration(pistolRound, pistolProfile, steel, 0.05f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, resultStopped.Outcome);
            Assert.Equal(0.0f, resultStopped.ExitVelocity);
            Assert.Equal(0.0f, resultStopped.RemainingKineticEnergy);
            Assert.Equal(resultStopped.InitialKineticEnergy, resultStopped.TransferredKineticEnergy, 3);

            // 3. Ricochet: Grazing angle on Steel (theta = 80 deg > 70 deg threshold)
            float ricochetAngle = 80.0f * MathF.PI / 180.0f; // 1.396 rad > 1.22 rad
            var grazingRound = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(900.0f * MathF.Sin(ricochetAngle), 0, 900.0f * MathF.Cos(ricochetAngle)),
                Time = 0f
            };
            var resultRicochet = system.CalculatePenetration(grazingRound, rifleProfile, steel, 0.05f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Ricochet, resultRicochet.Outcome);
            Assert.True(resultRicochet.ExitVelocity > 0f);
            // Deflected velocity vector must point away from the barrier normal
            Assert.True(resultRicochet.ExitVelocityVector.Z < 0f || resultRicochet.ExitVelocityVector.Length() > 0f);
        }

        [Fact]
        public void Tier1_F10_DependencyInjection_ServiceRegistration()
        {
            var provider = BuildTestServiceProvider();

            var turnResolver = provider.GetService<ITurnResolver>();
            var materialRegistry = provider.GetService<IMaterialRegistry>();
            var penetrationSystem = provider.GetService<IMaterialPenetrationSystem>();
            var envModel = provider.GetService<IEnvironmentModel>();
            var dragModel = provider.GetService<IDragModel>();

            Assert.NotNull(turnResolver);
            Assert.NotNull(materialRegistry);
            Assert.NotNull(penetrationSystem);
            Assert.NotNull(envModel);
            Assert.NotNull(dragModel);
        }

        #endregion

        #region Tier 2: Boundary & Corner Cases

        [Fact]
        public void Tier2_ZeroThicknessMaterial_PerforatesWithZeroEnergyLoss()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var steel = registry.GetMaterial(MaterialType.Steel);

            var profile = new BallisticProfile
            {
                Mass = 0.005f,
                CrossSectionalArea = 3.0e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var projectile = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0f
            };

            // Nominal thickness = 0.0f
            var result = system.CalculatePenetration(projectile, profile, steel, 0.0f, new Vector3(0, 0, -1));

            Assert.Equal(0.0f, result.EffectiveThickness);
            Assert.Equal(0.5f * profile.Mass * 800.0f * 800.0f, result.InitialKineticEnergy, 3);
        }

        [Fact]
        public void Tier2_UltraThickBarricade_StopsHighEnergyRound()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var concrete = registry.GetMaterial(MaterialType.Concrete);

            // .50 BMG bullet against 10-meter thick bunker wall
            var bmgProfile = new BallisticProfile
            {
                Mass = 0.045f,
                CrossSectionalArea = 1.27e-4f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var bmgRound = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 920.0f),
                Time = 0f
            };

            var result = system.CalculatePenetration(bmgRound, bmgProfile, concrete, 10.0f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0.0f, result.ExitVelocity);
            Assert.Equal(0.0f, result.RemainingKineticEnergy);
            Assert.Equal(result.InitialKineticEnergy, result.TransferredKineticEnergy, 3);
            Assert.Equal(Vector3.Zero, result.ExitVelocityVector);
        }

        [Fact]
        public void Tier2_ExtremeAngleOfIncidence_NormalAndGrazing()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var wood = registry.GetMaterial(MaterialType.Wood);

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Grazing angle theta = 89.9 degrees (1.569 rad)
            float nearGrazingAngle = 89.9f * MathF.PI / 180.0f;
            var grazingProj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(800.0f * MathF.Sin(nearGrazingAngle), 0, 800.0f * MathF.Cos(nearGrazingAngle)),
                Time = 0f
            };

            var result = system.CalculatePenetration(grazingProj, profile, wood, 0.05f, new Vector3(0, 0, -1));

            Assert.True(result.EffectiveThickness > 0f);
            Assert.False(float.IsNaN(result.EffectiveThickness));
            Assert.False(float.IsInfinity(result.EffectiveThickness));
            Assert.True(result.RemainingKineticEnergy >= 0f);
        }

        [Fact]
        public void Tier2_SubTickMicroSteps_AccumulatesAccurately()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            // Execute 10,000 micro-steps of 0.0001f TU (total = 1.0 TU)
            float microDt = 0.0001f;
            for (int i = 0; i < 10000; i++)
            {
                resolver.Tick(microDt);
            }

            Assert.Equal(1.0f, resolver.GlobalTime, 3);
            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(1.0f, action.ExecutionProgress, 4);
            Assert.True(action.IsComplete);
        }

        [Fact]
        public void Tier2_ExactCostMatch_CompletesWithoutOverOrUnderflow()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action = new GenericTacticalAction(actorId, 0.75f);
            resolver.ScheduleAction(action);

            // Single exact tick of 0.75f
            resolver.Tick(0.75f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(0.75f, action.ExecutionProgress, 5);
            Assert.Equal(0.75f, action.CompletionTime ?? 0f, 5);
            Assert.Equal(0.75f, resolver.GlobalTime, 5);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_ActionCancellation_MidExecution_PromotesQueuedAction()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actorId, 2.0f);
            var action2 = new GenericTacticalAction(actorId, 1.5f);
            var action3 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);
            resolver.ScheduleAction(action3);

            // Run action1 for 0.5 TU
            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Executing, action1.State);
            Assert.Equal(0.5f, action1.ExecutionProgress, 4);

            // Cancel action1 mid-execution
            bool cancelled = resolver.CancelAction(action1.Id);
            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, action1.State);

            // Action 2 should now be promoted to active
            Assert.Same(action2, resolver.GetCurrentAction(actorId));
            Assert.Single(resolver.GetQueuedActions(actorId));

            // Tick 1.5 TUs -> Action 2 completes
            resolver.Tick(1.5f);
            Assert.Equal(TacticalActionState.Completed, action2.State);

            // Action 3 becomes active
            Assert.Same(action3, resolver.GetCurrentAction(actorId));
        }

        [Fact]
        public void Tier2_ActorActionCancellation_ClearsActiveAndQueuedActions()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 1.0f);
            var a2 = new GenericTacticalAction(actorId, 2.0f);
            var a3 = new GenericTacticalAction(actorId, 3.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            int cancelledCount = resolver.CancelActorActions(actorId);

            Assert.Equal(3, cancelledCount);
            Assert.Equal(TacticalActionState.Cancelled, a1.State);
            Assert.Equal(TacticalActionState.Cancelled, a2.State);
            Assert.Equal(TacticalActionState.Cancelled, a3.State);
            Assert.Null(resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_LowEnergyVsHeavyArmor_YieldEnergyThresholdStopping()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var steel = registry.GetMaterial(MaterialType.Steel); // Yield energy threshold = 500 J

            // Very low energy round (Ek = 0.5 * 0.002 * 200^2 = 40 J < 500 J)
            var lightProfile = new BallisticProfile
            {
                Mass = 0.002f,
                CrossSectionalArea = 1.0e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var lowEnergyRound = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 200.0f),
                Time = 0f
            };

            // Even through thin 1mm steel, sub-yield energy cannot perforate
            var result = system.CalculatePenetration(lowEnergyRound, lightProfile, steel, 0.001f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0.0f, result.ExitVelocity);
            Assert.Equal(0.0f, result.RemainingKineticEnergy);
        }

        #endregion

        #region Tier 3: Cross-Feature Combinations

        [Fact]
        public void Tier3_TurnResolver_Drives_ConcurrentBallisticActionsThroughMaterials()
        {
            var sp = BuildTestServiceProvider();

            var resolver = sp.GetRequiredService<ITurnResolver>();
            var registry = sp.GetRequiredService<IMaterialRegistry>();
            var penetrationSystem = sp.GetRequiredService<IMaterialPenetrationSystem>();

            var shooter1 = Guid.NewGuid();
            var shooter2 = Guid.NewGuid();

            var wood = registry.GetMaterial(MaterialType.Wood);
            var concrete = registry.GetMaterial(MaterialType.Concrete);

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var bullet1 = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 850.0f), Time = 0f };
            var bullet2 = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 850.0f), Time = 0f };

            // Shooter 1 fires through 5cm Wood, Shooter 2 fires through 25cm Concrete
            var shot1 = new BallisticShotTacticalAction(shooter1, 1.0f, penetrationSystem, bullet1, profile, wood, 0.05f, new Vector3(0, 0, -1));
            var shot2 = new BallisticShotTacticalAction(shooter2, 1.0f, penetrationSystem, bullet2, profile, concrete, 0.25f, new Vector3(0, 0, -1));

            resolver.ScheduleAction(shot1);
            resolver.ScheduleAction(shot2);

            // Tick 1.0 TU
            resolver.Tick(1.0f);

            Assert.True(shot1.ShotFired);
            Assert.True(shot2.ShotFired);

            Assert.NotNull(shot1.Result);
            Assert.NotNull(shot2.Result);

            // Shooter 1 perforated wood
            Assert.Equal(PenetrationOutcome.Perforated, shot1.Result.Value.Outcome);
            Assert.True(shot1.Result.Value.ExitVelocity > 0f);

            // Shooter 2 was stopped by thick concrete
            Assert.Equal(PenetrationOutcome.Stopped, shot2.Result.Value.Outcome);
            Assert.Equal(0.0f, shot2.Result.Value.ExitVelocity);
        }

        [Fact]
        public void Tier3_CombatSequence_ActorSuppressionAndActionInterruption()
        {
            var resolver = new TurnResolver();

            var operatorId = Guid.NewGuid();
            var enemyId = Guid.NewGuid();

            // Operator planned 3 sequential actions: Move (2.0 TU) -> Aim (1.0 TU) -> Shoot (0.5 TU)
            var opMove = new MoveTacticalAction(operatorId, Vector3.Zero, new Vector3(10, 0, 0), 2.0f);
            var opAim = new AimTacticalAction(operatorId, enemyId, 1.0f);
            var opShoot = new GenericTacticalAction(operatorId, 0.5f);

            resolver.ScheduleAction(opMove);
            resolver.ScheduleAction(opAim);
            resolver.ScheduleAction(opShoot);

            // Enemy shoots at Operator at T = 1.0 TU
            var enemyShoot = new GenericTacticalAction(enemyId, 1.0f);
            resolver.ScheduleAction(enemyShoot);

            // Tick 1.0 TU
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Executing, opMove.State);
            Assert.Equal(0.5f, opMove.NormalizedProgress, 3);
            Assert.Equal(new Vector3(5, 0, 0), opMove.CurrentPosition);
            Assert.Equal(TacticalActionState.Completed, enemyShoot.State);

            // Enemy hit interrupts Operator: cancel all Operator actions
            int cancelled = resolver.CancelActorActions(operatorId);
            Assert.Equal(3, cancelled); // 1 active (opMove) + 2 queued (opAim, opShoot)

            Assert.Equal(TacticalActionState.Cancelled, opMove.State);
            Assert.Equal(TacticalActionState.Cancelled, opAim.State);
            Assert.Equal(TacticalActionState.Cancelled, opShoot.State);

            // Operator enqueues recovery action
            var opRecover = new WaitTacticalAction(operatorId, 1.5f);
            resolver.ScheduleAction(opRecover);

            resolver.Tick(1.5f);
            Assert.Equal(TacticalActionState.Completed, opRecover.State);
            Assert.Equal(2.5f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void Tier3_DependencyInjection_FullPipelineSimulation()
        {
            var sp = BuildTestServiceProvider();

            var resolver = sp.GetRequiredService<ITurnResolver>();
            var registry = sp.GetRequiredService<IMaterialRegistry>();
            var penetration = sp.GetRequiredService<IMaterialPenetrationSystem>();
            var drag = sp.GetRequiredService<IDragModel>();

            var actor = Guid.NewGuid();
            var target = Guid.NewGuid();

            var move = new MoveTacticalAction(actor, Vector3.Zero, new Vector3(0, 0, 5), 1.0f);
            var aim = new AimTacticalAction(actor, target, 0.5f);

            var profile = new BallisticProfile { Mass = 0.004f, CrossSectionalArea = 2.43e-5f, DragModel = drag };
            var shot = new BallisticShotTacticalAction(actor, 0.5f, penetration,
                new ProjectileState { Position = new Vector3(0, 0, 5), Velocity = new Vector3(0, 0, 900.0f), Time = 0f },
                profile, registry.GetMaterial(MaterialType.Glass), 0.01f, new Vector3(0, 0, -1));

            resolver.ScheduleAction(move);
            resolver.ScheduleAction(aim);
            resolver.ScheduleAction(shot);

            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, move.State);
            Assert.Equal(TacticalActionState.Completed, aim.State);
            Assert.Equal(TacticalActionState.Completed, shot.State);
            Assert.True(shot.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, shot.Result!.Value.Outcome);
        }

        #endregion

        #region Tier 4: Real-World Application Scenarios

        /// <summary>
        /// Scenario 1: Multi-Actor Breach & Clear Firefight
        /// 2 Operators breach an entry point, engaging 2 defenders behind Drywall and Sandbag cover.
        /// </summary>
        [Fact]
        public void Tier4_Scenario1_MultiActorBreachAndClearFirefight()
        {
            var resolver = new TurnResolver();
            var registry = new MaterialRegistry();
            var penetrationSystem = new MaterialPenetrationSystem();

            var opAlpha = Guid.NewGuid();
            var opBravo = Guid.NewGuid();
            var def1 = Guid.NewGuid();
            var def2 = Guid.NewGuid();

            var drywall = registry.GetMaterial(MaterialType.Drywall);
            var sand = registry.GetMaterial(MaterialType.Sand);

            var rifleProfile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var pistolProfile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 6.36e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Operator Alpha: Move (1.0 TU) -> Aim (0.5 TU) -> Shoot through Drywall (0.5 TU)
            var alphaMove = new MoveTacticalAction(opAlpha, new Vector3(-5, 0, 0), new Vector3(0, 0, 0), 1.0f);
            var alphaAim = new AimTacticalAction(opAlpha, def1, 0.5f);
            var alphaShot = new BallisticShotTacticalAction(opAlpha, 0.5f, penetrationSystem,
                new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 940.0f), Time = 0f },
                rifleProfile, drywall, 0.05f, new Vector3(0, 0, -1));

            // Operator Bravo: Move (0.8 TU) -> Aim (0.7 TU) -> Shoot through Drywall (0.5 TU)
            var bravoMove = new MoveTacticalAction(opBravo, new Vector3(-5, 2, 0), new Vector3(0, 2, 0), 0.8f);
            var bravoAim = new AimTacticalAction(opBravo, def2, 0.7f);
            var bravoShot = new BallisticShotTacticalAction(opBravo, 0.5f, penetrationSystem,
                new ProjectileState { Position = new Vector3(0, 2, 0), Velocity = new Vector3(0, 0, 940.0f), Time = 0f },
                rifleProfile, drywall, 0.05f, new Vector3(0, 0, -1));

            // Defender 1: Wait (1.2 TU) -> Aim (0.6 TU) -> Return fire (0.4 TU)
            var def1Wait = new WaitTacticalAction(def1, 1.2f);
            var def1Aim = new AimTacticalAction(def1, opAlpha, 0.6f);
            var def1Shot = new BallisticShotTacticalAction(def1, 0.4f, penetrationSystem,
                new ProjectileState { Position = new Vector3(0, 0, 10), Velocity = new Vector3(0, 0, -380.0f), Time = 0f },
                pistolProfile, sand, 0.15f, new Vector3(0, 0, 1));

            resolver.ScheduleAction(alphaMove);
            resolver.ScheduleAction(alphaAim);
            resolver.ScheduleAction(alphaShot);

            resolver.ScheduleAction(bravoMove);
            resolver.ScheduleAction(bravoAim);
            resolver.ScheduleAction(bravoShot);

            resolver.ScheduleAction(def1Wait);
            resolver.ScheduleAction(def1Aim);
            resolver.ScheduleAction(def1Shot);

            // Execute in discrete fractionated ticks of dt = 0.25 TU
            float dt = 0.25f;
            for (int step = 0; step < 10; step++)
            {
                resolver.Tick(dt);
            }

            Assert.Equal(2.5f, resolver.GlobalTime, 4);

            // Operators' shots completed
            Assert.True(alphaShot.ShotFired);
            Assert.True(bravoShot.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, alphaShot.Result!.Value.Outcome);
            Assert.Equal(PenetrationOutcome.Perforated, bravoShot.Result!.Value.Outcome);

            // High residual velocity through drywall
            Assert.True(alphaShot.Result.Value.ExitVelocity > 800.0f);
            Assert.True(bravoShot.Result.Value.ExitVelocity > 800.0f);

            // Defender return fire was stopped by sandbags
            Assert.True(def1Shot.ShotFired);
            Assert.Equal(PenetrationOutcome.Stopped, def1Shot.Result!.Value.Outcome);
            Assert.Equal(0.0f, def1Shot.Result.Value.ExitVelocity);
        }

        /// <summary>
        /// Scenario 2: Heavy Weapon Penetration Through Layered Barricade
        /// High-caliber .50 BMG rounds fired sequentially through Wood (0.02m) -> Concrete (0.04m) -> Steel (0.005m).
        /// </summary>
        [Fact]
        public void Tier4_Scenario2_HeavyWeaponPenetrationThroughLayeredBarricade()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var wood = registry.GetMaterial(MaterialType.Wood);
            var concrete = registry.GetMaterial(MaterialType.Concrete);
            var steel = registry.GetMaterial(MaterialType.Steel);

            var bmgProfile = new BallisticProfile
            {
                Mass = 0.045f,
                CrossSectionalArea = 1.27e-4f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float v0 = 920.0f;
            float ek0 = 0.5f * bmgProfile.Mass * v0 * v0;

            var initialProjectile = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, v0),
                Time = 0f
            };

            // Layer 1: Wood facade (0.02m)
            var layer1Result = system.CalculatePenetration(initialProjectile, bmgProfile, wood, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, layer1Result.Outcome);
            Assert.True(layer1Result.ExitVelocity < v0);
            Assert.True(layer1Result.ExitVelocity > 880.0f);

            // Layer 2: Concrete core (0.04m) using Layer 1 exit state
            var layer2Result = system.CalculatePenetration(layer1Result.ExitState, bmgProfile, concrete, 0.04f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, layer2Result.Outcome);
            Assert.True(layer2Result.ExitVelocity < layer1Result.ExitVelocity);

            // Layer 3: Steel backplate (0.005m = 5mm) using Layer 2 exit state
            var layer3Result = system.CalculatePenetration(layer2Result.ExitState, bmgProfile, steel, 0.005f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, layer3Result.Outcome);
            Assert.True(layer3Result.ExitVelocity < layer2Result.ExitVelocity);

            // Kinematic & Energy Degradation Assertions
            float v1 = layer1Result.ExitVelocity;
            float v2 = layer2Result.ExitVelocity;
            float v3 = layer3Result.ExitVelocity;

            Assert.True(v0 > v1, "Velocity must decrease after Wood.");
            Assert.True(v1 > v2, "Velocity must decrease after Concrete.");
            Assert.True(v2 > v3, "Velocity must decrease after Steel.");

            float totalEnergyLoss = layer1Result.TransferredKineticEnergy + layer2Result.TransferredKineticEnergy + layer3Result.TransferredKineticEnergy;
            float finalRemainingEnergy = layer3Result.RemainingKineticEnergy;

            Assert.Equal(ek0, totalEnergyLoss + finalRemainingEnergy, 2);

            // Concrete should absorb more energy per meter than Wood
            float woodLossPerMeter = layer1Result.TransferredKineticEnergy / 0.02f;
            float concreteLossPerMeter = layer2Result.TransferredKineticEnergy / 0.04f;
            Assert.True(concreteLossPerMeter > woodLossPerMeter, "Concrete drag per unit thickness must exceed Wood.");
        }

        /// <summary>
        /// Scenario 3: Concurrent Snipers Shooting Through Glass & Wall with Fractionated Reaction Interleaving
        /// Fast Sniper Alpha fires through Glass at T = 1.5 TU, eliminating Slower Sniper Bravo before Bravo fires.
        /// </summary>
        [Fact]
        public void Tier4_Scenario3_ConcurrentSnipersShootingThroughGlassAndWall_WithFractionatedInterleaving()
        {
            var resolver = new TurnResolver();
            var registry = new MaterialRegistry();
            var penetrationSystem = new MaterialPenetrationSystem();

            var alphaId = Guid.NewGuid();
            var bravoId = Guid.NewGuid();

            var glass = registry.GetMaterial(MaterialType.Glass);
            var concrete = registry.GetMaterial(MaterialType.Concrete);

            var sniperProfile = new BallisticProfile
            {
                Mass = 0.0095f, // 7.62x51mm
                CrossSectionalArea = 4.56e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Alpha (Fast): Aim (1.2 TU) -> Shoot through Glass (0.3 TU). Total = 1.5 TU
            var alphaAim = new AimTacticalAction(alphaId, bravoId, 1.2f);
            var alphaShot = new BallisticShotTacticalAction(alphaId, 0.3f, penetrationSystem,
                new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 840.0f), Time = 0f },
                sniperProfile, glass, 0.01f, new Vector3(0, 0, -1));

            // Bravo (Slow): Aim (1.8 TU) -> Shoot through Concrete (0.4 TU). Total = 2.2 TU
            var bravoAim = new AimTacticalAction(bravoId, alphaId, 1.8f);
            var bravoShot = new BallisticShotTacticalAction(bravoId, 0.4f, penetrationSystem,
                new ProjectileState { Position = new Vector3(0, 0, 500), Velocity = new Vector3(0, 0, -840.0f), Time = 0f },
                sniperProfile, concrete, 0.30f, new Vector3(0, 0, 1));

            resolver.ScheduleAction(alphaAim);
            resolver.ScheduleAction(alphaShot);

            resolver.ScheduleAction(bravoAim);
            resolver.ScheduleAction(bravoShot);

            // Simulation loop in dt = 0.1 TU steps
            float dt = 0.1f;
            bool hitResolved = false;

            while (resolver.GlobalTime < 2.5f)
            {
                resolver.Tick(dt);

                // At T = 1.5 TU, Alpha completes shot
                if (!hitResolved && alphaShot.State == TacticalActionState.Completed)
                {
                    hitResolved = true;
                    Assert.True(alphaShot.ShotFired);
                    Assert.Equal(PenetrationOutcome.Perforated, alphaShot.Result!.Value.Outcome);

                    // Alpha hit eliminates Bravo: cancel all of Bravo's actions
                    resolver.CancelActorActions(bravoId);
                }
            }

            Assert.True(hitResolved);
            Assert.Equal(TacticalActionState.Completed, alphaShot.State);
            Assert.Equal(TacticalActionState.Cancelled, bravoAim.State);
            Assert.Equal(TacticalActionState.Cancelled, bravoShot.State);
            Assert.False(bravoShot.ShotFired, "Bravo must not have fired due to pre-emptive elimination.");
        }

        /// <summary>
        /// Scenario 4: Suppressive Fire Sequence with Action Interruption & Cancellation
        /// Machine gunner delivers continuous bursts; Flanker is pinned and switches to Take Cover.
        /// </summary>
        [Fact]
        public void Tier4_Scenario4_SuppressiveFireSequence_WithActionInterruptionAndCancellation()
        {
            var resolver = new TurnResolver();

            var gunnerId = Guid.NewGuid();
            var flankerId = Guid.NewGuid();

            // Gunner queues 4 consecutive bursts of 0.6 TU each (total 2.4 TU)
            var burst1 = new GenericTacticalAction(gunnerId, 0.6f);
            var burst2 = new GenericTacticalAction(gunnerId, 0.6f);
            var burst3 = new GenericTacticalAction(gunnerId, 0.6f);
            var burst4 = new GenericTacticalAction(gunnerId, 0.6f);

            resolver.ScheduleAction(burst1);
            resolver.ScheduleAction(burst2);
            resolver.ScheduleAction(burst3);
            resolver.ScheduleAction(burst4);

            // Flanker attempts a 2.4 TU move across the street
            var flankerMove = new MoveTacticalAction(flankerId, Vector3.Zero, new Vector3(20, 0, 0), 2.4f);
            resolver.ScheduleAction(flankerMove);

            // Tick to T = 1.2 TU (after Burst 2 finishes)
            resolver.Tick(0.6f);
            resolver.Tick(0.6f);

            Assert.Equal(1.2f, resolver.GlobalTime, 4);
            Assert.Equal(TacticalActionState.Completed, burst1.State);
            Assert.Equal(TacticalActionState.Completed, burst2.State);
            Assert.Equal(TacticalActionState.Executing, flankerMove.State);
            Assert.Equal(0.5f, flankerMove.NormalizedProgress, 3);

            // Suppressive fire pins Flanker: cancel movement and take cover
            resolver.CancelActorActions(flankerId);
            Assert.Equal(TacticalActionState.Cancelled, flankerMove.State);

            var takeCover = new WaitTacticalAction(flankerId, 1.2f);
            resolver.ScheduleAction(takeCover);

            // Finish simulation ticks
            resolver.Tick(0.6f);
            resolver.Tick(0.6f);

            Assert.Equal(2.4f, resolver.GlobalTime, 4);
            Assert.Equal(TacticalActionState.Completed, burst3.State);
            Assert.Equal(TacticalActionState.Completed, burst4.State);
            Assert.Equal(TacticalActionState.Completed, takeCover.State);
            Assert.False(resolver.HasActiveActions);
        }

        /// <summary>
        /// Scenario 5: Calibrated Velocity Loss & Kinetic Energy Decay Curve Across Variable Calibers
        /// Comprehensive matrix of 9mm vs 5.56mm vs .50 BMG across Wood, Concrete, and Steel.
        /// </summary>
        [Fact]
        public void Tier4_Scenario5_CalibratedVelocityLossAndKineticEnergyDecayCurveAcrossVariableCalibers()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var wood = registry.GetMaterial(MaterialType.Wood);
            var concrete = registry.GetMaterial(MaterialType.Concrete);
            var steel = registry.GetMaterial(MaterialType.Steel);

            // Caliber 1: 9x19mm Pistol
            var p9mm = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 6.36e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };
            var round9mm = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 380.0f), Time = 0f };

            // Caliber 2: 5.56x45mm NATO Intermediate Rifle
            var p556 = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };
            var round556 = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 940.0f), Time = 0f };

            // Caliber 3: .50 BMG Heavy Anti-Materiel
            var p50bmg = new BallisticProfile
            {
                Mass = 0.045f,
                CrossSectionalArea = 1.27e-4f,
                DragModel = new StandardDragCurve(0.3f)
            };
            var round50bmg = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 900.0f), Time = 0f };

            // Test 1: Wood (0.05m)
            var res9mm_Wood = system.CalculatePenetration(round9mm, p9mm, wood, 0.05f, new Vector3(0, 0, -1));
            var res556_Wood = system.CalculatePenetration(round556, p556, wood, 0.05f, new Vector3(0, 0, -1));
            var res50_Wood = system.CalculatePenetration(round50bmg, p50bmg, wood, 0.05f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Perforated, res9mm_Wood.Outcome);
            Assert.Equal(PenetrationOutcome.Perforated, res556_Wood.Outcome);
            Assert.Equal(PenetrationOutcome.Perforated, res50_Wood.Outcome);

            // Higher velocity and smaller cross section of 5.56mm retains higher velocity percentage than 9mm
            float retRatio9mm = res9mm_Wood.ExitVelocity / 380.0f;
            float retRatio556 = res556_Wood.ExitVelocity / 940.0f;
            Assert.True(retRatio556 > retRatio9mm, "5.56mm should retain higher velocity ratio through wood than 9mm.");

            // Test 2: Concrete (0.05m = 5cm)
            var res9mm_Conc = system.CalculatePenetration(round9mm, p9mm, concrete, 0.05f, new Vector3(0, 0, -1));
            var res556_Conc = system.CalculatePenetration(round556, p556, concrete, 0.05f, new Vector3(0, 0, -1));
            var res50_Conc = system.CalculatePenetration(round50bmg, p50bmg, concrete, 0.05f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, res9mm_Conc.Outcome);
            Assert.Equal(PenetrationOutcome.Stopped, res556_Conc.Outcome);
            Assert.Equal(PenetrationOutcome.Perforated, res50_Conc.Outcome); // .50 BMG perforates 5cm concrete

            // Test 3: Steel (0.02m = 20mm armor)
            var res9mm_Steel = system.CalculatePenetration(round9mm, p9mm, steel, 0.02f, new Vector3(0, 0, -1));
            var res556_Steel = system.CalculatePenetration(round556, p556, steel, 0.02f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, res9mm_Steel.Outcome);
            Assert.Equal(PenetrationOutcome.Stopped, res556_Steel.Outcome);

            // Energy Conservation across all 8 matrix tests
            var allResults = new[] { res9mm_Wood, res556_Wood, res50_Wood, res9mm_Conc, res556_Conc, res50_Conc, res9mm_Steel, res556_Steel };
            foreach (var res in allResults)
            {
                Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy + res.TransferredKineticEnergy, 3);
            }
        }

        #endregion
    }
}
