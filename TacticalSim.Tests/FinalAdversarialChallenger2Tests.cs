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
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Final Milestone: Adversarial Coverage Hardening (Tier 5) Test Suite.
    /// Authored and empirically verified by Challenger 2.
    /// Focus: TacticalSim.Core.Materials, TacticalSim.Core.Ballistics, and TacticalSim.Core.DependencyInjection.
    /// </summary>
    public class FinalAdversarialChallenger2Tests
    {
        #region 1. TacticalSim.Core.Materials Adversarial Hardening

        [Fact]
        public void Materials_ZeroAndNegativeThickness_MultiAngleStress_PerforatesUnimpeded()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var steel = registry.GetMaterial(MaterialType.Steel);
            var kevlar = registry.GetMaterial(MaterialType.Kevlar);

            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float[] testThicknesses = { 0.0f, -1e-6f, -0.05f, -10.0f, -1000.0f };
            float[] speeds = { 1e-4f, 10.0f, 400.0f, 900.0f, 3500.0f, 15000.0f };
            float[] anglesDeg = { 0f, 15f, 45f, 70f, 85f, 89.9f };

            foreach (var mat in new[] { steel, kevlar })
            {
                foreach (float thickness in testThicknesses)
                {
                    foreach (float speed in speeds)
                    {
                        foreach (float angleDeg in anglesDeg)
                        {
                            float rad = angleDeg * MathF.PI / 180f;
                            Vector3 dir = Vector3.Normalize(new Vector3(MathF.Sin(rad), 0, MathF.Cos(rad)));
                            Vector3 initialVel = dir * speed;

                            var proj = new ProjectileState
                            {
                                Position = new Vector3(5, 10, 15),
                                Velocity = initialVel,
                                Time = 1.25f
                            };

                            var res = system.CalculatePenetration(proj, profile, mat, thickness, new Vector3(0, 0, -1));

                            Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                            Assert.Equal(proj.Velocity.Length(), res.ExitVelocity);
                            Assert.Equal(initialVel, res.ExitVelocityVector);
                            Assert.Equal(0f, res.EffectiveThickness);
                            Assert.Equal(0f, res.TransferredKineticEnergy, 3);
                            Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy, 3);
                            Assert.Equal(proj.Position, res.ExitPoint);
                            Assert.Equal(proj.Position, res.ExitState.Position);
                            Assert.Equal(initialVel, res.ExitState.Velocity);
                            Assert.Equal(proj.Time, res.ExitState.Time);
                            Assert.False(float.IsNaN(res.ExitVelocity));
                        }
                    }
                }
            }
        }

        [Fact]
        public void Materials_Hypervelocity_ExtremeKineticEnergy_ConservesEnergy()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var concrete = registry.GetMaterial(MaterialType.Concrete);
            var steel = registry.GetMaterial(MaterialType.Steel);

            var profile = new BallisticProfile
            {
                Mass = 0.050f, // 50 grams
                CrossSectionalArea = 0.0001f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float[] hyperSpeeds = { 5000f, 10000f, 25000f, 50000f };

            foreach (float speed in hyperSpeeds)
            {
                var proj = new ProjectileState
                {
                    Position = Vector3.Zero,
                    Velocity = new Vector3(0, 0, speed),
                    Time = 0f
                };

                // Thin steel (5mm)
                var resSteel = system.CalculatePenetration(proj, profile, steel, 0.005f, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(resSteel.ExitVelocity));
                Assert.False(float.IsInfinity(resSteel.ExitVelocity));
                Assert.Equal(resSteel.InitialKineticEnergy, resSteel.RemainingKineticEnergy + resSteel.TransferredKineticEnergy, 1);
                Assert.True(resSteel.ExitVelocity <= speed);

                // Thick concrete (2.0m)
                var resConc = system.CalculatePenetration(proj, profile, concrete, 2.0f, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(resConc.ExitVelocity));
                Assert.False(float.IsInfinity(resConc.ExitVelocity));
                Assert.Equal(resConc.InitialKineticEnergy, resConc.RemainingKineticEnergy + resConc.TransferredKineticEnergy, 1);
            }
        }

        [Fact]
        public void Materials_ExtremeDensities_SuperLightToSuperDense_SmoothScaling()
        {
            var system = new MaterialPenetrationSystem();
            var profile = new BallisticProfile
            {
                Mass = 0.010f,
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 1000f),
                Time = 0f
            };

            // 1. Super-light density (1e-6 kg/m^3)
            var ultraLight = new MaterialProperties("UltraLight", MaterialType.Custom, 1e-6f, 1.0f, 1.5f, 0f);
            var resLight = system.CalculatePenetration(proj, profile, ultraLight, 1.0f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resLight.Outcome);
            Assert.Equal(1000f, resLight.ExitVelocity, 2);
            Assert.Equal(0f, resLight.TransferredKineticEnergy, 2);

            // 2. Super-dense matter (1e12 kg/m^3)
            var ultraDense = new MaterialProperties("UltraDense", MaterialType.Custom, 1e12f, 1.0f, 1.5f, 100f);
            var resDense = system.CalculatePenetration(proj, profile, ultraDense, 0.001f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, resDense.Outcome);
            Assert.Equal(0f, resDense.ExitVelocity);
            Assert.Equal(resDense.InitialKineticEnergy, resDense.TransferredKineticEnergy, 2);
            Assert.False(float.IsNaN(resDense.ExitVelocity));
        }

        [Fact]
        public void Materials_ZeroResistanceAndZeroYield_BehavesAsFrictionlessMedium()
        {
            var system = new MaterialPenetrationSystem();
            var zeroMedium = new MaterialProperties("ZeroMedium", MaterialType.Custom, 5000f, 0f, 1.57f, 0f);

            var profile = new BallisticProfile
            {
                Mass = 0.005f,
                CrossSectionalArea = 0.00003f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 750f),
                Time = 0f
            };

            var res = system.CalculatePenetration(proj, profile, zeroMedium, 50.0f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
            Assert.Equal(750f, res.ExitVelocity, 3);
            Assert.Equal(0f, res.TransferredKineticEnergy, 3);
            Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy, 3);
        }

        [Fact]
        public void Materials_HighObliquityGrazingAngle_SpecularReflectionAndEnergyDecay()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();
            var steel = registry.GetMaterial(MaterialType.Steel);

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float speed = 850f;
            Vector3 barrierNormal = new Vector3(0, 0, -1);

            for (float angleDeg = 70.5f; angleDeg <= 89.5f; angleDeg += 0.5f)
            {
                float rad = angleDeg * MathF.PI / 180f;
                Vector3 inDir = Vector3.Normalize(new Vector3(MathF.Sin(rad), 0, MathF.Cos(rad)));

                var proj = new ProjectileState
                {
                    Position = new Vector3(10, 20, 30),
                    Velocity = inDir * speed,
                    Time = 0.1f
                };

                var res = system.CalculatePenetration(proj, profile, steel, 0.05f, barrierNormal);

                Assert.Equal(PenetrationOutcome.Ricochet, res.Outcome);
                Assert.Equal(rad, res.AngleOfIncidence, 3);

                Assert.True(res.ExitVelocityVector.Z < 0f, $"Reflected Z must be negative at {angleDeg} deg.");
                Assert.True(res.ExitVelocityVector.X > 0f, $"Reflected X must remain positive at {angleDeg} deg.");

                Vector3 outDir = Vector3.Normalize(res.ExitVelocityVector);
                float reflCos = MathF.Abs(Vector3.Dot(outDir, barrierNormal));
                float inCos = MathF.Abs(Vector3.Dot(inDir, barrierNormal));
                Assert.Equal(inCos, reflCos, 3);

                float ek0 = 0.5f * profile.Mass * speed * speed;
                float expectedLoss = ek0 * (1f - MathF.Sin(rad)) * 0.3f;
                float expectedRem = ek0 - expectedLoss;

                Assert.Equal(expectedRem, res.RemainingKineticEnergy, 2);
                Assert.Equal(expectedLoss, res.TransferredKineticEnergy, 2);
                Assert.Equal(ek0, res.RemainingKineticEnergy + res.TransferredKineticEnergy, 2);
            }
        }

        [Fact]
        public void Materials_FiveLayerChainedPenetration_SandwichArmor()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var glass = registry.GetMaterial(MaterialType.Glass);
            var wood = registry.GetMaterial(MaterialType.Wood);
            var drywall = registry.GetMaterial(MaterialType.Drywall);
            var concrete = registry.GetMaterial(MaterialType.Concrete);
            var steel = registry.GetMaterial(MaterialType.Steel);

            var profile = new BallisticProfile
            {
                Mass = 0.015f,
                CrossSectionalArea = 0.00006f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float v0 = 1200f;
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, v0),
                Time = 0f
            };

            float initialEk = 0.5f * profile.Mass * v0 * v0;

            var res1 = system.CalculatePenetration(proj, profile, glass, 0.008f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res1.Outcome);

            var res2 = system.CalculatePenetration(res1.ExitState, profile, wood, 0.03f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res2.Outcome);

            var res3 = system.CalculatePenetration(res2.ExitState, profile, drywall, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res3.Outcome);

            var res4 = system.CalculatePenetration(res3.ExitState, profile, concrete, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res4.Outcome);

            var res5 = system.CalculatePenetration(res4.ExitState, profile, steel, 0.003f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res5.Outcome);

            Assert.True(v0 > res1.ExitVelocity);
            Assert.True(res1.ExitVelocity > res2.ExitVelocity);
            Assert.True(res2.ExitVelocity > res3.ExitVelocity);
            Assert.True(res3.ExitVelocity > res4.ExitVelocity);
            Assert.True(res4.ExitVelocity > res5.ExitVelocity);

            float totalEnergyTransferred = res1.TransferredKineticEnergy + res2.TransferredKineticEnergy +
                                           res3.TransferredKineticEnergy + res4.TransferredKineticEnergy +
                                           res5.TransferredKineticEnergy;
            float finalRemainingEnergy = res5.RemainingKineticEnergy;

            Assert.Equal(initialEk, totalEnergyTransferred + finalRemainingEnergy, 2);
        }

        [Fact]
        public void Materials_SubYieldStopping_ExactTransition()
        {
            var system = new MaterialPenetrationSystem();
            var yieldMat = new MaterialProperties("YieldStepMat", MaterialType.Custom, 500f, 0.01f, 1.57f, 250.0f);

            var profile = new BallisticProfile
            {
                Mass = 0.005f,
                CrossSectionalArea = 0.00002f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float vBelow = MathF.Sqrt(249.95f / 0.0025f);
            var projBelow = new ProjectileState { Velocity = new Vector3(0, 0, vBelow) };
            var resBelow = system.CalculatePenetration(projBelow, profile, yieldMat, 0.001f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, resBelow.Outcome);
            Assert.Equal(0f, resBelow.ExitVelocity);
            Assert.Equal(resBelow.InitialKineticEnergy, resBelow.TransferredKineticEnergy, 2);

            float vAbove = MathF.Sqrt(250.05f / 0.0025f);
            var projAbove = new ProjectileState { Velocity = new Vector3(0, 0, vAbove) };
            var resAbove = system.CalculatePenetration(projAbove, profile, yieldMat, 0.001f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resAbove.Outcome);
            Assert.True(resAbove.ExitVelocity > 0f);
        }

        [Fact]
        public void Materials_Fuzz_20000RandomizedTrials_StrictInvariants()
        {
            var rng = new Random(2026_0818);
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var standardMats = new[]
            {
                registry.GetMaterial(MaterialType.Wood),
                registry.GetMaterial(MaterialType.Concrete),
                registry.GetMaterial(MaterialType.Steel),
                registry.GetMaterial(MaterialType.Glass),
                registry.GetMaterial(MaterialType.Drywall),
                registry.GetMaterial(MaterialType.Sand),
                registry.GetMaterial(MaterialType.Kevlar)
            };

            const int iterations = 20000;
            for (int i = 0; i < iterations; i++)
            {
                float mass = MathF.Pow(10f, (float)(rng.NextDouble() * 7.0 - 5.0));
                float area = MathF.Pow(10f, (float)(rng.NextDouble() * 6.0 - 7.0));
                float speed = (float)(rng.NextDouble() * 6000.0 + 1e-8);
                float thickness = (float)(rng.NextDouble() * 5.0 - 1.0);

                Vector3 dir = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));
                if (dir.LengthSquared() < 1e-4f) dir = Vector3.UnitZ;

                Vector3 norm = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (norm.LengthSquared() < 1e-4f) norm = -Vector3.UnitZ;
                else norm = Vector3.Normalize(norm);

                var proj = new ProjectileState
                {
                    Position = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f),
                    Velocity = dir * speed,
                    Time = (float)rng.NextDouble() * 5f
                };

                var prof = new BallisticProfile
                {
                    Mass = mass,
                    CrossSectionalArea = area,
                    DragModel = new StandardDragCurve(0.3f)
                };

                var mat = standardMats[rng.Next(standardMats.Length)];
                var res = system.CalculatePenetration(proj, prof, mat, thickness, norm);

                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsInfinity(res.ExitVelocity));
                Assert.False(float.IsNaN(res.InitialKineticEnergy));
                Assert.False(float.IsNaN(res.RemainingKineticEnergy));
                Assert.False(float.IsNaN(res.TransferredKineticEnergy));
                Assert.False(float.IsNaN(res.EffectiveThickness));
                Assert.False(float.IsNaN(res.AngleOfIncidence));

                float eSum = res.RemainingKineticEnergy + res.TransferredKineticEnergy;
                float eDiff = MathF.Abs(res.InitialKineticEnergy - eSum);
                float maxE = MathF.Max(res.InitialKineticEnergy, 1e-3f);
                Assert.True(eDiff / maxE < 1e-2f, $"Energy not conserved at iteration {i}: diff={eDiff}");

                Assert.True(res.ExitVelocity <= speed + 1e-3f);
                Assert.True(res.ExitVelocity >= 0f);
            }
        }

        #endregion

        #region 2. TacticalSim.Core.Ballistics Adversarial Hardening

        [Fact]
        public void Ballistics_MachNumberSweep_TransonicPeakAndSupersonicDecay()
        {
            var dragModel = new StandardDragCurve(0.3f);

            for (float mach = 0.0f; mach < 0.79f; mach += 0.05f)
            {
                Assert.Equal(0.3f, dragModel.GetDragCoefficient(mach), 4);
            }

            float prevCd = 0.3f;
            for (float mach = 0.8f; mach <= 1.0f; mach += 0.02f)
            {
                float cd = dragModel.GetDragCoefficient(mach);
                Assert.True(cd >= prevCd - 1e-5f, $"Transonic drag must rise monotonically at Mach {mach}");
                prevCd = cd;
            }
            Assert.Equal(0.75f, dragModel.GetDragCoefficient(1.0f), 3);

            for (float mach = 1.02f; mach <= 1.2f; mach += 0.02f)
            {
                float cd = dragModel.GetDragCoefficient(mach);
                Assert.True(cd <= prevCd + 1e-5f, $"Transonic drag must fall after Mach 1.0 at Mach {mach}");
                prevCd = cd;
            }
            Assert.Equal(0.36f, dragModel.GetDragCoefficient(1.2f), 3);

            for (float mach = 1.25f; mach <= 5.0f; mach += 0.25f)
            {
                float cd = dragModel.GetDragCoefficient(mach);
                Assert.True(cd >= 0.3f, $"Supersonic drag cannot drop below base Cd at Mach {mach}");
                Assert.True(cd <= 0.36f);
            }
        }

        [Fact]
        public void Ballistics_ICAOStandardAtmosphere_BarometricFormulaVerification()
        {
            var atmo = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));

            float prevDensity = float.MaxValue;

            for (float alt = 0f; alt <= 10000f; alt += 500f)
            {
                var cond = atmo.GetConditionsAt(new Vector3(0, alt, 0));

                Assert.False(float.IsNaN(cond.AirDensity));
                Assert.False(float.IsNaN(cond.SpeedOfSound));
                Assert.True(cond.AirDensity > 0f);
                Assert.True(cond.SpeedOfSound > 0f);

                Assert.True(cond.AirDensity < prevDensity, $"Air density must strictly decrease with altitude at {alt}m");
                prevDensity = cond.AirDensity;
            }

            var seaLevel = atmo.GetConditionsAt(Vector3.Zero);
            Assert.True(seaLevel.AirDensity > 1.20f && seaLevel.AirDensity < 1.25f);
            Assert.True(seaLevel.SpeedOfSound > 335f && seaLevel.SpeedOfSound < 345f);

            // Sub-zero altitude (e.g. trench or underground) clamps to sea level minimum
            var subSea = atmo.GetConditionsAt(new Vector3(0, -500f, 0));
            Assert.Equal(seaLevel.AirDensity, subSea.AirDensity, 4);
            Assert.Equal(seaLevel.SpeedOfSound, subSea.SpeedOfSound, 4);
        }

        [Fact]
        public void Ballistics_SolverRK4_AerodynamicWindDeflection_CrosswindHeadwindTailwind()
        {
            var drag = new StandardDragCurve(0.3f);
            var profile = new BallisticProfile
            {
                Mass = 0.009f,
                CrossSectionalArea = 0.00005f,
                DragModel = drag
            };

            var initialProj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800f),
                Time = 0f
            };

            float dt = 0.01f;
            int steps = 100;

            var envStill = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));
            var stateStill = initialProj;
            for (int i = 0; i < steps; i++) stateStill = BallisticSolver.StepRK4(stateStill, profile, envStill, dt);

            var envCrosswind = new ICAOStandardAtmosphere(new Vector3(15f, 0, 0), new Vector3(0, -9.80665f, 0));
            var stateCrosswind = initialProj;
            for (int i = 0; i < steps; i++) stateCrosswind = BallisticSolver.StepRK4(stateCrosswind, profile, envCrosswind, dt);

            var envHeadwind = new ICAOStandardAtmosphere(new Vector3(0, 0, -20f), new Vector3(0, -9.80665f, 0));
            var stateHeadwind = initialProj;
            for (int i = 0; i < steps; i++) stateHeadwind = BallisticSolver.StepRK4(stateHeadwind, profile, envHeadwind, dt);

            var envTailwind = new ICAOStandardAtmosphere(new Vector3(0, 0, 20f), new Vector3(0, -9.80665f, 0));
            var stateTailwind = initialProj;
            for (int i = 0; i < steps; i++) stateTailwind = BallisticSolver.StepRK4(stateTailwind, profile, envTailwind, dt);

            Assert.True(stateCrosswind.Position.X > 0.5f, $"Crosswind must deflect bullet in +X, got {stateCrosswind.Position.X}");
            Assert.Equal(0f, stateStill.Position.X, 3);

            Assert.True(stateHeadwind.Position.Z < stateStill.Position.Z, "Headwind must reduce flight distance.");
            Assert.True(stateHeadwind.Velocity.Z < stateStill.Velocity.Z, "Headwind must reduce final velocity.");

            Assert.True(stateTailwind.Position.Z > stateStill.Position.Z, "Tailwind must extend flight distance.");
            Assert.True(stateTailwind.Velocity.Z > stateStill.Velocity.Z, "Tailwind must increase final velocity.");
        }

        [Fact]
        public void Ballistics_SolverRK4_StationaryAndMicroTimesteps()
        {
            var env = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));
            var drag = new StandardDragCurve(0.3f);
            var profile = new BallisticProfile { Mass = 0.01f, CrossSectionalArea = 0.00005f, DragModel = drag };

            var dropState = new ProjectileState
            {
                Position = new Vector3(0, 100f, 0),
                Velocity = Vector3.Zero,
                Time = 0f
            };

            float dt = 0.0001f;
            for (int i = 0; i < 10000; i++)
            {
                dropState = BallisticSolver.StepRK4(dropState, profile, env, dt);
            }

            Assert.False(float.IsNaN(dropState.Position.Y));
            Assert.False(float.IsNaN(dropState.Velocity.Y));
            Assert.True(dropState.Position.Y < 100f, "Dropped projectile must fall under gravity.");
            Assert.True(dropState.Velocity.Y < 0f, "Downward velocity must be negative.");
        }

        #endregion

        #region 3. TacticalSim.Core.DependencyInjection Adversarial Hardening

        [Fact]
        public void DI_ValidateScopesAndBuildValidation_NoCaptiveDependencies()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();

            var options = new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            };

            var provider = services.BuildServiceProvider(options);
            Assert.NotNull(provider);

            using var scope = provider.CreateScope();
            var sp = scope.ServiceProvider;

            var reg = sp.GetRequiredService<IMaterialRegistry>();
            var pen = sp.GetRequiredService<IMaterialPenetrationSystem>();
            var turn = sp.GetRequiredService<ITurnResolver>();
            var drag = sp.GetRequiredService<IDragModel>();
            var env = sp.GetRequiredService<IEnvironmentModel>();

            Assert.NotNull(reg);
            Assert.NotNull(pen);
            Assert.NotNull(turn);
            Assert.NotNull(drag);
            Assert.NotNull(env);
        }

        [Fact]
        public void DI_IdempotentRegistration_MultipleCallsDoNotCorruptState()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            services.AddTacticalSimCore();
            services.AddTacticalSimCore();

            var provider = services.BuildServiceProvider();

            var reg1 = provider.GetRequiredService<IMaterialRegistry>();
            var reg2 = provider.GetRequiredService<IMaterialRegistry>();
            Assert.Same(reg1, reg2);

            var pen1 = provider.GetRequiredService<IMaterialPenetrationSystem>();
            var pen2 = provider.GetRequiredService<IMaterialPenetrationSystem>();
            Assert.NotSame(pen1, pen2);
        }

        [Fact]
        public void DI_CustomOverrides_PreAndPostRegistration_RespectsUserBindings()
        {
            // Case 1: Post-registration override
            var services1 = new ServiceCollection();
            services1.AddTacticalSimCore();
            var customDrag = new StandardDragCurve(0.65f);
            services1.AddSingleton<IDragModel>(customDrag);

            var provider1 = services1.BuildServiceProvider();
            var resolvedDrag1 = provider1.GetRequiredService<IDragModel>();
            Assert.Same(customDrag, resolvedDrag1);
            Assert.Equal(0.65f, resolvedDrag1.GetDragCoefficient(0.5f));

            // Case 2: Custom Environment Model
            var customEnv = new ICAOStandardAtmosphere(new Vector3(10f, 0, 0), new Vector3(0, -9.81f, 0));
            var services2 = new ServiceCollection();
            services2.AddTacticalSimCore();
            services2.AddSingleton<IEnvironmentModel>(customEnv);

            var provider2 = services2.BuildServiceProvider();
            var resolvedEnv2 = provider2.GetRequiredService<IEnvironmentModel>();
            Assert.Same(customEnv, resolvedEnv2);
            var cond = resolvedEnv2.GetConditionsAt(Vector3.Zero);
            Assert.Equal(10f, cond.WindVelocity.X);
        }

        [Fact]
        public void DI_MassiveParallelConcurrentResolutions_ThreadSafety()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            const int numThreads = 128;
            var outcomes = new ConcurrentBag<bool>();

            Parallel.For(0, numThreads, threadId =>
            {
                using var scope = provider.CreateScope();
                var sp = scope.ServiceProvider;

                var reg = sp.GetRequiredService<IMaterialRegistry>();
                var pen = sp.GetRequiredService<IMaterialPenetrationSystem>();
                var resolver = sp.GetRequiredService<ITurnResolver>();
                var drag = sp.GetRequiredService<IDragModel>();
                var env = sp.GetRequiredService<IEnvironmentModel>();

                var actor = Guid.NewGuid();
                bool actionDone = false;
                var action = new GenericTacticalAction(actor, 0.1f, onComplete: () => actionDone = true);

                resolver.ScheduleAction(action);
                resolver.Tick(0.1f);

                var wood = reg.GetMaterial("Wood");
                var proj = new ProjectileState { Velocity = new Vector3(0, 0, 800f) };
                var prof = new BallisticProfile { Mass = 0.005f, CrossSectionalArea = 0.00003f, DragModel = drag };
                var result = pen.CalculatePenetration(proj, prof, wood, 0.02f, new Vector3(0, 0, -1));

                outcomes.Add(actionDone && result.Outcome == PenetrationOutcome.Perforated);
            });

            Assert.Equal(numThreads, outcomes.Count);
            Assert.All(outcomes, Assert.True);
        }

        [Fact]
        public void DI_NullArgumentGuards_AllMethodsThrowArgumentNullException()
        {
            IServiceCollection nullServices = null!;
            Assert.Throws<ArgumentNullException>(() => nullServices.AddTacticalSimCore());
            Assert.Throws<ArgumentNullException>(() => nullServices.AddMaterialPenetration());
            Assert.Throws<ArgumentNullException>(() => nullServices.AddSimulationServices());
        }

        #endregion
    }
}
