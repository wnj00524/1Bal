using System;
using System.Numerics;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Physiology
{
    public struct TissueProperties
    {
        public float Density; // kg/m^3
        public float Elasticity; // Resistance to permanent tearing from stretch
        public float ShearStrength; // MPa, resistance to permanent tearing
        public float PainReceptorDensity;
    }
    
    public struct CavitationEvent
    {
        public Vector3 Origin;
        public float Radius;
        public float Energy;
    }

    /// <summary>
    /// Represents a discrete volume within a hierarchical body part (Octree node).
    /// </summary>
    public class PhysiologicalVoxel
    {
        public Vector3 Center { get; }
        public float Size { get; } // Assumes cubic voxel
        public Vector3 MinBounds { get; }
        public Vector3 MaxBounds { get; }
        
        public TissueProperties Tissue;
        public OrganType Organ { get; }
        
        // Damage state
        public float DepositedEnergy { get; private set; }
        public float TemporaryCavityVolume { get; private set; }
        public float PermanentCavityVolume { get; private set; }
        public bool IsDestroyed { get; private set; }
        
        public PhysiologicalVoxel(Vector3 center, float size, TissueProperties tissue, OrganType organ = OrganType.None)
        {
            Center = center;
            Size = size;
            Tissue = tissue;
            Organ = organ;
            
            float halfSize = size / 2f;
            MinBounds = new Vector3(center.X - halfSize, center.Y - halfSize, center.Z - halfSize);
            MaxBounds = new Vector3(center.X + halfSize, center.Y + halfSize, center.Z + halfSize);
            
            DepositedEnergy = 0f;
            TemporaryCavityVolume = 0f;
            PermanentCavityVolume = 0f;
            IsDestroyed = false;
        }

        /// <summary>
        /// Intersects a ray (projectile trajectory) with this voxel.
        /// Returns the distance traveled through the voxel, or 0 if no intersection.
        /// </summary>
        public float CalculateIntersectionDistance(Vector3 rayOrigin, Vector3 rayDirection)
        {
            float t1 = (MinBounds.X - rayOrigin.X) / (rayDirection.X != 0 ? rayDirection.X : 1e-6f);
            float t2 = (MaxBounds.X - rayOrigin.X) / (rayDirection.X != 0 ? rayDirection.X : 1e-6f);
            float t3 = (MinBounds.Y - rayOrigin.Y) / (rayDirection.Y != 0 ? rayDirection.Y : 1e-6f);
            float t4 = (MaxBounds.Y - rayOrigin.Y) / (rayDirection.Y != 0 ? rayDirection.Y : 1e-6f);
            float t5 = (MinBounds.Z - rayOrigin.Z) / (rayDirection.Z != 0 ? rayDirection.Z : 1e-6f);
            float t6 = (MaxBounds.Z - rayOrigin.Z) / (rayDirection.Z != 0 ? rayDirection.Z : 1e-6f);

            float tmin = MathF.Max(MathF.Max(MathF.Min(t1, t2), MathF.Min(t3, t4)), MathF.Min(t5, t6));
            float tmax = MathF.Min(MathF.Min(MathF.Max(t1, t2), MathF.Max(t3, t4)), MathF.Max(t5, t6));

            // if tmax < 0, ray (line) is intersecting AABB, but the whole AABB is behind us
            if (tmax < 0)
                return 0f;

            // if tmin > tmax, ray doesn't intersect AABB
            if (tmin > tmax)
                return 0f;

            // The intersection length is the distance between entry and exit points
            // tmin could be < 0 if the ray origin is inside the box
            float entry = MathF.Max(0, tmin);
            float exit = tmax;
            
            return exit - entry;
        }

        /// <summary>
        /// Processes a projectile passing through this voxel. 
        /// Calculates velocity loss, transfers energy, and calculates cavitation.
        /// Returns a CavitationEvent if radial propagation to neighbors is required.
        /// </summary>
        public CavitationEvent? ProcessPenetration(ref ProjectileState projectile, in BallisticProfile profile)
        {
            if (IsDestroyed) return null;

            Vector3 rayDirection = Vector3.Normalize(projectile.Velocity);
            float distanceInMeters = CalculateIntersectionDistance(projectile.Position, rayDirection);
            
            if (distanceInMeters <= 0.0001f) return null;

            float speed = projectile.Velocity.Length();
            float initialEnergy = 0.5f * profile.Mass * (speed * speed);

            if (Organ == OrganType.Bone)
            {
                Vector3 impactNormal = CalculateImpactNormal(projectile.Position, rayDirection);
                BoneImpactResult impact = InternalRicochetSolver.Resolve(
                    projectile.Velocity, profile, impactNormal, Tissue, distanceInMeters);
                projectile.Velocity = impact.Velocity;
                if (impact.Outcome == BoneImpactOutcome.Ricocheted)
                    return ApplyKineticEnergy(impact.TransferredEnergy, projectile.Position);

                ApplyKineticEnergy(impact.TransferredEnergy, projectile.Position);
            }
            
            // Simplified ballistic gel/tissue penetration drag: F_d = 0.5 * rho_tissue * v^2 * Cd * A
            // We use the tissue density instead of air density.
            float dragForce = 0.5f * Tissue.Density * (speed * speed) * profile.DragModel.GetDragCoefficient(0) * profile.CrossSectionalArea;
            
            // Work done by drag = Force * distance = Energy lost
            float energyLost = dragForce * distanceInMeters;
            
            // Clamp energy lost to max kinetic energy
            energyLost = MathF.Min(energyLost, initialEnergy);
            
            // Calculate exit velocity
            float remainingEnergy = initialEnergy - energyLost;
            float exitSpeed = MathF.Sqrt((2f * remainingEnergy) / profile.Mass);
            
            // Update projectile state
            projectile.Velocity = rayDirection * exitSpeed;
            projectile.Position += rayDirection * distanceInMeters; // Advance projectile to exit point
            
            float directCrushVolume = profile.CrossSectionalArea * distanceInMeters;

            return ApplyKineticEnergy(energyLost, projectile.Position - (rayDirection * (distanceInMeters * 0.5f)), directCrushVolume);
        }

        public bool Contains(Vector3 point)
        {
            return point.X >= MinBounds.X && point.X <= MaxBounds.X &&
                   point.Y >= MinBounds.Y && point.Y <= MaxBounds.Y &&
                   point.Z >= MinBounds.Z && point.Z <= MaxBounds.Z;
        }

        /// <summary>
        /// Processes a projectile moving a specific distance through this voxel during a timestep.
        /// </summary>
        public CavitationEvent? ProcessPenetrationStep(ref ProjectileState projectile, in BallisticProfile profile, float distanceInMeters)
        {
            if (IsDestroyed || distanceInMeters <= 0.000001f) return null;

            Vector3 rayDirection = Vector3.Normalize(projectile.Velocity);
            float speed = projectile.Velocity.Length();
            float initialEnergy = 0.5f * profile.Mass * (speed * speed);

            if (Organ == OrganType.Bone)
            {
                Vector3 impactNormal = CalculateNearestSurfaceNormal(projectile.Position);
                BoneImpactResult impact = InternalRicochetSolver.Resolve(
                    projectile.Velocity, profile, impactNormal, Tissue, MathF.Min(distanceInMeters, Size));
                projectile.Velocity = impact.Velocity;
                if (impact.Outcome == BoneImpactOutcome.Ricocheted)
                    return ApplyKineticEnergy(impact.TransferredEnergy, projectile.Position);

                ApplyKineticEnergy(impact.TransferredEnergy, projectile.Position);
            }
            
            // Simplified ballistic gel/tissue penetration drag: F_d = 0.5 * rho_tissue * v^2 * Cd * A
            float dragForce = 0.5f * Tissue.Density * (speed * speed) * profile.DragModel.GetDragCoefficient(0) * profile.CrossSectionalArea;
            
            // Work done by drag = Force * distance = Energy lost
            float energyLost = dragForce * distanceInMeters;
            energyLost = MathF.Min(energyLost, initialEnergy);
            
            // Calculate exit velocity
            float remainingEnergy = initialEnergy - energyLost;
            float exitSpeed = MathF.Sqrt((2f * remainingEnergy) / profile.Mass);
            
            // Update projectile state (Position is handled by RK4 external loop)
            projectile.Velocity = rayDirection * exitSpeed;

            // Direct crush damage from the physical bullet passing through
            float directCrushVolume = profile.CrossSectionalArea * distanceInMeters;

            return ApplyKineticEnergy(energyLost, projectile.Position, directCrushVolume);
        }

        private Vector3 CalculateImpactNormal(Vector3 origin, Vector3 direction)
        {
            if (Contains(origin)) return CalculateNearestSurfaceNormal(origin);

            float entryTime = float.NegativeInfinity;
            Vector3 normal = CalculateNearestSurfaceNormal(origin);

            CheckEntryPlane(MinBounds.X, MaxBounds.X, origin.X, direction.X,
                -Vector3.UnitX, Vector3.UnitX, ref entryTime, ref normal);
            CheckEntryPlane(MinBounds.Y, MaxBounds.Y, origin.Y, direction.Y,
                -Vector3.UnitY, Vector3.UnitY, ref entryTime, ref normal);
            CheckEntryPlane(MinBounds.Z, MaxBounds.Z, origin.Z, direction.Z,
                -Vector3.UnitZ, Vector3.UnitZ, ref entryTime, ref normal);
            return normal;
        }

        private static void CheckEntryPlane(float minimum, float maximum, float origin, float direction,
            Vector3 minimumNormal, Vector3 maximumNormal, ref float entryTime, ref Vector3 normal)
        {
            if (MathF.Abs(direction) < 1e-6f) return;
            float time = ((direction > 0f ? minimum : maximum) - origin) / direction;
            if (time > entryTime)
            {
                entryTime = time;
                normal = direction > 0f ? minimumNormal : maximumNormal;
            }
        }

        private Vector3 CalculateNearestSurfaceNormal(Vector3 point)
        {
            float xMin = MathF.Abs(point.X - MinBounds.X);
            float xMax = MathF.Abs(point.X - MaxBounds.X);
            float yMin = MathF.Abs(point.Y - MinBounds.Y);
            float yMax = MathF.Abs(point.Y - MaxBounds.Y);
            float zMin = MathF.Abs(point.Z - MinBounds.Z);
            float zMax = MathF.Abs(point.Z - MaxBounds.Z);
            float minimum = MathF.Min(MathF.Min(MathF.Min(xMin, xMax), MathF.Min(yMin, yMax)), MathF.Min(zMin, zMax));

            if (minimum == xMin) return -Vector3.UnitX;
            if (minimum == xMax) return Vector3.UnitX;
            if (minimum == yMin) return -Vector3.UnitY;
            if (minimum == yMax) return Vector3.UnitY;
            return minimum == zMin ? -Vector3.UnitZ : Vector3.UnitZ;
        }

        /// <summary>
        /// Applies kinetic energy transfer to the voxel directly (e.g. from adjacent blast).
        /// </summary>
        public CavitationEvent? ApplyKineticEnergy(float deltaE, Vector3 originPoint, float directCrushVolume = 0f)
        {
            if (IsDestroyed) return null;

            float previouslyDepositedEnergy = DepositedEnergy;
            DepositedEnergy += deltaE;
            PermanentCavityVolume += directCrushVolume;

            CavitationEvent? result = null;

            if (directCrushVolume > 0f)
            {
                // This is a direct hit from the bullet.
                // Calculate the macroscopic temporary cavity created by this energy dump.
                float macroscopicStretch = deltaE / (Tissue.Density * Tissue.Elasticity * 50f + 1e-4f);
                float cavityRadius = MathF.Cbrt(macroscopicStretch / (4f/3f * MathF.PI));
                
                result = new CavitationEvent
                {
                    Origin = originPoint,
                    Energy = deltaE,
                    Radius = cavityRadius
                };
            }
            else
            {
                // This is a neighbor receiving energy from the blast wave. Tissue
                // damage is cumulative: repeated loads must be evaluated against
                // energy already deposited in this voxel rather than treating every
                // impact as though it were acting on pristine tissue.
                float stretchDenominator = Tissue.Density * Tissue.Elasticity * 50f + 1e-4f;
                float previousStretchVolume = previouslyDepositedEnergy / stretchDenominator;
                float cumulativeStretchVolume = DepositedEnergy / stretchDenominator;
                float voxelVolume = Size * Size * Size;
                
                // If the local stretch exceeds the shear limits of the tissue, it tears permanently.
                // The threshold is based on ShearStrength. Brittle tissues (liver, brain, bone) tear easily.
                float tearThreshold = Tissue.ShearStrength * 0.1f * voxelVolume;
                
                if (cumulativeStretchVolume > tearThreshold)
                {
                    // Tissues with high elasticity (like muscle) snap back and suffer less permanent tearing
                    // Brittle tissues (like liver, bone) suffer massive permanent tearing
                    float tearingFactor = 0.1f * (1.0f - Tissue.Elasticity);
                    float previousTearingVolume = previousStretchVolume > tearThreshold
                        ? previousStretchVolume * tearingFactor
                        : 0f;
                    float cumulativeTearingVolume = cumulativeStretchVolume * tearingFactor;
                    PermanentCavityVolume += cumulativeTearingVolume - previousTearingVolume;
                }
            }
            
            // If the voxel has accumulated enough permanent tearing/crush (50% of its volume), it is destroyed.
            if (PermanentCavityVolume > (Size * Size * Size * 0.5f))
            {
                IsDestroyed = true;
                PermanentCavityVolume = Size * Size * Size;
            }

            return result;
        }
    }
}
