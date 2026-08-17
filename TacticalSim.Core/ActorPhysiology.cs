using System;
using System.Collections.Generic;
using System.Numerics;

namespace TacticalSim.Core.Physiology
{
    public enum BodyPartType
    {
        Head,
        Thorax,
        Abdomen,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg
    }

    /// <summary>
    /// Represents a hierarchical body part containing a volume of PhysiologicalVoxels.
    /// </summary>
    public class BodyPart
    {
        public BodyPartType Type { get; set; }
        public BodyPart Parent { get; set; }
        public List<BodyPart> Children { get; set; } = new List<BodyPart>();
        
        // In a full implementation, this would be the root of an Octree.
        // For scaffolding, we represent it as a flat list or conceptual bounds.
        public List<PhysiologicalVoxel> Voxels { get; set; } = new List<PhysiologicalVoxel>();
        
        public float TotalBloodVolumeLost { get; set; }
        public float CurrentBleedRate { get; set; } // ml per second

        public void ApplyTrauma(Vector3 impactPoint, float kineticEnergy)
        {
            // Simplified trauma routing to voxels
            foreach (var voxel in Voxels)
            {
                // In reality, raycast or spatial query the octree based on trajectory
                if (Vector3.Distance(voxel.Center, impactPoint) < voxel.Size)
                {
                    voxel.ApplyKineticEnergy(kineticEnergy, impactPoint);
                    
                    // Induce hemorrhage if permanent cavity exists
                    if (voxel.PermanentCavityVolume > 0)
                    {
                        CurrentBleedRate += voxel.PermanentCavityVolume * 0.5f; // ml/s
                    }
                }
            }
        }
    }

    /// <summary>
    /// Controller for the state machine of an actor's physiology.
    /// Manages the fractionated TU economy and active hemorrhage resolution.
    /// </summary>
    public interface IActorPhysiology
    {
        BodyPart RootBodyPart { get; }
        float TotalBloodVolume { get; }
        float ConsciousnessLevel { get; } // 0.0 to 1.0
        
        /// <summary>
        /// Advances the physiological state machine by a given timestep.
        /// Evaluates active hemorrhage and updates consciousness (MARCH triage model).
        /// </summary>
        void TickPhysiology(float dt);
        
        /// <summary>
        /// Applies trauma from a ballistic impact.
        /// </summary>
        void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint);
    }
}
