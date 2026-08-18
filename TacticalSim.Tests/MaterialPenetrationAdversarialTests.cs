using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Tests
{
    public class MaterialPenetrationAdversarialTests
    {
        private readonly IMaterialRegistry _registry;
        private readonly IMaterialPenetrationSystem _penetrationSystem;

        public MaterialPenetrationAdversarialTests()
        {
            _registry = new MaterialRegistry();
            _penetrationSystem = new MaterialPenetrationSystem();
        }

        #region Extreme Projectiles

        [Fact]
        public void Projectile_Hypervelocity_5000mps_ConservesEnergyAndNoNaN()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 5000.0f), // Hypervelocity 5000 m/s
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.020f, // 20 grams
                CrossSectionalArea = 0.0001f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.05f, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(result.ExitVelocity));
            Assert.False(float.IsInfinity(result.ExitVelocity));
            Assert.False(float.IsNaN(result.RemainingKineticEnergy));
            Assert.False(float.IsNaN(result.TransferredKineticEnergy));
            Assert.True(result.InitialKineticEnergy > 0f);
            Assert.Equal(result.InitialKineticEnergy, result.RemainingKineticEnergy + result.TransferredKineticEnergy, 1);
            Assert.True(result.ExitVelocity <= 5000.0f);
        }

        [Fact]
        public void Projectile_MicroscopicMass_1e6kg_NoNaNOrDivByZero()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 1000.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 1e-6f, // 1 milligram
                CrossSectionalArea = 1e-8f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.01f, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(result.ExitVelocity));
            Assert.False(float.IsInfinity(result.ExitVelocity));
            Assert.False(float.IsNaN(result.RemainingKineticEnergy));
            Assert.False(float.IsNaN(result.TransferredKineticEnergy));
            Assert.True(result.ExitVelocity <= 1000.0f);
            Assert.Equal(result.InitialKineticEnergy, result.RemainingKineticEnergy + result.TransferredKineticEnergy, 4);
        }

        [Fact]
        public void Projectile_MassivePenetrator_100kg_CalculatesCorrectly()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 100.0f, // 100 kg artillery / bunker buster
                CrossSectionalArea = 0.03f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var concrete = _registry.GetMaterial(MaterialType.Concrete);
            // Ek0 = 0.5 * 100 * 800^2 = 32 MJ. Fdrag = 0.5 * 2400 * 1.8 * 0.03 * 800^2 = 41.472 MN.
            // For 0.5m thickness, work is 20.736 MJ (< 32 MJ), so round perforates with residual energy 11.264 MJ.
            var result = _penetrationSystem.CalculatePenetration(proj, profile, concrete, 0.5f, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(result.ExitVelocity));
            Assert.False(float.IsInfinity(result.ExitVelocity));
            Assert.True(result.ExitVelocity > 0f);
            Assert.Equal(PenetrationOutcome.Perforated, result.Outcome);
            Assert.Equal(result.InitialKineticEnergy, result.RemainingKineticEnergy + result.TransferredKineticEnergy, 0);

            // 1.0m barrier (> 0.7716m) -> Stopped
            var resultStop = _penetrationSystem.CalculatePenetration(proj, profile, concrete, 1.0f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, resultStop.Outcome);
            Assert.Equal(0f, resultStop.ExitVelocity);
            Assert.Equal(resultStop.InitialKineticEnergy, resultStop.TransferredKineticEnergy, 0);
        }

        [Fact]
        public void Projectile_NearZeroSpeed_HandledWithoutException()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 1e-8f), // Sub-threshold velocity
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.05f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0f, result.ExitVelocity);
            Assert.False(float.IsNaN(result.InitialKineticEnergy));
        }

        #endregion

        #region Extreme Materials

        [Fact]
        public void Material_SuperdenseNeutronium_StopsInstantlyWithoutNaN()
        {
            var superdense = new MaterialProperties(
                name: "SuperdenseNeutronium",
                type: MaterialType.Custom,
                density: 1e9f, // 1 billion kg/m^3
                resistanceCoefficient: 100f,
                ricochetAngleThreshold: 1.5f,
                yieldEnergyThreshold: 1000f);

            _registry.RegisterMaterial(superdense);

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 1500.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.010f,
                CrossSectionalArea = 0.0001f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var result = _penetrationSystem.CalculatePenetration(proj, profile, superdense, 0.001f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0f, result.ExitVelocity);
            Assert.Equal(result.InitialKineticEnergy, result.TransferredKineticEnergy);
            Assert.Equal(0f, result.RemainingKineticEnergy);
            Assert.False(float.IsNaN(result.ExitVelocity));
        }

        [Fact]
        public void Material_ZeroResistance_PerforatesWithZeroEnergyLoss()
        {
            var frictionless = new MaterialProperties(
                name: "FrictionlessVoid",
                type: MaterialType.Custom,
                density: 1000f,
                resistanceCoefficient: 0f, // Zero resistance
                ricochetAngleThreshold: 1.57f,
                yieldEnergyThreshold: 10f);

            _registry.RegisterMaterial(frictionless);

            float speed = 800.0f;
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.005f,
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var result = _penetrationSystem.CalculatePenetration(proj, profile, frictionless, 10.0f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Perforated, result.Outcome);
            Assert.Equal(speed, result.ExitVelocity, 2);
            Assert.Equal(0f, result.TransferredKineticEnergy, 2);
            Assert.Equal(result.InitialKineticEnergy, result.RemainingKineticEnergy, 2);
        }

        [Fact]
        public void Material_NearInfiniteYieldEnergy_StopsAllProjectiles()
        {
            var impenetrable = new MaterialProperties(
                name: "AdamantiumYield",
                type: MaterialType.Custom,
                density: 500f,
                resistanceCoefficient: 0.1f,
                ricochetAngleThreshold: 1.57f,
                yieldEnergyThreshold: 1e12f); // 1 TeraJoule yield threshold

            _registry.RegisterMaterial(impenetrable);

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 2000.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.050f,
                CrossSectionalArea = 0.0001f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var result = _penetrationSystem.CalculatePenetration(proj, profile, impenetrable, 0.001f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0f, result.ExitVelocity);
            Assert.Equal(result.InitialKineticEnergy, result.TransferredKineticEnergy);
        }

        #endregion

        #region Geometric Edge Cases

        [Fact]
        public void Geometry_IdenticalEntryAndExitPoints_ZeroThickness_HandledGracefully()
        {
            var proj = new ProjectileState
            {
                Position = new Vector3(5, 5, 5),
                Velocity = new Vector3(0, 0, 700.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            Vector3 pt = new Vector3(5, 5, 5);

            // Identical entry and exit points (distance = 0)
            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, pt, pt, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(result.ExitVelocity));
            Assert.False(float.IsNaN(result.TransferredKineticEnergy));
            Assert.False(float.IsNaN(result.RemainingKineticEnergy));
            Assert.Equal(0f, result.EffectiveThickness);
        }

        [Fact]
        public void Geometry_NonNormalizedNormal_NormalizesCorrectly()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);

            // Large magnitude normal vector (0, 0, -500)
            var resultLargeNormal = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, new Vector3(0, 0, -500.0f));
            // Unit normal vector (0, 0, -1)
            var resultUnitNormal = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, new Vector3(0, 0, -1.0f));

            Assert.Equal(resultUnitNormal.AngleOfIncidence, resultLargeNormal.AngleOfIncidence, 4);
            Assert.Equal(resultUnitNormal.ExitVelocity, resultLargeNormal.ExitVelocity, 2);
            Assert.Equal(resultUnitNormal.TransferredKineticEnergy, resultLargeNormal.TransferredKineticEnergy, 2);
        }

        [Fact]
        public void Geometry_ZeroNormal_DefaultsToDirectHeadOnImpact()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);

            // Zero surface normal
            var resultZeroNormal = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, Vector3.Zero);
            var resultDirectNormal = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, new Vector3(0, 0, -1));

            Assert.Equal(0f, resultZeroNormal.AngleOfIncidence, 4);
            Assert.Equal(resultDirectNormal.ExitVelocity, resultZeroNormal.ExitVelocity, 2);
        }

        [Fact]
        public void Geometry_OpposingNormals_RicochetPointsOutwardInBothCases()
        {
            float angleRad = 80.0f * MathF.PI / 180.0f; // 80 deg glancing
            Vector3 dir = Vector3.Normalize(new Vector3(MathF.Sin(angleRad), 0, MathF.Cos(angleRad)));

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = dir * 900.0f,
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);

            // Surface normal pointing against projectile (+Z component is negative: (0,0,-1))
            var resOutward = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.05f, new Vector3(0, 0, -1));
            // Surface normal pointing with projectile (+Z component is positive: (0,0,1))
            var resInward = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.05f, new Vector3(0, 0, 1));

            Assert.Equal(PenetrationOutcome.Ricochet, resOutward.Outcome);
            Assert.Equal(PenetrationOutcome.Ricochet, resInward.Outcome);

            // In both cases, reflected velocity vector should point away from the barrier face (Z < 0)
            Assert.True(resOutward.ExitVelocityVector.Z < 0, "Outward normal reflection must point away from barrier face.");
            Assert.True(resInward.ExitVelocityVector.Z < 0, "Inward normal reflection must point away from barrier face.");
            Assert.Equal(resOutward.ExitVelocity, resInward.ExitVelocity, 2);
        }

        [Fact]
        public void Geometry_GrazingAngle90Degrees_HandledWithoutDivisionByZero()
        {
            // Grazing at exactly 90 degrees (tangent to surface)
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(800.0f, 0, 0), // Moving strictly along +X
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            // Normal along -Z: dot(d, n) = 0, angle = 90 deg = pi/2 rad
            var result = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.05f, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(result.ExitVelocity));
            Assert.False(float.IsInfinity(result.ExitVelocity));
            Assert.Equal(PenetrationOutcome.Ricochet, result.Outcome);
            Assert.Equal(MathF.PI / 2.0f, result.AngleOfIncidence, 3);
        }

        #endregion

        #region Concurrency & Multi-threading

        [Fact]
        public async Task Concurrency_MultiThreadedPenetrationSystem_StressTest()
        {
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var wood = registry.GetMaterial(MaterialType.Wood);
            var concrete = registry.GetMaterial(MaterialType.Concrete);
            var steel = registry.GetMaterial(MaterialType.Steel);

            var tasks = new Task[50];
            var results = new ConcurrentBag<PenetrationResult>();

            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    var profile = new BallisticProfile
                    {
                        Mass = 0.004f + (index % 10) * 0.001f,
                        CrossSectionalArea = 0.000025f,
                        DragModel = new StandardDragCurve(0.3f)
                    };

                    for (int step = 0; step < 100; step++)
                    {
                        var proj = new ProjectileState
                        {
                            Position = new Vector3(index, step, 0),
                            Velocity = new Vector3(0, 0, 300.0f + step * 5.0f),
                            Time = step * 0.01f
                        };

                        var mat = (index % 3) switch
                        {
                            0 => wood,
                            1 => concrete,
                            _ => steel
                        };

                        var res = system.CalculatePenetration(proj, profile, mat, 0.02f, new Vector3(0, 0, -1));
                        results.Add(res);

                        // Invariant: no NaN/Inf
                        Assert.False(float.IsNaN(res.ExitVelocity));
                        Assert.False(float.IsNaN(res.RemainingKineticEnergy));
                        Assert.False(float.IsNaN(res.TransferredKineticEnergy));
                    }
                });
            }

            await Task.WhenAll(tasks);
            Assert.Equal(50 * 100, results.Count);
        }

        [Fact]
        public async Task Concurrency_MaterialRegistry_HeavyReadWriteContention()
        {
            var registry = new MaterialRegistry();
            var tasks = new Task[30];

            for (int i = 0; i < tasks.Length; i++)
            {
                int threadId = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        string name = $"Dynamic_Mat_{threadId}_{j}";
                        registry.RegisterMaterial(new MaterialProperties(
                            name,
                            MaterialType.Custom,
                            density: 1000f + j,
                            resistanceCoefficient: 1.0f + j * 0.01f,
                            ricochetAngleThreshold: 1.4f,
                            yieldEnergyThreshold: 50f + j));

                        Assert.True(registry.TryGetMaterial(name, out var mat));
                        Assert.Equal(1000f + j, mat.Density);

                        // Concurrent read of standard materials
                        var steel = registry.GetMaterial(MaterialType.Steel);
                        Assert.Equal(7850.0f, steel.Density);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        #endregion

        #region Invariants & Randomized Fuzz Testing

        [Fact]
        public void FuzzTest_RandomizedInputs_PreserveStrictEnergyConservationAndNoNaN()
        {
            var rng = new Random(42);
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var materials = new[]
            {
                registry.GetMaterial(MaterialType.Wood),
                registry.GetMaterial(MaterialType.Concrete),
                registry.GetMaterial(MaterialType.Steel),
                registry.GetMaterial(MaterialType.Glass),
                registry.GetMaterial(MaterialType.Drywall),
                registry.GetMaterial(MaterialType.Sand),
                registry.GetMaterial(MaterialType.Kevlar)
            };

            for (int iter = 0; iter < 1000; iter++)
            {
                float mass = (float)(rng.NextDouble() * 5.0 + 0.001); // 1g to 5kg
                float area = (float)(rng.NextDouble() * 0.01 + 0.00001);
                float speed = (float)(rng.NextDouble() * 3000.0 + 1.0); // 1 to 3000 m/s
                float thickness = (float)(rng.NextDouble() * 2.0 + 0.001); // 1mm to 2m

                // Random non-zero direction
                Vector3 dir = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));

                if (dir.LengthSquared() < 1e-4f) dir = Vector3.UnitZ;

                // Random normal
                Vector3 normal = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));

                if (normal.LengthSquared() < 1e-4f) normal = -Vector3.UnitZ;

                var proj = new ProjectileState
                {
                    Position = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f),
                    Velocity = dir * speed,
                    Time = (float)rng.NextDouble() * 10f
                };

                var profile = new BallisticProfile
                {
                    Mass = mass,
                    CrossSectionalArea = area,
                    DragModel = new StandardDragCurve(0.3f)
                };

                var mat = materials[rng.Next(materials.Length)];
                var result = system.CalculatePenetration(proj, profile, mat, thickness, normal);

                // Invariant checks
                Assert.False(float.IsNaN(result.ExitVelocity), $"NaN ExitVelocity on iteration {iter}");
                Assert.False(float.IsInfinity(result.ExitVelocity), $"Infinity ExitVelocity on iteration {iter}");
                Assert.False(float.IsNaN(result.RemainingKineticEnergy), $"NaN RemainingKineticEnergy on iteration {iter}");
                Assert.False(float.IsNaN(result.TransferredKineticEnergy), $"NaN TransferredKineticEnergy on iteration {iter}");
                Assert.False(float.IsNaN(result.EffectiveThickness), $"NaN EffectiveThickness on iteration {iter}");
                Assert.False(float.IsNaN(result.AngleOfIncidence), $"NaN AngleOfIncidence on iteration {iter}");

                // Energy conservation invariant: Ek0 == Erem + Etrans
                float totalEnergy = result.RemainingKineticEnergy + result.TransferredKineticEnergy;
                float energyDiff = MathF.Abs(result.InitialKineticEnergy - totalEnergy);
                float relDiff = energyDiff / MathF.Max(result.InitialKineticEnergy, 1e-4f);
                Assert.True(relDiff < 1e-3f, $"Energy not conserved on iteration {iter}: diff={energyDiff}, relDiff={relDiff}");

                // No speed amplification
                Assert.True(result.ExitVelocity <= speed + 1e-3f, $"Exit velocity {result.ExitVelocity} exceeded initial {speed} on iteration {iter}");

                // Non-negativity
                Assert.True(result.RemainingKineticEnergy >= -1e-4f);
                Assert.True(result.TransferredKineticEnergy >= -1e-4f);
            }
        }

        #endregion
    }
}
