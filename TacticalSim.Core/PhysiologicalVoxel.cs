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

            return ApplyKineticEnergy(energyLost, projectile.Position - (rayDirection * (distanceInMeters * 0.5f)));
        }

        /// <summary>
        /// Applies kinetic energy transfer to the voxel directly (e.g. from adjacent blast).
        /// </summary>
        public CavitationEvent? ApplyKineticEnergy(float deltaE, Vector3 originPoint)
        {
            if (IsDestroyed) return null;

            DepositedEnergy += deltaE;
            
            // Temporary cavity volume calculation
            // Energy creates a cavity volume inversely proportional to elasticity and density
            float stretchFactor = deltaE / (Tissue.Density * Tissue.Elasticity * 50f + 1e-4f);
            TemporaryCavityVolume += stretchFactor;
            
            // Calculate spherical radius of the temporary cavity (V = 4/3 * pi * r^3 -> r = cbrt(V / (4/3 * pi)))
            float cavityRadius = MathF.Cbrt(TemporaryCavityVolume / (4f/3f * MathF.PI));

            // Permanent cavity occurs when stretch stress exceeds shear strength
            if (stretchFactor > Tissue.ShearStrength * 0.005f) // Calibrated conversion factor
            {
                PermanentCavityVolume += stretchFactor * 0.1f; // 10% of temporary becomes permanent
                if (PermanentCavityVolume > (Size * Size * Size)) 
                {
                    IsDestroyed = true;
                }
            }
            
            // If cavity radius exceeds half voxel size, we need to propagate
            if (cavityRadius > (Size * 0.5f))
            {
                return new CavitationEvent
                {
                    Origin = originPoint,
                    Radius = cavityRadius,
                    Energy = deltaE * 0.5f // Dissipate energy radially
                };
            }
            
            return null;
        }
    }
}
