using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Tests
{
    public class MaterialPenetrationEmpiricalChallengerTests
    {
        private readonly IMaterialPenetrationSystem _penetrationSystem;
        private readonly IMaterialRegistry _registry;

        public MaterialPenetrationEmpiricalChallengerTests()
        {
            _penetrationSystem = new MaterialPenetrationSystem();
            _registry = new MaterialRegistry();
        }

        #region Task 1.1: Zero and Negative Thickness Across Velocity Regimes

        [Fact]
        public void ZeroAndNegativeThickness_PlanarOverload_MatrixVerification()
        {
            var wood = _registry.GetMaterial(MaterialType.Wood);
            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.00005f,
                DragModel = new StandardDragCurve(0.3f)
            };

            float[] testThicknesses = { 0.0f, -1e-8f, -0.01f, -1.0f, -500.0f };
            float[] stationarySpeeds = { 0.0f, 1e-15f, 1e-9f, 9.99e-7f };
            float[] activeSpeeds = { 1.001e-6f, 1e-5f, 0.1f, 1.0f, 300.0f, 850.0f, 4000.0f };

            Vector3[] normals =
            {
                new Vector3(0, 0, -1),
                new Vector3(0, 0, 1),
                new Vector3(1, 0, 0),
                Vector3.Zero,
                Vector3.Normalize(new Vector3(1, 2, 3))
            };

            // 1. Stationary / Near-zero speed with non-positive thickness -> MUST BE STOPPED
            foreach (float t in testThicknesses)
            {
                foreach (float v in stationarySpeeds)
                {
                    foreach (var normal in normals)
                    {
                        var proj = new ProjectileState
                        {
                            Position = new Vector3(1, 2, 3),
                            Velocity = new Vector3(0, 0, v),
                            Time = 0.1f
                        };

                        var res = _penetrationSystem.CalculatePenetration(proj, profile, wood, t, normal);

                        Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
                        Assert.Equal(0.0f, res.ExitVelocity);
                        Assert.Equal(Vector3.Zero, res.ExitVelocityVector);
                        Assert.Equal(0.0f, res.RemainingKineticEnergy);
                        Assert.Equal(res.InitialKineticEnergy, res.TransferredKineticEnergy);
                        Assert.False(float.IsNaN(res.ExitVelocity));
                        Assert.False(float.IsNaN(res.InitialKineticEnergy));
                    }
                }
            }

            // 2. Active speed with non-positive thickness -> MUST PERFORATE UNIMPEDED
            foreach (float t in testThicknesses)
            {
                foreach (float v in activeSpeeds)
                {
                    foreach (var normal in normals)
                    {
                        Vector3 velocity = new Vector3(0, 0, v);
                        var proj = new ProjectileState
                        {
                            Position = new Vector3(10, 20, 30),
                            Velocity = velocity,
                            Time = 0.5f
                        };

                        var res = _penetrationSystem.CalculatePenetration(proj, profile, wood, t, normal);

                        Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                        Assert.Equal(v, res.ExitVelocity, 4);
                        Assert.Equal(velocity, res.ExitVelocityVector);
                        Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy, 4);
                        Assert.Equal(0.0f, res.TransferredKineticEnergy, 4);
                        Assert.Equal(0.0f, res.EffectiveThickness);
                        Assert.Equal(proj.Position, res.EntryPoint);
                        Assert.Equal(proj.Position, res.ExitPoint);
                        Assert.Equal(proj.Position, res.ExitState.Position);
                        Assert.Equal(velocity, res.ExitState.Velocity);
                    }
                }
            }
        }

        [Fact]
        public void ZeroAndNegativeThickness_ExplicitCoordinatesOverload_MatrixVerification()
        {
            var steel = _registry.GetMaterial(MaterialType.Steel);
            var profile = new BallisticProfile
            {
                Mass = 0.010f,
                CrossSectionalArea = 0.00006f,
                DragModel = new StandardDragCurve(0.3f)
            };

            Vector3 entry = new Vector3(12.5f, -3.2f, 44.0f);
            Vector3 exit = entry; // Distance = 0

            float[] stationarySpeeds = { 0.0f, 1e-12f, 9.99e-7f };
            float[] activeSpeeds = { 1.001e-6f, 0.5f, 750.0f, 3000.0f };

            // 1. Stationary with coincident points -> Stopped
            foreach (float v in stationarySpeeds)
            {
                var proj = new ProjectileState
                {
                    Position = entry,
                    Velocity = new Vector3(0, 0, v),
                    Time = 1.0f
                };

                var res = _penetrationSystem.CalculatePenetration(proj, profile, steel, entry, exit, new Vector3(0, 0, -1));

                Assert.Equal(PenetrationOutcome.Stopped, res.Outcome);
                Assert.Equal(0.0f, res.ExitVelocity);
                Assert.Equal(Vector3.Zero, res.ExitVelocityVector);
                Assert.Equal(0.0f, res.RemainingKineticEnergy);
                Assert.Equal(res.InitialKineticEnergy, res.TransferredKineticEnergy);
            }

            // 2. Active speed with coincident points -> Perforated unimpeded
            foreach (float v in activeSpeeds)
            {
                Vector3 velocity = new Vector3(0, 0, v);
                var proj = new ProjectileState
                {
                    Position = entry,
                    Velocity = velocity,
                    Time = 1.0f
                };

                var res = _penetrationSystem.CalculatePenetration(proj, profile, steel, entry, exit, new Vector3(0, 0, -1));

                Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                Assert.Equal(v, res.ExitVelocity, 4);
                Assert.Equal(velocity, res.ExitVelocityVector);
                Assert.Equal(res.InitialKineticEnergy, res.RemainingKineticEnergy, 4);
                Assert.Equal(0.0f, res.TransferredKineticEnergy, 4);
                Assert.Equal(0.0f, res.EffectiveThickness);
                Assert.Equal(entry, res.EntryPoint);
                Assert.Equal(exit, res.ExitPoint);
                Assert.Equal(exit, res.ExitState.Position);
                Assert.Equal(velocity, res.ExitState.Velocity);
            }
        }

        #endregion

        #region Task 1.2: 10,000 Randomized Energy Conservation Trials

        [Fact]
        public void TenThousandRandomizedTrials_EnergyConservationAndInvariants()
        {
            var rng = new Random(1337_2026);
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

            const int totalTrials = 10_000;
            int perforatedCount = 0;
            int stoppedCount = 0;
            int ricochetCount = 0;

            for (int i = 0; i < totalTrials; i++)
            {
                // Generate varied parameters
                float mass = MathF.Pow(10f, (float)(rng.NextDouble() * 7.0 - 5.0)); // 1e-5 kg (10mg) to 100 kg
                float area = MathF.Pow(10f, (float)(rng.NextDouble() * 6.0 - 7.0)); // 1e-7 m^2 to 0.1 m^2
                float speed = (float)(rng.NextDouble() * 5000.0 + 1e-7); // up to 5000 m/s
                float thickness = (float)(rng.NextDouble() * 4.0 - 0.5); // -0.5m to 3.5m (tests negative and positive)

                // Random 3D direction vector
                Vector3 dir = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (dir.LengthSquared() < 1e-6f) dir = Vector3.UnitZ;
                dir = Vector3.Normalize(dir);

                // Random normal vector
                Vector3 normal = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (normal.LengthSquared() < 1e-6f) normal = -Vector3.UnitZ;
                normal = Vector3.Normalize(normal);

                var proj = new ProjectileState
                {
                    Position = new Vector3((float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f),
                    Velocity = dir * speed,
                    Time = (float)rng.NextDouble() * 10f
                };

                var profile = new BallisticProfile
                {
                    Mass = mass,
                    CrossSectionalArea = area,
                    DragModel = new StandardDragCurve(0.3f)
                };

                // Also occasionally test dynamically randomized custom materials
                MaterialProperties mat;
                if (rng.Next(5) == 0)
                {
                    mat = new MaterialProperties(
                        $"Fuzz_Mat_{i}",
                        MaterialType.Custom,
                        density: (float)(rng.NextDouble() * 15000.0 + 1.0),
                        resistanceCoefficient: (float)(rng.NextDouble() * 20.0),
                        ricochetAngleThreshold: (float)(rng.NextDouble() * 1.5 + 0.1),
                        yieldEnergyThreshold: (float)(rng.NextDouble() * 2000.0));
                }
                else
                {
                    mat = materials[rng.Next(materials.Length)];
                }

                // Randomly choose overload
                PenetrationResult result;
                if (rng.Next(2) == 0)
                {
                    result = _penetrationSystem.CalculatePenetration(proj, profile, mat, thickness, normal);
                }
                else
                {
                    float pathDist = MathF.Max(0f, thickness);
                    Vector3 exitPt = proj.Position + dir * pathDist;
                    result = _penetrationSystem.CalculatePenetration(proj, profile, mat, proj.Position, exitPt, normal);
                }

                // 1. Invariant: No NaN or Infinity in any output field
                Assert.False(float.IsNaN(result.ExitVelocity), $"Trial {i}: NaN ExitVelocity");
                Assert.False(float.IsInfinity(result.ExitVelocity), $"Trial {i}: Inf ExitVelocity");
                Assert.False(float.IsNaN(result.InitialKineticEnergy), $"Trial {i}: NaN InitialKineticEnergy");
                Assert.False(float.IsNaN(result.RemainingKineticEnergy), $"Trial {i}: NaN RemainingKineticEnergy");
                Assert.False(float.IsNaN(result.TransferredKineticEnergy), $"Trial {i}: NaN TransferredKineticEnergy");
                Assert.False(float.IsNaN(result.EffectiveThickness), $"Trial {i}: NaN EffectiveThickness");
                Assert.False(float.IsNaN(result.AngleOfIncidence), $"Trial {i}: NaN AngleOfIncidence");
                Assert.False(float.IsNaN(result.ExitVelocityVector.X) || float.IsNaN(result.ExitVelocityVector.Y) || float.IsNaN(result.ExitVelocityVector.Z), $"Trial {i}: NaN ExitVelocityVector");

                // 2. Invariant: Energy conservation Ek0 == E_rem + E_trans
                float eSum = result.RemainingKineticEnergy + result.TransferredKineticEnergy;
                float eAbsDiff = MathF.Abs(result.InitialKineticEnergy - eSum);
                float eRelDiff = eAbsDiff / MathF.Max(result.InitialKineticEnergy, 1e-4f);
                Assert.True(eRelDiff < 1e-3f || eAbsDiff < 1e-4f,
                    $"Trial {i}: Energy not conserved! Ek0={result.InitialKineticEnergy}, Erem={result.RemainingKineticEnergy}, Etrans={result.TransferredKineticEnergy}, diff={eAbsDiff}, relDiff={eRelDiff}");

                // 3. Invariant: No energy / velocity amplification
                Assert.True(result.ExitVelocity <= speed + 1e-3f, $"Trial {i}: Exit velocity {result.ExitVelocity} > initial {speed}");
                Assert.True(result.RemainingKineticEnergy <= result.InitialKineticEnergy + 1e-3f, $"Trial {i}: Erem > Ek0");

                // 4. Invariant: Non-negative energies
                Assert.True(result.RemainingKineticEnergy >= -1e-5f, $"Trial {i}: Negative remaining energy {result.RemainingKineticEnergy}");
                Assert.True(result.TransferredKineticEnergy >= -1e-5f, $"Trial {i}: Negative transferred energy {result.TransferredKineticEnergy}");

                // 5. Outcome-specific invariants
                switch (result.Outcome)
                {
                    case PenetrationOutcome.Perforated:
                        perforatedCount++;
                        Assert.True(result.ExitVelocity > 0f);
                        Assert.True(result.RemainingKineticEnergy > 0f);
                        break;
                    case PenetrationOutcome.Stopped:
                        stoppedCount++;
                        Assert.Equal(0f, result.ExitVelocity);
                        Assert.Equal(0f, result.RemainingKineticEnergy);
                        Assert.Equal(Vector3.Zero, result.ExitVelocityVector);
                        Assert.Equal(Vector3.Zero, result.ExitState.Velocity);
                        break;
                    case PenetrationOutcome.Ricochet:
                        ricochetCount++;
                        Assert.True(result.AngleOfIncidence >= mat.RicochetAngleThreshold - 1e-4f);
                        Assert.True(result.ExitVelocity > 0f);
                        break;
                }
            }

            // Ensure our 10,000 trials explored all physical outcomes adequately
            Assert.True(perforatedCount > 1000, $"Insufficient perforated outcomes: {perforatedCount}");
            Assert.True(stoppedCount > 1000, $"Insufficient stopped outcomes: {stoppedCount}");
            Assert.True(ricochetCount > 500, $"Insufficient ricochet outcomes: {ricochetCount}");
        }

        #endregion

        #region Task 1.3: Monotonicity Checks Across Thickness, Density, and Resistance

        [Fact]
        public void Monotonicity_ThicknessSweep_DecreasingVelocityAndIncreasingEnergyTransfer()
        {
            var concrete = _registry.GetMaterial(MaterialType.Concrete);
            var profile = new BallisticProfile
            {
                Mass = 0.008f, // 8 grams
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900.0f),
                Time = 0.0f
            };

            float prevExitVelocity = float.MaxValue;
            float prevTransferredEnergy = -1.0f;
            float prevRemainingEnergy = float.MaxValue;

            // Sweep thickness from 0.0m to 0.5m in 500 steps
            for (int step = 0; step <= 500; step++)
            {
                float thickness = step * 0.001f; // 1mm increments
                var res = _penetrationSystem.CalculatePenetration(proj, profile, concrete, thickness, new Vector3(0, 0, -1));

                if (step == 0)
                {
                    Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                    Assert.Equal(900.0f, res.ExitVelocity, 3);
                    Assert.Equal(0.0f, res.TransferredKineticEnergy, 3);
                }

                // Monotonicity assertions:
                // As thickness increases, ExitVelocity must not increase
                Assert.True(res.ExitVelocity <= prevExitVelocity + 1e-4f,
                    $"Thickness {thickness}m: ExitVelocity {res.ExitVelocity} > prev {prevExitVelocity}");

                // As thickness increases, TransferredKineticEnergy must not decrease
                Assert.True(res.TransferredKineticEnergy >= prevTransferredEnergy - 1e-4f,
                    $"Thickness {thickness}m: TransferredEnergy {res.TransferredKineticEnergy} < prev {prevTransferredEnergy}");

                // As thickness increases, RemainingKineticEnergy must not increase
                Assert.True(res.RemainingKineticEnergy <= prevRemainingEnergy + 1e-4f,
                    $"Thickness {thickness}m: RemainingEnergy {res.RemainingKineticEnergy} > prev {prevRemainingEnergy}");

                prevExitVelocity = res.ExitVelocity;
                prevTransferredEnergy = res.TransferredKineticEnergy;
                prevRemainingEnergy = res.RemainingKineticEnergy;
            }
        }

        [Fact]
        public void Monotonicity_DensitySweep_DecreasingVelocityAndIncreasingEnergyTransfer()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 850.0f),
                Time = 0.0f
            };

            float prevExitVelocity = float.MaxValue;
            float prevTransferredEnergy = -1.0f;
            float prevRemainingEnergy = float.MaxValue;

            // Sweep density from 100 kg/m^3 to 15,000 kg/m^3 in 300 steps
            for (int step = 1; step <= 300; step++)
            {
                float density = step * 50.0f;
                var mat = new MaterialProperties(
                    $"DensitySweep_{step}",
                    MaterialType.Custom,
                    density: density,
                    resistanceCoefficient: 1.5f,
                    ricochetAngleThreshold: 1.57f,
                    yieldEnergyThreshold: 100f);

                var res = _penetrationSystem.CalculatePenetration(proj, profile, mat, 0.03f, new Vector3(0, 0, -1));

                Assert.True(res.ExitVelocity <= prevExitVelocity + 1e-4f,
                    $"Density {density} kg/m^3: ExitVelocity {res.ExitVelocity} > prev {prevExitVelocity}");

                Assert.True(res.TransferredKineticEnergy >= prevTransferredEnergy - 1e-4f,
                    $"Density {density} kg/m^3: TransferredEnergy {res.TransferredKineticEnergy} < prev {prevTransferredEnergy}");

                Assert.True(res.RemainingKineticEnergy <= prevRemainingEnergy + 1e-4f,
                    $"Density {density} kg/m^3: RemainingEnergy {res.RemainingKineticEnergy} > prev {prevRemainingEnergy}");

                prevExitVelocity = res.ExitVelocity;
                prevTransferredEnergy = res.TransferredKineticEnergy;
                prevRemainingEnergy = res.RemainingKineticEnergy;
            }
        }

        [Fact]
        public void Monotonicity_ResistanceCoefficientSweep_DecreasingVelocity()
        {
            var profile = new BallisticProfile
            {
                Mass = 0.008f,
                CrossSectionalArea = 0.000045f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 850.0f),
                Time = 0.0f
            };

            float prevExitVelocity = float.MaxValue;
            float prevTransferredEnergy = -1.0f;

            // Sweep Cr from 0.0 to 10.0 in 200 steps
            for (int step = 0; step <= 200; step++)
            {
                float cr = step * 0.05f;
                var mat = new MaterialProperties(
                    $"CrSweep_{step}",
                    MaterialType.Custom,
                    density: 2000f,
                    resistanceCoefficient: cr,
                    ricochetAngleThreshold: 1.57f,
                    yieldEnergyThreshold: 100f);

                var res = _penetrationSystem.CalculatePenetration(proj, profile, mat, 0.02f, new Vector3(0, 0, -1));

                Assert.True(res.ExitVelocity <= prevExitVelocity + 1e-4f,
                    $"Cr {cr}: ExitVelocity {res.ExitVelocity} > prev {prevExitVelocity}");

                Assert.True(res.TransferredKineticEnergy >= prevTransferredEnergy - 1e-4f,
                    $"Cr {cr}: TransferredEnergy {res.TransferredKineticEnergy} < prev {prevTransferredEnergy}");

                prevExitVelocity = res.ExitVelocity;
                prevTransferredEnergy = res.TransferredKineticEnergy;
            }
        }

        #endregion
    }
}
