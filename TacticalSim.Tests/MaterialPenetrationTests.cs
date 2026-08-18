using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Tests
{
    public class MaterialPenetrationTests
    {
        private readonly IMaterialRegistry _registry;
        private readonly IMaterialPenetrationSystem _penetrationSystem;

        public MaterialPenetrationTests()
        {
            _registry = new MaterialRegistry();
            _penetrationSystem = new MaterialPenetrationSystem();
        }

        [Fact]
        public void MaterialRegistry_StandardMaterials_ArePreloaded()
        {
            // Verify Wood
            var wood = _registry.GetMaterial(MaterialType.Wood);
            Assert.Equal("Wood", wood.Name);
            Assert.Equal(MaterialType.Wood, wood.Type);
            Assert.Equal(600.0f, wood.Density);
            Assert.Equal(1.0f, wood.ResistanceCoefficient);
            Assert.Equal(1.48f, wood.RicochetAngleThreshold);
            Assert.Equal(50.0f, wood.YieldEnergyThreshold);

            // Verify Concrete
            var concrete = _registry.GetMaterial(MaterialType.Concrete);
            Assert.Equal("Concrete", concrete.Name);
            Assert.Equal(MaterialType.Concrete, concrete.Type);
            Assert.Equal(2400.0f, concrete.Density);
            Assert.Equal(1.8f, concrete.ResistanceCoefficient);
            Assert.Equal(1.31f, concrete.RicochetAngleThreshold);
            Assert.Equal(200.0f, concrete.YieldEnergyThreshold);

            // Verify Steel
            var steel = _registry.GetMaterial(MaterialType.Steel);
            Assert.Equal("Steel", steel.Name);
            Assert.Equal(MaterialType.Steel, steel.Type);
            Assert.Equal(7850.0f, steel.Density);
            Assert.Equal(2.5f, steel.ResistanceCoefficient);
            Assert.Equal(1.22f, steel.RicochetAngleThreshold);
            Assert.Equal(500.0f, steel.YieldEnergyThreshold);

            // Verify Glass
            var glass = _registry.GetMaterial(MaterialType.Glass);
            Assert.Equal("Glass", glass.Name);
            Assert.Equal(MaterialType.Glass, glass.Type);
            Assert.Equal(2500.0f, glass.Density);
            Assert.Equal(0.5f, glass.ResistanceCoefficient);
            Assert.Equal(1.48f, glass.RicochetAngleThreshold);
            Assert.Equal(20.0f, glass.YieldEnergyThreshold);

            // Verify Drywall
            var drywall = _registry.GetMaterial(MaterialType.Drywall);
            Assert.Equal("Drywall", drywall.Name);
            Assert.Equal(MaterialType.Drywall, drywall.Type);
            Assert.Equal(800.0f, drywall.Density);
            Assert.Equal(0.4f, drywall.ResistanceCoefficient);
            Assert.Equal(1.52f, drywall.RicochetAngleThreshold);
            Assert.Equal(10.0f, drywall.YieldEnergyThreshold);

            // Verify Sand
            var sand = _registry.GetMaterial(MaterialType.Sand);
            Assert.Equal("Sand", sand.Name);
            Assert.Equal(MaterialType.Sand, sand.Type);
            Assert.Equal(1600.0f, sand.Density);
            Assert.Equal(1.5f, sand.ResistanceCoefficient);
            Assert.Equal(1.55f, sand.RicochetAngleThreshold);
            Assert.Equal(30.0f, sand.YieldEnergyThreshold);

            // Verify Kevlar
            var kevlar = _registry.GetMaterial(MaterialType.Kevlar);
            Assert.Equal("Kevlar", kevlar.Name);
            Assert.Equal(MaterialType.Kevlar, kevlar.Type);
            Assert.Equal(1440.0f, kevlar.Density);
            Assert.Equal(3.2f, kevlar.ResistanceCoefficient);
            Assert.Equal(1.48f, kevlar.RicochetAngleThreshold);
            Assert.Equal(100.0f, kevlar.YieldEnergyThreshold);
        }

        [Fact]
        public void MaterialRegistry_CustomMaterial_RegistersAndRetrievesCorrectly()
        {
            var customMaterial = new MaterialProperties(
                name: "Titanium-Composite",
                type: MaterialType.Custom,
                density: 4500.0f,
                resistanceCoefficient: 2.2f,
                ricochetAngleThreshold: 1.25f,
                yieldEnergyThreshold: 350.0f);

            _registry.RegisterMaterial(customMaterial);

            // Retrieve by exact name
            var retrievedByName = _registry.GetMaterial("Titanium-Composite");
            Assert.Equal("Titanium-Composite", retrievedByName.Name);
            Assert.Equal(4500.0f, retrievedByName.Density);

            // Retrieve by case-insensitive name
            Assert.True(_registry.TryGetMaterial("titanium-composite", out var retrievedCaseInsensitive));
            Assert.Equal(4500.0f, retrievedCaseInsensitive.Density);

            // Verify non-existent material fails gracefully
            Assert.False(_registry.TryGetMaterial("NonExistentMaterial", out _));
            Assert.Throws<KeyNotFoundException>(() => _registry.GetMaterial("NonExistentMaterial"));
        }

        [Fact]
        public void Penetration_VelocityLoss_MonotonicWithDensity()
        {
            // Projectile: 5.56mm NATO (4g, 900 m/s, area = 2.43e-5 m^2)
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.0000243f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float thickness = 0.03f; // 3 cm
            Vector3 normal = new Vector3(0, 0, -1);

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var concrete = _registry.GetMaterial(MaterialType.Concrete);
            var steel = _registry.GetMaterial(MaterialType.Steel);

            var resultWood = _penetrationSystem.CalculatePenetration(proj, profile, wood, thickness, normal);
            var resultConcrete = _penetrationSystem.CalculatePenetration(proj, profile, concrete, thickness, normal);
            var resultSteel = _penetrationSystem.CalculatePenetration(proj, profile, steel, thickness, normal);

            // Wood should lose less energy than Concrete, which loses less than Steel
            Assert.True(resultWood.TransferredKineticEnergy < resultConcrete.TransferredKineticEnergy,
                $"Expected Wood energy transfer ({resultWood.TransferredKineticEnergy}) < Concrete ({resultConcrete.TransferredKineticEnergy})");
            Assert.True(resultConcrete.TransferredKineticEnergy < resultSteel.TransferredKineticEnergy,
                $"Expected Concrete energy transfer ({resultConcrete.TransferredKineticEnergy}) < Steel ({resultSteel.TransferredKineticEnergy})");

            // Exit velocity: Wood > Concrete > Steel
            Assert.True(resultWood.ExitVelocity > resultConcrete.ExitVelocity,
                $"Expected Wood exit velocity ({resultWood.ExitVelocity}) > Concrete ({resultConcrete.ExitVelocity})");
            Assert.True(resultConcrete.ExitVelocity > resultSteel.ExitVelocity,
                $"Expected Concrete exit velocity ({resultConcrete.ExitVelocity}) > Steel ({resultSteel.ExitVelocity})");
        }

        [Fact]
        public void Penetration_VelocityLoss_MonotonicWithThickness()
        {
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 850.0f),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            Vector3 normal = new Vector3(0, 0, -1);

            var res02 = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, normal);
            var res05 = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.05f, normal);
            var res10 = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.10f, normal);

            // Monotonic decrease in exit velocity with thickness
            Assert.True(res02.ExitVelocity > res05.ExitVelocity,
                $"Exit velocity for 0.02m ({res02.ExitVelocity}) should be > 0.05m ({res05.ExitVelocity})");
            Assert.True(res05.ExitVelocity > res10.ExitVelocity,
                $"Exit velocity for 0.05m ({res05.ExitVelocity}) should be > 0.10m ({res10.ExitVelocity})");

            // Monotonic increase in transferred energy
            Assert.True(res02.TransferredKineticEnergy < res05.TransferredKineticEnergy);
            Assert.True(res05.TransferredKineticEnergy < res10.TransferredKineticEnergy);
        }

        [Fact]
        public void Penetration_AngledImpact_IncreasesEffectiveThickness()
        {
            float speed = 800.0f;
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            float nominalThickness = 0.05f; // 5 cm
            Vector3 normal = new Vector3(0, 0, -1); // Surface face facing -Z

            // Normal impact (0 degrees): velocity along +Z
            var proj0 = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0.0f
            };
            var res0 = _penetrationSystem.CalculatePenetration(proj0, profile, wood, nominalThickness, normal);

            // 45 degrees impact: velocity = (sin 45, 0, cos 45) * speed
            float rad45 = MathF.PI / 4.0f;
            var proj45 = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(MathF.Sin(rad45) * speed, 0, MathF.Cos(rad45) * speed),
                Time = 0.0f
            };
            var res45 = _penetrationSystem.CalculatePenetration(proj45, profile, wood, nominalThickness, normal);

            // 60 degrees impact: velocity = (sin 60, 0, cos 60) * speed
            float rad60 = MathF.PI / 3.0f;
            var proj60 = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(MathF.Sin(rad60) * speed, 0, MathF.Cos(rad60) * speed),
                Time = 0.0f
            };
            var res60 = _penetrationSystem.CalculatePenetration(proj60, profile, wood, nominalThickness, normal);

            // Effective thickness check: T_eff(60 deg) = T0 / cos(60 deg) = T0 / 0.5 = 2 * T0 = 0.10m
            Assert.Equal(0.05f, res0.EffectiveThickness, 3);
            Assert.Equal(0.05f / MathF.Cos(rad45), res45.EffectiveThickness, 3);
            Assert.Equal(0.10f, res60.EffectiveThickness, 3);

            // Angles of incidence
            Assert.Equal(0.0f, res0.AngleOfIncidence, 2);
            Assert.Equal(rad45, res45.AngleOfIncidence, 2);
            Assert.Equal(rad60, res60.AngleOfIncidence, 2);

            // Energy transferred increases with obliquity
            Assert.True(res60.TransferredKineticEnergy > res45.TransferredKineticEnergy);
            Assert.True(res45.TransferredKineticEnergy > res0.TransferredKineticEnergy);
        }

        [Fact]
        public void Penetration_ConservesTotalKineticEnergy()
        {
            var materials = new[]
            {
                _registry.GetMaterial(MaterialType.Wood),
                _registry.GetMaterial(MaterialType.Concrete),
                _registry.GetMaterial(MaterialType.Steel),
                _registry.GetMaterial(MaterialType.Glass),
                _registry.GetMaterial(MaterialType.Drywall),
                _registry.GetMaterial(MaterialType.Sand),
                _registry.GetMaterial(MaterialType.Kevlar)
            };

            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            Vector3 normal = new Vector3(0, 0, -1);
            float[] speeds = { 300.0f, 600.0f, 950.0f };
            float[] thicknesses = { 0.01f, 0.05f, 0.20f, 0.60f };
            float[] anglesDeg = { 0.0f, 30.0f, 60.0f, 80.0f, 88.0f };

            foreach (var mat in materials)
            {
                foreach (float speed in speeds)
                {
                    foreach (float thickness in thicknesses)
                    {
                        foreach (float angleDeg in anglesDeg)
                        {
                            float rad = angleDeg * MathF.PI / 180.0f;
                            var proj = new ProjectileState
                            {
                                Position = Vector3.Zero,
                                Velocity = new Vector3(MathF.Sin(rad) * speed, 0, MathF.Cos(rad) * speed),
                                Time = 0.0f
                            };

                            var result = _penetrationSystem.CalculatePenetration(proj, profile, mat, thickness, normal);

                            float totalEnergy = result.RemainingKineticEnergy + result.TransferredKineticEnergy;
                            Assert.True(
                                MathF.Abs(result.InitialKineticEnergy - totalEnergy) < 1e-3f,
                                $"Energy not conserved for material {mat.Name}, speed {speed}, angle {angleDeg} deg: E0={result.InitialKineticEnergy}, Rem+Trans={totalEnergy}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void Penetration_ThinBarrier_PerforatesWithCorrectExitEnergy()
        {
            float mass = 0.004f; // 4 grams
            float speed = 800.0f; // 800 m/s
            float area = 0.00005f; // 5e-5 m^2

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = mass,
                CrossSectionalArea = area,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            float thickness = 0.01f; // 1 cm
            Vector3 normal = new Vector3(0, 0, -1);

            // Manual calculation:
            // Ek0 = 0.5 * 0.004 * 800^2 = 1280 J
            // Fdrag = 0.5 * 600 * 1.0 * 5e-5 * 800^2 = 9600 N
            // DeltaE = 9600 * 0.01 = 96 J
            // Erem = 1280 - 96 = 1184 J
            // vexit = sqrt(2 * 1184 / 0.004) = sqrt(592000) ~= 769.415 m/s

            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, thickness, normal);

            Assert.Equal(PenetrationOutcome.Perforated, result.Outcome);
            Assert.Equal(1280.0f, result.InitialKineticEnergy, 2);
            Assert.Equal(96.0f, result.TransferredKineticEnergy, 2);
            Assert.Equal(1184.0f, result.RemainingKineticEnergy, 2);
            Assert.Equal(769.415f, result.ExitVelocity, 1);
            Assert.Equal(new Vector3(0, 0, 769.415f).Z, result.ExitVelocityVector.Z, 1);
            Assert.Equal(0.01f, result.ExitPoint.Z, 3);
            Assert.Equal(0.01f, result.ExitState.Position.Z, 3);
        }

        [Fact]
        public void Penetration_ThickBarrier_StopsProjectile()
        {
            float mass = 0.008f;
            float speed = 350.0f; // Pistol round
            float area = 0.000065f;

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = mass,
                CrossSectionalArea = area,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            float thickBarrier = 0.50f; // 50 cm armor steel
            Vector3 normal = new Vector3(0, 0, -1);

            var result = _penetrationSystem.CalculatePenetration(proj, profile, steel, thickBarrier, normal);

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0.0f, result.ExitVelocity);
            Assert.Equal(Vector3.Zero, result.ExitVelocityVector);
            Assert.Equal(0.0f, result.RemainingKineticEnergy);
            Assert.Equal(result.InitialKineticEnergy, result.TransferredKineticEnergy);
            Assert.Equal(Vector3.Zero, result.ExitState.Velocity);
        }

        [Fact]
        public void Penetration_HighObliquity_TriggersRicochet()
        {
            float mass = 0.004f;
            float speed = 900.0f;
            var steel = _registry.GetMaterial(MaterialType.Steel); // Ricochet threshold = 1.22 rad (70 deg)

            // Impact at 80 degrees (~1.396 rad)
            float angleRad = 80.0f * MathF.PI / 180.0f;
            Vector3 dir = new Vector3(MathF.Sin(angleRad), 0, MathF.Cos(angleRad));

            var proj = new ProjectileState
            {
                Position = new Vector3(0, 0, 5.0f),
                Velocity = dir * speed,
                Time = 1.2f
            };

            var profile = new BallisticProfile
            {
                Mass = mass,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            Vector3 normal = new Vector3(0, 0, -1);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.05f, normal);

            Assert.Equal(PenetrationOutcome.Ricochet, result.Outcome);
            Assert.True(result.AngleOfIncidence >= steel.RicochetAngleThreshold);
            Assert.Equal(proj.Position, result.EntryPoint);
            Assert.Equal(proj.Position, result.ExitPoint);

            // Reflected direction: Z component was positive (+cos 80), after reflection off -Z normal it should be negative (-cos 80)
            Assert.True(result.ExitVelocityVector.Z < 0, "Reflected velocity vector should point away from the barrier face.");
            Assert.True(result.ExitVelocityVector.X > 0, "Reflected velocity vector X component should be preserved.");

            // Energy calculation for ricochet: Eloss = Ek0 * (1 - sin theta) * 0.3
            float ek0 = 0.5f * mass * speed * speed;
            float expectedLoss = ek0 * (1.0f - MathF.Sin(angleRad)) * 0.3f;
            float expectedRem = ek0 - expectedLoss;

            Assert.Equal(expectedRem, result.RemainingKineticEnergy, 2);
            Assert.Equal(expectedLoss, result.TransferredKineticEnergy, 2);
        }

        [Fact]
        public void CalculatePenetration_ExplicitCoordinates_CalculatesCorrectly()
        {
            var proj = new ProjectileState
            {
                Position = new Vector3(10, 20, 30),
                Velocity = new Vector3(0, 0, 800.0f),
                Time = 0.5f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            Vector3 entry = new Vector3(10, 20, 30);
            Vector3 exit = new Vector3(10, 20, 30.04f); // 4 cm distance
            Vector3 normal = new Vector3(0, 0, -1);

            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, entry, exit, normal);

            Assert.Equal(PenetrationOutcome.Perforated, result.Outcome);
            Assert.Equal(0.04f, result.EffectiveThickness, 4);
            Assert.Equal(entry, result.EntryPoint);
            Assert.Equal(exit, result.ExitPoint);
            Assert.Equal(exit, result.ExitState.Position);
            Assert.True(result.ExitVelocity > 0f);
        }

        [Fact]
        public void Penetration_BelowYieldThreshold_IsStopped()
        {
            // Low energy round (e.g. 50 J) striking Steel with Yield threshold of 500 J
            float mass = 0.004f;
            float speed = 100.0f; // Ek0 = 0.5 * 0.004 * 10000 = 20 J (< 500 J)

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed),
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = mass,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var steel = _registry.GetMaterial(MaterialType.Steel);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, steel, 0.001f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0.0f, result.ExitVelocity);
            Assert.Equal(20.0f, result.TransferredKineticEnergy, 2);
            Assert.Equal(0.0f, result.RemainingKineticEnergy);
        }

        [Fact]
        public void Penetration_ZeroVelocity_HandledSafely()
        {
            var proj = new ProjectileState
            {
                Position = new Vector3(1, 2, 3),
                Velocity = Vector3.Zero,
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.01f,
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var result = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.05f, new Vector3(0, 0, -1));

            Assert.Equal(PenetrationOutcome.Stopped, result.Outcome);
            Assert.Equal(0f, result.InitialVelocity);
            Assert.Equal(0f, result.ExitVelocity);
            Assert.Equal(0f, result.InitialKineticEnergy);
            Assert.Equal(0f, result.RemainingKineticEnergy);
            Assert.Equal(0f, result.TransferredKineticEnergy);
        }

        [Fact]
        public void Penetration_InwardAndOutwardNormals_YieldIdenticalAngleOfIncidence()
        {
            float speed = 800.0f;
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, speed), // Moving in +Z
                Time = 0.0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var wood = _registry.GetMaterial(MaterialType.Wood);
            var resultOutward = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, new Vector3(0, 0, -1));
            var resultInward = _penetrationSystem.CalculatePenetration(proj, profile, wood, 0.02f, new Vector3(0, 0, 1));

            Assert.Equal(resultOutward.AngleOfIncidence, resultInward.AngleOfIncidence, 4);
            Assert.Equal(resultOutward.EffectiveThickness, resultInward.EffectiveThickness, 4);
            Assert.Equal(resultOutward.ExitVelocity, resultInward.ExitVelocity, 2);
        }

        [Fact]
        public async System.Threading.Tasks.Task MaterialRegistry_ThreadSafety_ConcurrentReadsAndWrites()
        {
            var registry = new MaterialRegistry();
            var tasks = new System.Threading.Tasks.Task[20];

            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    // Register dynamic custom material
                    registry.RegisterMaterial(new MaterialProperties(
                        $"Custom_{index}",
                        MaterialType.Custom,
                        1000f + index,
                        1.0f + index * 0.1f,
                        1.4f,
                        50f));

                    // Read standard material
                    var wood = registry.GetMaterial(MaterialType.Wood);
                    Assert.Equal(600.0f, wood.Density);

                    // Read just registered material
                    Assert.True(registry.TryGetMaterial($"Custom_{index}", out var custom));
                    Assert.Equal(1000f + index, custom.Density);
                });
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        #region Adversarial Empirical Stress Tests

        [Fact]
        public void Penetration_10000RandomizedInvariantFuzz_ConservesEnergyAndNeverProducesNaN()
        {
            var rng = new Random(42);
            var system = new MaterialPenetrationSystem();

            for (int i = 0; i < 10000; i++)
            {
                // Generate random physical parameters
                float mass = (float)(0.001 + rng.NextDouble() * 10.0); // 1g to 10kg
                float area = (float)(1e-6 + rng.NextDouble() * 0.05);  // 1e-6 to 0.05 m^2
                float speed = (float)(0.001 + rng.NextDouble() * 4000.0); // 0.001 to 4000 m/s
                
                // Random 3D direction
                Vector3 dir = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));
                if (dir.LengthSquared() < 1e-6f) dir = Vector3.UnitZ;

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

                var material = new MaterialProperties(
                    $"FuzzMat_{i}",
                    MaterialType.Custom,
                    density: (float)(0.1 + rng.NextDouble() * 20000.0),
                    resistanceCoefficient: (float)(0.01 + rng.NextDouble() * 10.0),
                    ricochetAngleThreshold: (float)(0.2 + rng.NextDouble() * 1.35),
                    yieldEnergyThreshold: (float)(rng.NextDouble() * 5000.0));

                float thickness = (float)(0.0001 + rng.NextDouble() * 5.0); // 0.1mm to 5m

                // Random normal (can be inward, outward, or arbitrary)
                Vector3 normal = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)));
                if (normal.LengthSquared() < 1e-6f) normal = Vector3.UnitY;

                var result = system.CalculatePenetration(proj, profile, material, thickness, normal);

                // 1. NaN and Infinity assertions
                Assert.False(float.IsNaN(result.ExitVelocity), $"Iteration {i}: ExitVelocity is NaN");
                Assert.False(float.IsInfinity(result.ExitVelocity), $"Iteration {i}: ExitVelocity is Infinity");
                Assert.False(float.IsNaN(result.RemainingKineticEnergy), $"Iteration {i}: RemainingKineticEnergy is NaN");
                Assert.False(float.IsNaN(result.TransferredKineticEnergy), $"Iteration {i}: TransferredKineticEnergy is NaN");
                Assert.False(float.IsNaN(result.InitialKineticEnergy), $"Iteration {i}: InitialKineticEnergy is NaN");
                Assert.False(float.IsNaN(result.EffectiveThickness), $"Iteration {i}: EffectiveThickness is NaN");
                Assert.False(float.IsNaN(result.AngleOfIncidence), $"Iteration {i}: AngleOfIncidence is NaN");
                Assert.False(float.IsNaN(result.ExitVelocityVector.X) || float.IsNaN(result.ExitVelocityVector.Y) || float.IsNaN(result.ExitVelocityVector.Z),
                    $"Iteration {i}: ExitVelocityVector has NaN components");

                // 2. Physical Invariants
                Assert.True(result.InitialKineticEnergy >= 0f, $"Iteration {i}: Negative InitialKineticEnergy");
                Assert.True(result.RemainingKineticEnergy >= 0f, $"Iteration {i}: Negative RemainingKineticEnergy");
                Assert.True(result.TransferredKineticEnergy >= 0f, $"Iteration {i}: Negative TransferredKineticEnergy");
                Assert.True(result.ExitVelocity >= 0f, $"Iteration {i}: Negative ExitVelocity");
                Assert.True(result.ExitVelocity <= speed + 1e-3f, $"Iteration {i}: ExitVelocity ({result.ExitVelocity}) > InitialVelocity ({speed})");

                // 3. Strict Energy Conservation Invariant: E_k0 == E_rem + E_transferred
                float totalKinetic = result.RemainingKineticEnergy + result.TransferredKineticEnergy;
                float allowedDelta = MathF.Max(1e-3f, result.InitialKineticEnergy * 1e-5f);
                Assert.True(
                    MathF.Abs(result.InitialKineticEnergy - totalKinetic) <= allowedDelta,
                    $"Iteration {i}: Energy not conserved. E0={result.InitialKineticEnergy}, Rem+Trans={totalKinetic}, Diff={MathF.Abs(result.InitialKineticEnergy - totalKinetic)}");

                // 4. Kinematic consistency
                float vectorSpeed = result.ExitVelocityVector.Length();
                Assert.True(
                    MathF.Abs(vectorSpeed - result.ExitVelocity) <= 1e-3f,
                    $"Iteration {i}: Vector speed ({vectorSpeed}) != scalar ExitVelocity ({result.ExitVelocity})");
            }
        }

        [Fact]
        public void Penetration_DragRetardation_ContinuousMonotonicityAcrossDensitiesAndThicknesses()
        {
            var system = new MaterialPenetrationSystem();
            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900.0f),
                Time = 0.0f
            };
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };
            Vector3 normal = new Vector3(0, 0, -1);

            // 1. Density sweep: 50 kg/m^3 to 15,000 kg/m^3 in 150 steps (fixed thickness 0.05m)
            float fixedThickness = 0.05f;
            float prevExitVelocity = float.MaxValue;
            float prevTransferredEnergy = -1f;

            for (float density = 50f; density <= 15000f; density += 100f)
            {
                var mat = new MaterialProperties(
                    "SweepMat",
                    MaterialType.Custom,
                    density: density,
                    resistanceCoefficient: 1.5f,
                    ricochetAngleThreshold: 1.5f,
                    yieldEnergyThreshold: 50f);

                var res = system.CalculatePenetration(proj, profile, mat, fixedThickness, normal);

                Assert.True(res.ExitVelocity <= prevExitVelocity + 1e-5f,
                    $"Density Monotonicity Failure: Density {density} gave exit velocity {res.ExitVelocity} > previous {prevExitVelocity}");
                Assert.True(res.TransferredKineticEnergy >= prevTransferredEnergy - 1e-5f,
                    $"Density Energy Monotonicity Failure: Density {density} transferred {res.TransferredKineticEnergy} < previous {prevTransferredEnergy}");

                prevExitVelocity = res.ExitVelocity;
                prevTransferredEnergy = res.TransferredKineticEnergy;
            }

            // 2. Thickness sweep: 0.001m to 0.50m in 200 steps (fixed density 2400 kg/m^3)
            var concrete = new MaterialProperties(
                "ConcreteSweep",
                MaterialType.Concrete,
                density: 2400f,
                resistanceCoefficient: 1.8f,
                ricochetAngleThreshold: 1.31f,
                yieldEnergyThreshold: 200f);

            prevExitVelocity = float.MaxValue;
            prevTransferredEnergy = -1f;

            for (float thickness = 0.001f; thickness <= 0.50f; thickness += 0.0025f)
            {
                var res = system.CalculatePenetration(proj, profile, concrete, thickness, normal);

                Assert.True(res.ExitVelocity <= prevExitVelocity + 1e-5f,
                    $"Thickness Monotonicity Failure: Thickness {thickness} gave exit velocity {res.ExitVelocity} > previous {prevExitVelocity}");
                Assert.True(res.TransferredKineticEnergy >= prevTransferredEnergy - 1e-5f,
                    $"Thickness Energy Monotonicity Failure: Thickness {thickness} transferred {res.TransferredKineticEnergy} < previous {prevTransferredEnergy}");

                prevExitVelocity = res.ExitVelocity;
                prevTransferredEnergy = res.TransferredKineticEnergy;
            }
        }

        [Fact]
        public void Penetration_SingularityAndNumericalStability_EdgeCases()
        {
            var system = new MaterialPenetrationSystem();
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };
            var wood = new MaterialProperties("Wood", MaterialType.Wood, 600f, 1.0f, 1.48f, 50f);

            // A. Near-zero velocities
            float[] nearZeroSpeeds = { 0f, 1e-12f, 1e-9f, 1e-6f, 1e-5f };
            foreach (float v in nearZeroSpeeds)
            {
                var proj = new ProjectileState { Velocity = new Vector3(0, 0, v) };
                var res = system.CalculatePenetration(proj, profile, wood, 0.05f, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsNaN(res.RemainingKineticEnergy));
                Assert.False(float.IsNaN(res.TransferredKineticEnergy));
                Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
            }

            // B. Zero and negative thicknesses
            float[] edgeThicknesses = { 0f, -0.01f, -100f, 1e-12f, 1e-6f };
            foreach (float t in edgeThicknesses)
            {
                var proj = new ProjectileState { Velocity = new Vector3(0, 0, 800f) };
                var res = system.CalculatePenetration(proj, profile, wood, t, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsNaN(res.EffectiveThickness));
                Assert.True(res.EffectiveThickness >= 0f);
                if (t <= 0f)
                {
                    Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                    Assert.Equal(800f, res.ExitVelocity, 3);
                    Assert.Equal(0f, res.TransferredKineticEnergy, 3);
                }
            }

            // C. Near-90 degree glancing angles (cosine -> 0)
            float speed = 800f;
            float[] anglesNear90 = { 89.0f, 89.9f, 89.99f, 89.999f, 90.0f };
            foreach (float deg in anglesNear90)
            {
                float rad = deg * MathF.PI / 180f;
                var proj = new ProjectileState
                {
                    Velocity = new Vector3(MathF.Sin(rad) * speed, 0, MathF.Cos(rad) * speed)
                };
                var res = system.CalculatePenetration(proj, profile, wood, 0.05f, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsNaN(res.EffectiveThickness));
                Assert.False(float.IsInfinity(res.EffectiveThickness));
                Assert.False(float.IsNaN(res.AngleOfIncidence));
                Assert.True(res.AngleOfIncidence <= MathF.PI / 2.0f + 1e-4f);
            }

            // D. Degenerate Normal Vectors (Zero vector, inverted vector, parallel, perpendicular)
            var projNormal = new ProjectileState { Velocity = new Vector3(0, 0, 800f) };
            
            // Zero normal
            var resZeroNormal = system.CalculatePenetration(projNormal, profile, wood, 0.05f, Vector3.Zero);
            Assert.False(float.IsNaN(resZeroNormal.ExitVelocity));
            Assert.Equal(0f, resZeroNormal.AngleOfIncidence, 3); // Handled safely via fallback normal

            // Inverted normal (pointing +Z instead of -Z)
            var resInverted = system.CalculatePenetration(projNormal, profile, wood, 0.05f, new Vector3(0, 0, 1));
            var resDirect = system.CalculatePenetration(projNormal, profile, wood, 0.05f, new Vector3(0, 0, -1));
            Assert.Equal(resDirect.ExitVelocity, resInverted.ExitVelocity, 3);
            Assert.Equal(resDirect.TransferredKineticEnergy, resInverted.TransferredKineticEnergy, 3);
            Assert.Equal(resDirect.AngleOfIncidence, resInverted.AngleOfIncidence, 3);

            // Perpendicular normal (grazing 90 deg)
            var resPerp = system.CalculatePenetration(projNormal, profile, wood, 0.05f, new Vector3(1, 0, 0));
            Assert.False(float.IsNaN(resPerp.ExitVelocity));

            // E. Hyper-velocity projectile (100,000 m/s) and microscopic projectile (1e-8 kg)
            var hyperProj = new ProjectileState { Velocity = new Vector3(0, 0, 100000f) };
            var hyperProfile = new BallisticProfile { Mass = 1e-8f, CrossSectionalArea = 1e-10f, DragModel = new StandardDragCurve(0.3f) };
            var resHyper = system.CalculatePenetration(hyperProj, hyperProfile, wood, 0.05f, new Vector3(0, 0, -1));
            Assert.False(float.IsNaN(resHyper.ExitVelocity));
            Assert.False(float.IsInfinity(resHyper.ExitVelocity));
            Assert.Equal(resHyper.InitialKineticEnergy, resHyper.RemainingKineticEnergy + resHyper.TransferredKineticEnergy, 1);
        }

        [Fact]
        public void Penetration_Ricochet_ReflectionSymmetryAndEnergyDamping()
        {
            var system = new MaterialPenetrationSystem();
            var steel = new MaterialProperties("Steel", MaterialType.Steel, 7850f, 2.5f, 1.22f, 500f); // 1.22 rad ~= 69.9 deg
            float speed = 900.0f;
            float mass = 0.004f;
            var profile = new BallisticProfile
            {
                Mass = mass,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Test across multiple angles in ricochet regime (72 deg to 88 deg)
            for (float angleDeg = 72f; angleDeg <= 88f; angleDeg += 2f)
            {
                float angleRad = angleDeg * MathF.PI / 180f;
                // Incoming vector in XZ plane: (sin theta, 0, cos theta)
                Vector3 inDir = new Vector3(MathF.Sin(angleRad), 0, MathF.Cos(angleRad));
                var proj = new ProjectileState
                {
                    Position = new Vector3(0, 0, 10f),
                    Velocity = inDir * speed,
                    Time = 0.5f
                };

                Vector3 normal = new Vector3(0, 0, -1); // Surface face normal pointing -Z
                var res = system.CalculatePenetration(proj, profile, steel, 0.10f, normal);

                Assert.Equal(PenetrationOutcome.Ricochet, res.Outcome);
                Assert.Equal(angleRad, res.AngleOfIncidence, 3);

                // 1. Reflected direction unit vector
                Vector3 outDir = Vector3.Normalize(res.ExitVelocityVector);
                
                // Incident angle relative to barrier plane: inDir.X > 0, inDir.Z > 0.
                // Reflected angle: outDir.X should be positive, outDir.Z should be negative (bounced back in -Z).
                Assert.True(outDir.Z < -1e-4f, $"Reflected Z component should be negative (bounced off barrier face). Angle {angleDeg}");
                Assert.True(outDir.X > 1e-4f, $"Reflected X component should remain positive. Angle {angleDeg}");

                // Specular reflection law: angle between outDir and barrier normal (-Z) should equal angle of incidence
                float reflDot = MathF.Abs(Vector3.Dot(outDir, normal));
                float reflAngle = MathF.Acos(Math.Clamp(reflDot, 0f, 1f));
                Assert.Equal(angleRad, reflAngle, 3);

                // 2. Energy Damping check: E_loss = E_k0 * (1 - sin(theta)) * 0.3
                float ek0 = 0.5f * mass * speed * speed;
                float expectedLoss = ek0 * (1.0f - MathF.Sin(angleRad)) * 0.3f;
                float expectedRem = ek0 - expectedLoss;

                Assert.Equal(expectedRem, res.RemainingKineticEnergy, 2);
                Assert.Equal(expectedLoss, res.TransferredKineticEnergy, 2);
                Assert.Equal(ek0, res.RemainingKineticEnergy + res.TransferredKineticEnergy, 2);

                // 3. As angle approaches 90 deg (grazing), energy loss approaches 0
                if (angleDeg >= 88f)
                {
                    Assert.True(res.RemainingKineticEnergy > 0.99f * ek0,
                        $"Near-grazing ricochet should retain >99% kinetic energy, got {res.RemainingKineticEnergy}/{ek0}");
                }
            }
        }

        [Fact]
        public void MaterialRegistry_AdversarialLookups_InvalidInputsAndExceptions()
        {
            var registry = new MaterialRegistry();

            // Null, empty, whitespace lookups
            Assert.False(registry.TryGetMaterial("", out _));
            Assert.False(registry.TryGetMaterial("   ", out _));
            Assert.False(registry.TryGetMaterial(null!, out _));

            Assert.Throws<KeyNotFoundException>(() => registry.GetMaterial(""));
            Assert.Throws<KeyNotFoundException>(() => registry.GetMaterial("   "));
            Assert.Throws<KeyNotFoundException>(() => registry.GetMaterial("Unknown_Material_X"));
            Assert.Throws<KeyNotFoundException>(() => registry.GetMaterial((MaterialType)9999));

            // Registration validation
            Assert.Throws<ArgumentException>(() => registry.RegisterMaterial(new MaterialProperties("", MaterialType.Custom, 100f, 1f, 1f, 1f)));
            Assert.Throws<ArgumentException>(() => registry.RegisterMaterial(new MaterialProperties("   ", MaterialType.Custom, 100f, 1f, 1f, 1f)));
            Assert.Throws<ArgumentException>(() => registry.RegisterMaterial(new MaterialProperties(null!, MaterialType.Custom, 100f, 1f, 1f, 1f)));
        }

        [Fact]
        public void Penetration_ZeroOrNegativeThickness_PassesThroughUnimpeded()
        {
            var system = new MaterialPenetrationSystem();
            var wood = _registry.GetMaterial(MaterialType.Wood);
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = new Vector3(10, 20, 30),
                Velocity = new Vector3(0, 0, 850.0f),
                Time = 1.0f
            };

            // 1. Slab overload with zero nominal thickness
            var resZero = system.CalculatePenetration(proj, profile, wood, 0f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resZero.Outcome);
            Assert.Equal(850.0f, resZero.ExitVelocity);
            Assert.Equal(resZero.InitialKineticEnergy, resZero.RemainingKineticEnergy);
            Assert.Equal(0f, resZero.TransferredKineticEnergy);
            Assert.Equal(proj.Velocity, resZero.ExitVelocityVector);
            Assert.Equal(proj.Position, resZero.ExitPoint);
            Assert.Equal(proj.Position, resZero.ExitState.Position);
            Assert.Equal(proj.Velocity, resZero.ExitState.Velocity);

            // 2. Slab overload with negative nominal thickness
            var resNeg = system.CalculatePenetration(proj, profile, wood, -0.05f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resNeg.Outcome);
            Assert.Equal(850.0f, resNeg.ExitVelocity);
            Assert.Equal(resNeg.InitialKineticEnergy, resNeg.RemainingKineticEnergy);
            Assert.Equal(0f, resNeg.TransferredKineticEnergy);

            // 3. Explicit coordinates overload with coincident entry & exit points
            var resCoincident = system.CalculatePenetration(proj, profile, wood, proj.Position, proj.Position, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resCoincident.Outcome);
            Assert.Equal(850.0f, resCoincident.ExitVelocity);
            Assert.Equal(resCoincident.InitialKineticEnergy, resCoincident.RemainingKineticEnergy);
            Assert.Equal(0f, resCoincident.TransferredKineticEnergy);
            Assert.Equal(proj.Position, resCoincident.ExitPoint);
            Assert.Equal(proj.Position, resCoincident.ExitState.Position);
        }

        #endregion
    }
}
