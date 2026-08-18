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
    public class MaterialPenetrationChallenger2Tests
    {
        private readonly IMaterialRegistry _registry;
        private readonly IMaterialPenetrationSystem _penetrationSystem;

        public MaterialPenetrationChallenger2Tests()
        {
            _registry = new MaterialRegistry();
            _penetrationSystem = new MaterialPenetrationSystem();
        }

        #region 1. Coincident Entry and Exit Points Stress Testing

        [Theory]
        [InlineData(1e-5f)]
        [InlineData(0.1f)]
        [InlineData(1.0f)]
        [InlineData(50.0f)]
        [InlineData(850.0f)]
        [InlineData(3000.0f)]
        [InlineData(100000.0f)]
        public void CoincidentPoints_NonZeroSpeeds_AlwaysPerforatesUnimpeded(float speed)
        {
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var steel = _registry.GetMaterial(MaterialType.Steel);
            var kevlar = _registry.GetMaterial(MaterialType.Kevlar);

            Vector3 pt = new Vector3(12.34f, -56.78f, 910.11f);
            Vector3 vel = Vector3.Normalize(new Vector3(1, -2, 3)) * speed;
            var proj = new ProjectileState
            {
                Position = pt,
                Velocity = vel,
                Time = 2.5f
            };

            foreach (var mat in new[] { wood, steel, kevlar })
            {
                // Explicit coordinates overload with coincident points
                var res = _penetrationSystem.CalculatePenetration(proj, profile, mat, pt, pt, new Vector3(0, 1, 0));

                Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                Assert.Equal(proj.Velocity.Length(), res.ExitVelocity);
                Assert.Equal(proj.Velocity, res.ExitVelocityVector);
                Assert.Equal(0f, res.EffectiveThickness);
                Assert.Equal(0f, res.TransferredKineticEnergy, 3);
                Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy, 3);
                Assert.Equal(pt, res.ExitPoint);
                Assert.Equal(pt, res.ExitState.Position);
                Assert.Equal(vel, res.ExitState.Velocity);
                Assert.Equal(2.5f, res.ExitState.Time);
                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsInfinity(res.ExitVelocity));
            }
        }

        [Fact]
        public void CoincidentPoints_SubMillimeterDeltaDistances_HandledGracefully()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            Vector3 entry = new Vector3(100f, 200f, 300f);
            var proj = new ProjectileState
            {
                Position = entry,
                Velocity = new Vector3(0, 0, 900f),
                Time = 1.0f
            };

            // Test micro deltas: 1e-12m to 1e-5m
            float[] deltas = { 1e-12f, 1e-9f, 1e-7f, 1e-6f, 1e-5f };
            foreach (float d in deltas)
            {
                Vector3 exit = entry + new Vector3(0, 0, d);
                var res = _penetrationSystem.CalculatePenetration(proj, profile, steel, entry, exit, new Vector3(0, 0, -1));

                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsInfinity(res.ExitVelocity));
                Assert.False(float.IsNaN(res.TransferredKineticEnergy));
                Assert.False(float.IsNaN(res.RemainingKineticEnergy));
                Assert.True(res.ExitVelocity > 0f);
                Assert.True(res.ExitVelocity <= 900f);
            }
        }

        [Fact]
        public void CoincidentPoints_StationaryProjectile_ReturnsStopped()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            Vector3 pt = new Vector3(5, 5, 5);
            var projZero = new ProjectileState
            {
                Position = pt,
                Velocity = Vector3.Zero,
                Time = 0.5f
            };

            var res = _penetrationSystem.CalculatePenetration(projZero, profile, steel, pt, pt, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
            Assert.Equal(0f, res.ExitVelocity);
            Assert.Equal(0f, res.InitialKineticEnergy);
            Assert.Equal(0f, res.RemainingKineticEnergy);
            Assert.Equal(0f, res.TransferredKineticEnergy);
        }

        #endregion

        #region 2. Extreme Velocities & Numerical Stability

        [Theory]
        [InlineData(0f)]
        [InlineData(1e-15f)]
        [InlineData(1e-9f)]
        [InlineData(9.99e-7f)]
        [InlineData(1.001e-6f)]
        [InlineData(1e-4f)]
        [InlineData(10000.0f)]
        [InlineData(50000.0f)]
        [InlineData(200000.0f)]
        public void ExtremeVelocities_NoNaNOrInfinityAcrossFullSpectrum(float speed)
        {
            var profile = new BallisticProfile
            {
                Mass = 0.005f,
                CrossSectionalArea = 0.00003f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var concrete = _registry.GetMaterial(MaterialType.Concrete);
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0f
            };

            var res = _penetrationSystem.CalculatePenetration(proj, profile, concrete, 0.10f, new Vector3(0, 0, -1));

            Assert.False(float.IsNaN(res.ExitVelocity), $"NaN ExitVelocity at speed {speed}");
            Assert.False(float.IsInfinity(res.ExitVelocity), $"Inf ExitVelocity at speed {speed}");
            Assert.False(float.IsNaN(res.InitialKineticEnergy));
            Assert.False(float.IsNaN(res.RemainingKineticEnergy));
            Assert.False(float.IsNaN(res.TransferredKineticEnergy));
            Assert.False(float.IsNaN(res.EffectiveThickness));
            Assert.False(float.IsNaN(res.AngleOfIncidence));

            if (speed < 1e-6f)
            {
                Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
                Assert.Equal(0f, res.ExitVelocity);
            }
            else
            {
                // Energy conservation
                Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy + res.TransferredKineticEnergy, 1);
            }
        }

        #endregion

        #region 3. Degenerate and Adversarial Normals

        [Fact]
        public void DegenerateNormals_ZeroAndNearZeroNormals_HandledSafelyWithoutNaN()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800f),
                Time = 0f
            };

            Vector3[] degenerateNormals = {
                Vector3.Zero,
                new Vector3(1e-15f, 0, 0),
                new Vector3(0, 1e-20f, 0),
                new Vector3(1e-10f, 1e-10f, 1e-10f),
                new Vector3(1e8f, 0, 0),
                new Vector3(-1e12f, 1e12f, -1e12f)
            };

            foreach (var n in degenerateNormals)
            {
                var res = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.05f, n);
                Assert.False(float.IsNaN(res.ExitVelocity), $"NaN on normal {n}");
                Assert.False(float.IsInfinity(res.ExitVelocity), $"Inf on normal {n}");
                Assert.False(float.IsNaN(res.AngleOfIncidence), $"NaN angle on normal {n}");
                Assert.True(res.AngleOfIncidence >= 0f && res.AngleOfIncidence <= MathF.PI / 2f + 1e-3f);
            }
        }

        #endregion

        #region 4. Heavy Concurrent Multi-Threading Stress Test

        [Fact]
        public async Task Concurrency_IntenseSimultaneousRegistryAndPenetration_NoExceptions()
        {
            var registry = new MaterialRegistry();
            var system = new MaterialPenetrationSystem();

            int threadCount = 64;
            int iterationsPerThread = 200;
            var tasks = new Task[threadCount];
            var outcomes = new ConcurrentDictionary<PenetrationOutcome, int>();

            for (int t = 0; t < threadCount; t++)
            {
                int tid = t;
                tasks[t] = Task.Run(() =>
                {
                    var rnd = new Random(tid * 1000 + 7);

                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        // 1. Dynamic registration / retrieval
                        string matName = $"ConcurrentMat_{tid}_{i % 5}";
                        if (i % 10 == 0)
                        {
                            registry.RegisterMaterial(new MaterialProperties(
                                matName,
                                MaterialType.Custom,
                                density: 500f + tid * 10f,
                                resistanceCoefficient: 1.0f + (tid % 5) * 0.2f,
                                ricochetAngleThreshold: 1.4f,
                                yieldEnergyThreshold: 50f + i));
                        }

                        MaterialProperties activeMat;
                        if (registry.TryGetMaterial(matName, out var customMat))
                        {
                            activeMat = customMat;
                        }
                        else
                        {
                            activeMat = registry.GetMaterial((MaterialType)(i % 7));
                        }

                        // 2. Penetration calculation
                        float speed = (float)(rnd.NextDouble() * 1500.0 + 10.0);
                        var proj = new ProjectileState
                        {
                            Position = new Vector3((float)rnd.NextDouble() * 10f, (float)rnd.NextDouble() * 10f, 0),
                            Velocity = new Vector3(0, 0, speed),
                            Time = (float)rnd.NextDouble()
                        };

                        var prof = new BallisticProfile
                        {
                            Mass = 0.004f,
                            CrossSectionalArea = 0.000025f,
                            DragModel = new StandardDragCurve(0.3f)
                        };

                        float thick = (float)(rnd.NextDouble() * 0.2 + 0.001);
                        var res = system.CalculatePenetration(proj, prof, activeMat, thick, new Vector3(0, 0, -1));

                        outcomes.AddOrUpdate(res.Outcome, 1, (_, count) => count + 1);

                        Assert.False(float.IsNaN(res.ExitVelocity));
                        Assert.False(float.IsNaN(res.RemainingKineticEnergy));
                        Assert.False(float.IsNaN(res.TransferredKineticEnergy));
                    }
                });
            }

            await Task.WhenAll(tasks);

            int totalCalculations = 0;
            foreach (var kvp in outcomes)
            {
                totalCalculations += kvp.Value;
            }

            Assert.Equal(threadCount * iterationsPerThread, totalCalculations);
        }

        #endregion

        #region 5. Chained Multi-Layer Penetration Simulation

        [Fact]
        public void MultiLayerPenetration_ChainThroughMultipleBarriers_ConservesState()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.008f, // 8g projectile
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 950.0f), // 950 m/s
                Time = 0f
            };

            // Barrier Sequence: Drywall (1.5cm) -> Glass (0.5cm) -> Wood (2cm)
            var drywall = _registry.GetMaterial(MaterialType.Drywall);
            var glass = _registry.GetMaterial(MaterialType.Glass);
            var wood = _registry.GetMaterial(MaterialType.Wood);

            // Layer 1: Drywall
            var res1 = _penetrationSystem.CalculatePenetration(proj, profile, drywall, 0.015f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res1.Outcome);
            Assert.True(res1.ExitVelocity < 950.0f);
            Assert.True(res1.ExitVelocity > 800.0f);

            // Layer 2: Glass (using ExitState of Layer 1)
            var res2 = _penetrationSystem.CalculatePenetration(res1.ExitState, profile, glass, 0.005f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res2.Outcome);
            Assert.True(res2.ExitVelocity < res1.ExitVelocity);

            // Layer 3: Wood
            var res3 = _penetrationSystem.CalculatePenetration(res2.ExitState, profile, wood, 0.02f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, res3.Outcome);
            Assert.True(res3.ExitVelocity < res2.ExitVelocity);

            // Total energy conserved across all stages
            float totalTransferred = res1.TransferredKineticEnergy + res2.TransferredKineticEnergy + res3.TransferredKineticEnergy;
            float initialEnergy = res1.InitialKineticEnergy;
            float finalEnergy = res3.RemainingKineticEnergy;

            Assert.Equal(initialEnergy, totalTransferred + finalEnergy, 2);
        }

        #endregion

        #region 6. Boundary Yield and Perforation Thresholds

        [Fact]
        public void YieldThreshold_ExactBoundary_TransitionsCleanly()
        {
            var yieldMat = new MaterialProperties(
                name: "YieldBoundaryMat",
                type: MaterialType.Custom,
                density: 1000f,
                resistanceCoefficient: 0.1f,
                ricochetAngleThreshold: 1.57f,
                yieldEnergyThreshold: 100.0f); // 100 Joules threshold

            _registry.RegisterMaterial(yieldMat);

            var profile = new BallisticProfile
            {
                Mass = 0.002f, // 2 grams
                CrossSectionalArea = 0.00001f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Case A: Initial kinetic energy = 99.99 J (< 100 J threshold)
            // Ek = 0.5 * 0.002 * v^2 = 99.99 => v = sqrt(99.99 / 0.001) ~= 316.21195 m/s
            float vBelow = MathF.Sqrt(99.99f / 0.001f);
            var projBelow = new ProjectileState { Velocity = new Vector3(0, 0, vBelow) };
            var resBelow = _penetrationSystem.CalculatePenetration(projBelow, profile, yieldMat, 0.001f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Stopped, resBelow.Outcome);
            Assert.Equal(0f, resBelow.ExitVelocity);

            // Case B: Initial kinetic energy = 100.01 J (> 100 J threshold)
            float vAbove = MathF.Sqrt(100.01f / 0.001f);
            var projAbove = new ProjectileState { Velocity = new Vector3(0, 0, vAbove) };
            var resAbove = _penetrationSystem.CalculatePenetration(projAbove, profile, yieldMat, 0.001f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resAbove.Outcome);
            Assert.True(resAbove.ExitVelocity > 0f);
        }

        #endregion

        #region 7. Comprehensive 10,000-Iteration Invariant Fuzz Test

        [Fact]
        public void FuzzTest_10000_AdversarialIterations_StrictInvariants()
        {
            var rng = new Random(1337);
            var system = new MaterialPenetrationSystem();
            var registry = new MaterialRegistry();

            var standardMaterials = new[]
            {
                registry.GetMaterial(MaterialType.Wood),
                registry.GetMaterial(MaterialType.Concrete),
                registry.GetMaterial(MaterialType.Steel),
                registry.GetMaterial(MaterialType.Glass),
                registry.GetMaterial(MaterialType.Drywall),
                registry.GetMaterial(MaterialType.Sand),
                registry.GetMaterial(MaterialType.Kevlar)
            };

            for (int i = 0; i < 10000; i++)
            {
                // Random mass: 1e-6 to 1000 kg
                float mass = MathF.Pow(10f, (float)(rng.NextDouble() * 9.0 - 6.0));
                // Random area: 1e-8 to 0.1 m^2
                float area = MathF.Pow(10f, (float)(rng.NextDouble() * 7.0 - 8.0));
                // Random speed: 0 to 100,000 m/s
                float speed = (float)(rng.NextDouble() < 0.05 ? 0.0 : (rng.NextDouble() < 0.1 ? rng.NextDouble() * 1e-5 : rng.NextDouble() * 5000.0));

                Vector3 dir = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (dir.LengthSquared() < 1e-4f) dir = Vector3.UnitZ;
                else dir = Vector3.Normalize(dir);

                Vector3 vel = dir * speed;

                var proj = new ProjectileState
                {
                    Position = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f),
                    Velocity = vel,
                    Time = (float)rng.NextDouble() * 10f
                };

                var prof = new BallisticProfile
                {
                    Mass = mass,
                    CrossSectionalArea = area,
                    DragModel = new StandardDragCurve(0.3f)
                };

                // Surface normal
                Vector3 norm = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (rng.NextDouble() < 0.05) norm = Vector3.Zero; // 5% zero normal

                // Material selection
                MaterialProperties mat;
                if (rng.NextDouble() < 0.3)
                {
                    // Custom random material
                    mat = new MaterialProperties(
                        $"FuzzMat_{i}",
                        MaterialType.Custom,
                        density: (float)(rng.NextDouble() * 20000.0 + 1.0),
                        resistanceCoefficient: (float)(rng.NextDouble() * 10.0),
                        ricochetAngleThreshold: (float)(rng.NextDouble() * MathF.PI / 2.0),
                        yieldEnergyThreshold: (float)(rng.NextDouble() * 2000.0));
                }
                else
                {
                    mat = standardMaterials[rng.Next(standardMaterials.Length)];
                }

                // Random overload selection
                PenetrationResult res;
                if (rng.NextDouble() < 0.5)
                {
                    // Slab overload: nominal thickness can be negative, zero, or positive
                    float thickness = (float)(rng.NextDouble() < 0.1 ? -rng.NextDouble() : rng.NextDouble() * 2.0);
                    res = system.CalculatePenetration(proj, prof, mat, thickness, norm);

                    if (speed >= 1e-6f && thickness <= 0f)
                    {
                        Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                        Assert.Equal(proj.Velocity.Length(), res.ExitVelocity);
                        Assert.Equal(0f, res.TransferredKineticEnergy, 2);
                    }
                }
                else
                {
                    // Explicit coordinate overload: entry & exit can be coincident, reversed, or spaced
                    Vector3 entry = proj.Position;
                    Vector3 exit;
                    if (rng.NextDouble() < 0.2)
                    {
                        exit = entry; // Coincident points
                    }
                    else
                    {
                        exit = entry + dir * (float)(rng.NextDouble() * 2.0);
                    }

                    res = system.CalculatePenetration(proj, prof, mat, entry, exit, norm);

                    if (speed >= 1e-6f && entry == exit)
                    {
                        Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                        Assert.Equal(proj.Velocity.Length(), res.ExitVelocity);
                        Assert.Equal(0f, res.TransferredKineticEnergy, 2);
                    }
                }

                // Core Invariants across all 10,000 cases:
                Assert.False(float.IsNaN(res.ExitVelocity), $"NaN ExitVelocity at fuzz {i}");
                Assert.False(float.IsInfinity(res.ExitVelocity), $"Inf ExitVelocity at fuzz {i}");
                Assert.False(float.IsNaN(res.InitialKineticEnergy), $"NaN InitialKineticEnergy at fuzz {i}");
                Assert.False(float.IsNaN(res.RemainingKineticEnergy), $"NaN RemainingKineticEnergy at fuzz {i}");
                Assert.False(float.IsNaN(res.TransferredKineticEnergy), $"NaN TransferredKineticEnergy at fuzz {i}");
                Assert.False(float.IsNaN(res.EffectiveThickness), $"NaN EffectiveThickness at fuzz {i}");
                Assert.False(float.IsNaN(res.AngleOfIncidence), $"NaN AngleOfIncidence at fuzz {i}");

                Assert.False(float.IsNaN(res.ExitVelocityVector.X));
                Assert.False(float.IsNaN(res.ExitVelocityVector.Y));
                Assert.False(float.IsNaN(res.ExitVelocityVector.Z));

                Assert.False(float.IsNaN(res.ExitPoint.X));
                Assert.False(float.IsNaN(res.ExitPoint.Y));
                Assert.False(float.IsNaN(res.ExitPoint.Z));

                // Energy conservation
                float totalEnergy = res.RemainingKineticEnergy + res.TransferredKineticEnergy;
                float diff = MathF.Abs(res.InitialKineticEnergy - totalEnergy);
                float maxE = MathF.Max(res.InitialKineticEnergy, 1.0f);
                Assert.True(diff / maxE < 1e-2f, $"Energy not conserved at fuzz {i}: Ek0={res.InitialKineticEnergy}, Total={totalEnergy}");

                // Velocity bounds
                Assert.True(res.ExitVelocity <= speed + 1e-2f, $"Exit velocity amplified at fuzz {i}: Initial={speed}, Exit={res.ExitVelocity}");
                Assert.True(res.ExitVelocity >= 0f);

                if (speed < 1e-6f)
                {
                    Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
                    Assert.Equal(0f, res.ExitVelocity);
                }
            }
        }

        #endregion
    }
}
