using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;

namespace StressHarness
{
    public class MockCustomDragModel : IDragModel
    {
        public float GetDragCoefficient(float machNumber) => 0.42f;
    }

    public class MockCustomEnvironment : IEnvironmentModel
    {
        public EnvironmentState GetConditionsAt(Vector3 position) => new EnvironmentState
        {
            AirDensity = 1.15f,
            WindVelocity = Vector3.Zero,
            SpeedOfSound = 340f,
            Gravity = new Vector3(0, -9.8f, 0)
        };
    }

    public static class Program
    {
        private static int _passCount = 0;
        private static int _failCount = 0;

        public static int Main(string[] args)
        {
            Console.WriteLine("=== TACTICALSIM M3 EXTENDED ADVERSARIAL CHALLENGE HARNESS ===");
            var stopwatch = Stopwatch.StartNew();

            RunTest("Test 1: Null ServiceCollection Arguments", TestNullArguments);
            RunTest("Test 2: Fluent Chaining Instance Preservation", TestFluentChaining);
            RunTest("Test 3: Lifetime Semantics (Singleton vs Transient)", TestLifetimeSemantics);
            RunTest("Test 4: Scoped and Nested Scope Hierarchy Semantics", TestScopedHierarchy);
            RunTest("Test 5: Modular Registration Isolation", TestModularRegistrations);
            RunTest("Test 6: Idempotent / Repeated Registrations", TestRepeatedRegistrations);
            RunTest("Test 7: High Concurrency Resolution Stress (10,000 Tasks)", TestHighConcurrencyResolution);
            RunTest("Test 8: Concurrent Execution of Independent Turn Resolvers", TestConcurrentTurnResolverExecution);
            RunTest("Test 9: Concurrent Material Registration & Penetration Calculations", TestConcurrentMaterialRegistryAndPenetration);
            RunTest("Test 10: Physics Model Defaults & Numerical Accuracy", TestPhysicsDefaultsAndAccuracy);
            RunTest("Test 11: BodyPart Hierarchy Nullability & Voxel Trauma", TestBodyPartNullabilityAndTrauma);
            RunTest("Test 12: Scope Disposal Isolation for Singletons", TestScopeDisposalIsolation);
            RunTest("Test 13: Strict ServiceProvider Validation (ValidateScopes & ValidateOnBuild)", TestStrictServiceProviderValidation);
            RunTest("Test 14: Custom Implementation Override Pre/Post Registration", TestCustomImplementationOverrides);
            RunTest("Test 15: Mass Parallel Scopes & Cross-Scope Concurrent Resolution", TestMassParallelScopes);
            RunTest("Test 16: Interleaved TurnResolver Operations Under Concurrency", TestInterleavedTurnResolverOps);

            stopwatch.Stop();
            Console.WriteLine("\n==================================================");
            Console.WriteLine($"RESULTS: {_passCount} PASSED, {_failCount} FAILED in {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine("==================================================");

            return _failCount == 0 ? 0 : 1;
        }

        private static void RunTest(string testName, Action testAction)
        {
            Console.Write($"[RUNNING] {testName} ... ");
            try
            {
                testAction();
                Console.WriteLine("PASSED");
                _passCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _failCount++;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion Failed: {message}");
            }
        }

        private static void TestNullArguments()
        {
            IServiceCollection nullServices = null!;

            bool threw1 = false;
            try { nullServices.AddTacticalSimCore(); } catch (ArgumentNullException) { threw1 = true; }
            Assert(threw1, "AddTacticalSimCore must throw ArgumentNullException when services is null");

            bool threw2 = false;
            try { nullServices.AddMaterialPenetration(); } catch (ArgumentNullException) { threw2 = true; }
            Assert(threw2, "AddMaterialPenetration must throw ArgumentNullException when services is null");

            bool threw3 = false;
            try { nullServices.AddSimulationServices(); } catch (ArgumentNullException) { threw3 = true; }
            Assert(threw3, "AddSimulationServices must throw ArgumentNullException when services is null");
        }

        private static void TestFluentChaining()
        {
            var sc = new ServiceCollection();
            var res1 = sc.AddTacticalSimCore();
            Assert(ReferenceEquals(sc, res1), "AddTacticalSimCore must return original collection");

            var sc2 = new ServiceCollection();
            var res2 = sc2.AddMaterialPenetration();
            Assert(ReferenceEquals(sc2, res2), "AddMaterialPenetration must return original collection");

            var sc3 = new ServiceCollection();
            var res3 = sc3.AddSimulationServices();
            Assert(ReferenceEquals(sc3, res3), "AddSimulationServices must return original collection");
        }

        private static void TestLifetimeSemantics()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            // Singletons
            var matReg1 = sp.GetRequiredService<IMaterialRegistry>();
            var matReg2 = sp.GetRequiredService<IMaterialRegistry>();
            Assert(ReferenceEquals(matReg1, matReg2), "IMaterialRegistry must be Singleton");

            var drag1 = sp.GetRequiredService<IDragModel>();
            var drag2 = sp.GetRequiredService<IDragModel>();
            Assert(ReferenceEquals(drag1, drag2), "IDragModel must be Singleton");

            var env1 = sp.GetRequiredService<IEnvironmentModel>();
            var env2 = sp.GetRequiredService<IEnvironmentModel>();
            Assert(ReferenceEquals(env1, env2), "IEnvironmentModel must be Singleton");

            // Transients
            var turn1 = sp.GetRequiredService<ITurnResolver>();
            var turn2 = sp.GetRequiredService<ITurnResolver>();
            Assert(!ReferenceEquals(turn1, turn2), "ITurnResolver must be Transient");

            var pen1 = sp.GetRequiredService<IMaterialPenetrationSystem>();
            var pen2 = sp.GetRequiredService<IMaterialPenetrationSystem>();
            Assert(!ReferenceEquals(pen1, pen2), "IMaterialPenetrationSystem must be Transient");
        }

        private static void TestScopedHierarchy()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var rootProvider = services.BuildServiceProvider();

            using var scopeA = rootProvider.CreateScope();
            using var scopeB = rootProvider.CreateScope();
            using var nestedScopeA1 = scopeA.ServiceProvider.CreateScope();

            var regRoot = rootProvider.GetRequiredService<IMaterialRegistry>();
            var regA = scopeA.ServiceProvider.GetRequiredService<IMaterialRegistry>();
            var regB = scopeB.ServiceProvider.GetRequiredService<IMaterialRegistry>();
            var regA1 = nestedScopeA1.ServiceProvider.GetRequiredService<IMaterialRegistry>();

            Assert(ReferenceEquals(regRoot, regA), "Singleton must be same across root and scopeA");
            Assert(ReferenceEquals(regRoot, regB), "Singleton must be same across root and scopeB");
            Assert(ReferenceEquals(regRoot, regA1), "Singleton must be same across nested scopes");

            var turnA = scopeA.ServiceProvider.GetRequiredService<ITurnResolver>();
            var turnB = scopeB.ServiceProvider.GetRequiredService<ITurnResolver>();
            var turnA1 = nestedScopeA1.ServiceProvider.GetRequiredService<ITurnResolver>();

            Assert(!ReferenceEquals(turnA, turnB), "Transients must differ across sibling scopes");
            Assert(!ReferenceEquals(turnA, turnA1), "Transients must differ between parent and child scope");
        }

        private static void TestModularRegistrations()
        {
            // Material Penetration only
            var scMat = new ServiceCollection();
            scMat.AddMaterialPenetration();
            var spMat = scMat.BuildServiceProvider();

            Assert(spMat.GetService<IMaterialRegistry>() != null, "IMaterialRegistry must be registered");
            Assert(spMat.GetService<IMaterialPenetrationSystem>() != null, "IMaterialPenetrationSystem must be registered");
            Assert(spMat.GetService<ITurnResolver>() == null, "ITurnResolver must NOT be registered in AddMaterialPenetration");
            Assert(spMat.GetService<IDragModel>() == null, "IDragModel must NOT be registered in AddMaterialPenetration");
            Assert(spMat.GetService<IEnvironmentModel>() == null, "IEnvironmentModel must NOT be registered in AddMaterialPenetration");

            // Simulation Services only
            var scSim = new ServiceCollection();
            scSim.AddSimulationServices();
            var spSim = scSim.BuildServiceProvider();

            Assert(spSim.GetService<ITurnResolver>() != null, "ITurnResolver must be registered");
            Assert(spSim.GetService<IMaterialRegistry>() == null, "IMaterialRegistry must NOT be registered in AddSimulationServices");
            Assert(spSim.GetService<IMaterialPenetrationSystem>() == null, "IMaterialPenetrationSystem must NOT be registered in AddSimulationServices");
            Assert(spSim.GetService<IDragModel>() == null, "IDragModel must NOT be registered in AddSimulationServices");
            Assert(spSim.GetService<IEnvironmentModel>() == null, "IEnvironmentModel must NOT be registered in AddSimulationServices");
        }

        private static void TestRepeatedRegistrations()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            services.AddTacticalSimCore();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            var reg = sp.GetRequiredService<IMaterialRegistry>();
            var pen = sp.GetRequiredService<IMaterialPenetrationSystem>();
            var turn = sp.GetRequiredService<ITurnResolver>();
            var drag = sp.GetRequiredService<IDragModel>();
            var env = sp.GetRequiredService<IEnvironmentModel>();

            Assert(reg != null && pen != null && turn != null && drag != null && env != null,
                "Repeated AddTacticalSimCore calls must resolve successfully without error");
        }

        private static void TestHighConcurrencyResolution()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            int threadCount = 10000;
            var exceptions = new ConcurrentBag<Exception>();
            var singletons = new ConcurrentBag<IMaterialRegistry>();
            var transients = new ConcurrentBag<ITurnResolver>();

            Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 }, i =>
            {
                try
                {
                    var reg = sp.GetRequiredService<IMaterialRegistry>();
                    var pen = sp.GetRequiredService<IMaterialPenetrationSystem>();
                    var turn = sp.GetRequiredService<ITurnResolver>();
                    var drag = sp.GetRequiredService<IDragModel>();
                    var env = sp.GetRequiredService<IEnvironmentModel>();

                    singletons.Add(reg);
                    transients.Add(turn);

                    Assert(reg != null && pen != null && turn != null && drag != null && env != null, "All services must resolve");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert(exceptions.IsEmpty, $"Concurrent resolution threw {exceptions.Count} exceptions");
            Assert(singletons.Distinct().Count() == 1, "All 10000 resolutions of IMaterialRegistry must point to the exact same singleton");
            Assert(transients.Distinct().Count() == threadCount, $"All {threadCount} transient ITurnResolver instances must be distinct");
        }

        private static void TestConcurrentTurnResolverExecution()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            int simCount = 100;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, simCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, s =>
            {
                try
                {
                    var resolver = sp.GetRequiredService<ITurnResolver>();
                    var actor1 = Guid.NewGuid();
                    var actor2 = Guid.NewGuid();
                    int action1Runs = 0;
                    int action2Runs = 0;

                    resolver.ScheduleAction(new GenericTacticalAction(actor1, 10f, dt => Interlocked.Increment(ref action1Runs)));
                    resolver.ScheduleAction(new GenericTacticalAction(actor2, 15f, dt => Interlocked.Increment(ref action2Runs)));

                    Assert(resolver.ActiveActorCount == 2, "Resolver must have 2 active actors");
                    Assert(resolver.HasActiveActions, "Resolver must have active actions");

                    resolver.Tick(5f);
                    Assert(Math.Abs(resolver.GlobalTime - 5f) < 0.001f, "Global time must be 5.0");
                    Assert(action1Runs > 0 && action2Runs > 0, "Both actions must execute");

                    resolver.Tick(5f);
                    Assert(Math.Abs(resolver.GlobalTime - 10f) < 0.001f, "Global time must be 10.0");
                    Assert(resolver.ActiveActorCount == 1, "Actor1 action should be completed");

                    resolver.Tick(5f);
                    Assert(Math.Abs(resolver.GlobalTime - 15f) < 0.001f, "Global time must be 15.0");
                    Assert(!resolver.HasActiveActions, "All actions should be complete");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert(exceptions.IsEmpty, $"Concurrent simulation executions failed with {exceptions.Count} exceptions");
        }

        private static void TestConcurrentMaterialRegistryAndPenetration()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<IMaterialRegistry>();
            var exceptions = new ConcurrentBag<Exception>();
            int iterations = 1000;

            // Register custom materials concurrently
            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                try
                {
                    var penSystem = sp.GetRequiredService<IMaterialPenetrationSystem>();
                    var dragModel = sp.GetRequiredService<IDragModel>();

                    string matName = $"CompositeArmor_{i}";
                    var customMat = new MaterialProperties
                    {
                        Name = matName,
                        Type = MaterialType.Steel,
                        Density = 3000f + (i % 1000),
                        ResistanceCoefficient = 1.2f + (i % 10) * 0.1f,
                        RicochetAngleThreshold = 1.1f,
                        YieldEnergyThreshold = 500f + (i % 100)
                    };

                    registry.RegisterMaterial(customMat);

                    // Perform penetration calculation
                    var proj = new ProjectileState
                    {
                        Position = Vector3.Zero,
                        Velocity = new Vector3(0, 0, 850f),
                        Time = 0f
                    };

                    var prof = new BallisticProfile
                    {
                        Mass = 0.009f,
                        CrossSectionalArea = 0.00005f,
                        DragModel = dragModel
                    };

                    var result = penSystem.CalculatePenetration(proj, prof, customMat, 0.05f, new Vector3(0, 0, -1));
                    Assert(result.InitialKineticEnergy > 0f, "Initial KE must be positive");
                    Assert(result.TransferredKineticEnergy >= 0f, "Transferred KE must be >= 0");
                    Assert(Math.Abs(result.InitialKineticEnergy - (result.RemainingKineticEnergy + result.TransferredKineticEnergy)) < 0.01f,
                        "Energy conservation must strictly hold");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert(exceptions.IsEmpty, $"Concurrent registry/penetration had {exceptions.Count} exceptions");
        }

        private static void TestPhysicsDefaultsAndAccuracy()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            var drag = sp.GetRequiredService<IDragModel>();
            var env = sp.GetRequiredService<IEnvironmentModel>();

            // IDragModel standard drag curve verification
            float cdSubsonic = drag.GetDragCoefficient(0.4f);
            float cdTransonic = drag.GetDragCoefficient(1.0f);
            float cdSupersonic = drag.GetDragCoefficient(2.5f);

            Assert(Math.Abs(cdSubsonic - 0.3f) < 0.001f, "Subsonic drag coefficient must default to 0.3");
            Assert(cdTransonic > cdSubsonic, "Transonic drag must peak higher than subsonic baseline");
            Assert(cdSupersonic >= cdSubsonic, "Supersonic drag must be >= base drag");

            // IEnvironmentModel verification
            var seaLevel = env.GetConditionsAt(Vector3.Zero);
            Assert(Math.Abs(seaLevel.Gravity.X) < 0.001f, "Gravity X must be 0");
            Assert(Math.Abs(seaLevel.Gravity.Y - (-9.80665f)) < 0.001f, "Gravity Y must be -9.80665");
            Assert(Math.Abs(seaLevel.Gravity.Z) < 0.001f, "Gravity Z must be 0");
            Assert(seaLevel.AirDensity > 1.2f && seaLevel.AirDensity < 1.3f, "Sea level air density must be ~1.225 kg/m^3");

            // Ballistic RK4 integration
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(100f, 0f, 0f),
                Time = 0f
            };
            var profile = new BallisticProfile
            {
                Mass = 0.01f,
                CrossSectionalArea = 0.0001f,
                DragModel = drag
            };

            var next = BallisticSolver.StepRK4(proj, profile, env, 0.1f);
            Assert(next.Position.X > 0f, "X position must advance");
            Assert(next.Position.Y < 0f, "Y position must decrease due to gravity");
            Assert(next.Velocity.X < 100f, "X velocity must slow due to drag");
            Assert(next.Velocity.Y < 0f, "Y velocity must become negative due to gravity");
        }

        private static void TestBodyPartNullabilityAndTrauma()
        {
            // Verify BodyPart root node with null Parent
            var root = new BodyPart
            {
                Type = BodyPartType.Thorax,
                Parent = null,
                TotalBloodVolumeLost = 0f,
                CurrentBleedRate = 0f
            };

            Assert(root.Parent == null, "Root BodyPart Parent must be nullable and null");

            var child = new BodyPart
            {
                Type = BodyPartType.Head,
                Parent = root
            };
            root.Children.Add(child);

            Assert(ReferenceEquals(child.Parent, root), "Child BodyPart Parent must reference root");

            var tissue = new TissueProperties
            {
                Density = 1000f,
                Elasticity = 0.5f,
                ShearStrength = 1.0f
            };
            var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.05f, tissue);
            child.Voxels.Add(voxel);

            child.ApplyTrauma(Vector3.Zero, 500f);
            Assert(voxel.DepositedEnergy > 0f, "Voxel must absorb deposited trauma energy");
        }

        private static void TestScopeDisposalIsolation()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var rootProvider = services.BuildServiceProvider();

            IMaterialRegistry singletonFromScope;
            using (var scope = rootProvider.CreateScope())
            {
                singletonFromScope = scope.ServiceProvider.GetRequiredService<IMaterialRegistry>();
                Assert(singletonFromScope != null, "Singleton resolved in scope");
            }

            // After scope disposal, singleton in root provider must remain functional and identical
            var singletonFromRoot = rootProvider.GetRequiredService<IMaterialRegistry>();
            Assert(ReferenceEquals(singletonFromScope, singletonFromRoot), "Singleton persists across disposed scopes");

            // Querying registered materials on the singleton still functions
            var wood = singletonFromRoot.GetMaterial("Wood");
            Assert(wood.Name == "Wood", "Material registry remains functional after scope disposal");
        }

        private static void TestStrictServiceProviderValidation()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();

            var options = new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            };

            var provider = services.BuildServiceProvider(options);
            Assert(provider != null, "ServiceProvider should build cleanly with ValidateScopes and ValidateOnBuild");

            using var scope = provider!.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ITurnResolver>();
            Assert(resolver != null, "Scoped resolution of TurnResolver must succeed");
        }

        private static void TestCustomImplementationOverrides()
        {
            // Override IDragModel and IEnvironmentModel after AddTacticalSimCore
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            services.AddSingleton<IDragModel, MockCustomDragModel>();
            services.AddSingleton<IEnvironmentModel, MockCustomEnvironment>();

            var sp = services.BuildServiceProvider();
            var drag = sp.GetRequiredService<IDragModel>();
            var env = sp.GetRequiredService<IEnvironmentModel>();

            Assert(drag is MockCustomDragModel, "Subsequent registration overrides default drag model");
            Assert(Math.Abs(drag.GetDragCoefficient(1.0f) - 0.42f) < 0.001f, "Mock custom drag coefficient returned");
            Assert(env is MockCustomEnvironment, "Subsequent registration overrides default environment model");
        }

        private static void TestMassParallelScopes()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var rootProvider = services.BuildServiceProvider();

            int scopeCount = 200;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, scopeCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, s =>
            {
                try
                {
                    using var scope = rootProvider.CreateScope();
                    var reg = scope.ServiceProvider.GetRequiredService<IMaterialRegistry>();
                    var turn = scope.ServiceProvider.GetRequiredService<ITurnResolver>();
                    var pen = scope.ServiceProvider.GetRequiredService<IMaterialPenetrationSystem>();
                    var drag = scope.ServiceProvider.GetRequiredService<IDragModel>();
                    var env = scope.ServiceProvider.GetRequiredService<IEnvironmentModel>();

                    Assert(reg != null && turn != null && pen != null && drag != null && env != null, "Scope resolution successful");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert(exceptions.IsEmpty, $"Mass parallel scopes threw {exceptions.Count} exceptions");
        }

        private static void TestInterleavedTurnResolverOps()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var sp = services.BuildServiceProvider();

            int instances = 50;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, instances, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                try
                {
                    var resolver = sp.GetRequiredService<ITurnResolver>();
                    var actorId = Guid.NewGuid();
                    var action1 = new GenericTacticalAction(actorId, 10f, dt => { });
                    var action2 = new GenericTacticalAction(actorId, 20f, dt => { });

                    resolver.ScheduleAction(action1);
                    resolver.ScheduleAction(action2);
                    resolver.Tick(5f);

                    // Cancel queued action
                    resolver.CancelAction(action2.Id);
                    Assert(resolver.GetQueuedActions(actorId).Count == 0, "Queued action should be cancelled");

                    resolver.Tick(5f);
                    Assert(action1.IsComplete, "Action1 should be complete");
                    Assert(!resolver.HasActiveActions, "No active actions left");

                    resolver.Reset();
                    Assert(resolver.GlobalTime == 0f, "Global time reset");
                    Assert(resolver.ActiveActorCount == 0, "Active actor count reset");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert(exceptions.IsEmpty, $"Interleaved turn resolver ops had {exceptions.Count} exceptions");
        }
    }
}
