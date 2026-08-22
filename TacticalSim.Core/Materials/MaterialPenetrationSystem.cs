using System;
using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Implements terminal ballistics simulation for barrier penetration, resistance drag, and ricochet mechanics.
    /// </summary>
    public class MaterialPenetrationSystem : IMaterialPenetrationSystem
    {
        public PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            float nominalThickness,
            Vector3 surfaceNormal)
        {
            Vector3 entryPoint = projectile.Position;
            float speed = projectile.Velocity.Length();

            if (speed < 1e-6f)
            {
                float eZero = 0.5f * profile.MassKilograms.Kilograms * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = MathF.Max(0f, nominalThickness),
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }

            if (nominalThickness <= 0f)
            {
                float ek0 = 0.5f * profile.MassKilograms.Kilograms * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Perforated,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = 0f,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = speed,
                    InitialKineticEnergy = ek0,
                    RemainingKineticEnergy = ek0,
                    TransferredKineticEnergy = 0f,
                    ExitVelocityVector = projectile.Velocity,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = projectile.Velocity,
                        Time = projectile.Time
                    }
                };
            }

            Vector3 d = projectile.Velocity / speed;
            Vector3 n = surfaceNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(surfaceNormal) : -d;

            // Angle of incidence theta = acos(clamp(|-d . n|, 0, 1))
            float dot = MathF.Abs(Vector3.Dot(d, n));
            float cosTheta = Math.Clamp(dot, 0f, 1f);
            float theta = MathF.Acos(cosTheta);

            float effectiveThickness = nominalThickness / MathF.Max(cosTheta, 1e-4f);

            return ProcessPenetrationCore(
                projectile,
                profile,
                material,
                entryPoint,
                d,
                n,
                theta,
                effectiveThickness,
                explicitExitPoint: null);
        }

        public PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            Vector3 entryPoint,
            Vector3 exitPoint,
            Vector3 surfaceNormal)
        {
            float speed = projectile.Velocity.Length();
            float effectiveThickness = Vector3.Distance(entryPoint, exitPoint);

            if (speed < 1e-6f)
            {
                float eZero = 0.5f * profile.MassKilograms.Kilograms * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    EffectiveThickness = effectiveThickness,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }

            if (effectiveThickness <= 0f)
            {
                float ek0 = 0.5f * profile.MassKilograms.Kilograms * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Perforated,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    EffectiveThickness = 0f,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = speed,
                    InitialKineticEnergy = ek0,
                    RemainingKineticEnergy = ek0,
                    TransferredKineticEnergy = 0f,
                    ExitVelocityVector = projectile.Velocity,
                    ExitState = new ProjectileState
                    {
                        Position = exitPoint,
                        Velocity = projectile.Velocity,
                        Time = projectile.Time
                    }
                };
            }

            Vector3 d = projectile.Velocity / speed;
            Vector3 n = surfaceNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(surfaceNormal) : -d;

            float dot = MathF.Abs(Vector3.Dot(d, n));
            float cosTheta = Math.Clamp(dot, 0f, 1f);
            float theta = MathF.Acos(cosTheta);

            return ProcessPenetrationCore(
                projectile,
                profile,
                material,
                entryPoint,
                d,
                n,
                theta,
                effectiveThickness,
                explicitExitPoint: exitPoint);
        }

        private static PenetrationResult ProcessPenetrationCore(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            Vector3 entryPoint,
            Vector3 d,
            Vector3 n,
            float theta,
            float effectiveThickness,
            Vector3? explicitExitPoint)
        {
            float v0 = projectile.Velocity.Length();
            float ek0 = 0.5f * profile.MassKilograms.Kilograms * v0 * v0;

            // Check for ricochet based on critical angle of incidence threshold
            if (theta >= material.RicochetAngleThreshold)
            {
                // Align normal outward (opposing incident direction d) so reflection points away from barrier face
                Vector3 nOutward = Vector3.Dot(d, n) > 0 ? -n : n;
                Vector3 dRefl = d - 2.0f * Vector3.Dot(d, nOutward) * nOutward;
                if (dRefl.LengthSquared() > 1e-6f)
                {
                    dRefl = Vector3.Normalize(dRefl);
                }
                else
                {
                    dRefl = -d;
                }

                float eLoss = ek0 * (1.0f - MathF.Sin(theta)) * 0.3f;
                float eRem = ek0 - eLoss;
                float eTrans = eLoss;

                float vExit = MathF.Sqrt(MathF.Max(0f, 2.0f * eRem / profile.MassKilograms.Kilograms));
                Vector3 vExitVec = dRefl * vExit;

                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Ricochet,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = effectiveThickness,
                    AngleOfIncidence = theta,
                    InitialVelocity = v0,
                    ExitVelocity = vExit,
                    InitialKineticEnergy = ek0,
                    RemainingKineticEnergy = eRem,
                    TransferredKineticEnergy = eTrans,
                    ExitVelocityVector = vExitVec,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = vExitVec,
                        Time = projectile.Time
                    }
                };
            }
            else
            {
                // Terminal ballistics resistance / medium drag force
                float fDrag = 0.5f * material.MassDensity.KilogramsPerCubicMeter * material.ResistanceCoefficient * profile.CrossSectionalAreaSquareMeters.SquareMeters * v0 * v0;
                float deltaE = MathF.Min(fDrag * effectiveThickness, ek0);
                float eRem = ek0 - deltaE;
                float eTrans = deltaE;

                // Perforation criteria: residual kinetic energy > 0.001 J and initial kinetic energy >= yield threshold
                if (eRem > Energy.FromJoules(0.001f).Joules && ek0 >= material.YieldEnergy.Joules)
                {
                    float vExit = MathF.Sqrt(MathF.Max(0f, 2.0f * eRem / profile.MassKilograms.Kilograms));
                    Vector3 vExitVec = d * vExit;
                    Vector3 calculatedExitPoint = explicitExitPoint ?? (entryPoint + d * effectiveThickness);

                    return new PenetrationResult
                    {
                        Outcome = PenetrationOutcome.Perforated,
                        EntryPoint = entryPoint,
                        ExitPoint = calculatedExitPoint,
                        EffectiveThickness = effectiveThickness,
                        AngleOfIncidence = theta,
                        InitialVelocity = v0,
                        ExitVelocity = vExit,
                        InitialKineticEnergy = ek0,
                        RemainingKineticEnergy = eRem,
                        TransferredKineticEnergy = eTrans,
                        ExitVelocityVector = vExitVec,
                        ExitState = new ProjectileState
                        {
                            Position = calculatedExitPoint,
                            Velocity = vExitVec,
                            Time = projectile.Time
                        }
                    };
                }
                else
                {
                    // Projectile arrested/stopped in barrier
                    float penetrationDepth = fDrag > 1e-6f ? MathF.Min(ek0 / fDrag, effectiveThickness) : 0f;
                    Vector3 stoppedPoint = entryPoint + d * penetrationDepth;

                    return new PenetrationResult
                    {
                        Outcome = PenetrationOutcome.Stopped,
                        EntryPoint = entryPoint,
                        ExitPoint = stoppedPoint,
                        EffectiveThickness = effectiveThickness,
                        AngleOfIncidence = theta,
                        InitialVelocity = v0,
                        ExitVelocity = 0f,
                        InitialKineticEnergy = ek0,
                        RemainingKineticEnergy = 0f,
                        TransferredKineticEnergy = ek0,
                        ExitVelocityVector = Vector3.Zero,
                        ExitState = new ProjectileState
                        {
                            Position = stoppedPoint,
                            Velocity = Vector3.Zero,
                            Time = projectile.Time
                        }
                    };
                }
            }
        }
    }
}
