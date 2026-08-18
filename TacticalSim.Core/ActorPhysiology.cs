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

    public enum HemorrhageClass
    {
        Class1, // 0-15%
        Class2, // 15-30%
        Class3, // 30-40%
        Class4, // >40%
        Fatal   // >50%
    }

    /// <summary>
    /// Represents a hierarchical body part containing a volume of PhysiologicalVoxels.
    /// </summary>
    public class BodyPart
    {
        public BodyPartType Type { get; set; }
        public BodyPart? Parent { get; set; }
        public List<BodyPart> Children { get; set; } = new List<BodyPart>();
        
        public List<PhysiologicalVoxel> Voxels { get; set; } = new List<PhysiologicalVoxel>();
        
        // Circulatory System
        public float ArterialBleedRate { get; set; } // ml/s
        public float VenousBleedRate { get; set; } // ml/s
        
        // Interventions
        public bool HasTourniquet { get; set; }
        public float IschemiaDuration { get; set; } // seconds
        public bool IsNecrotic { get; set; }

        public float GetActiveBleedRate()
        {
            if (HasTourniquet && IsExtremity(Type))
            {
                return 0f; // Tourniquet completely halts distal bleeding
            }
            
            float activeRate = ArterialBleedRate + VenousBleedRate;
            
            // Dynamically calculate from destroyed voxels
            foreach (var voxel in Voxels)
            {
                if (voxel.IsDestroyed)
                {
                    float volCc = voxel.Size * voxel.Size * voxel.Size * 1_000_000f; // m^3 to cm^3
                    float rate = voxel.Organ switch
                    {
                        OrganType.Heart => 10.0f,
                        OrganType.Liver => 2.0f,
                        OrganType.Lung => 0.5f,
                        OrganType.Muscle => 0.05f,
                        OrganType.Stomach => 0.1f,
                        OrganType.Bone => 0.8f,
                        _ => 0.05f
                    };
                    activeRate += rate * volCc;
                }
            }
            
            return activeRate;
        }

        private bool IsExtremity(BodyPartType type) =>
            type == BodyPartType.LeftArm || type == BodyPartType.RightArm ||
            type == BodyPartType.LeftLeg || type == BodyPartType.RightLeg;

        public void ApplyTrauma(Vector3 impactPoint, float kineticEnergy)
        {
            // Simplified trauma routing to voxels
            foreach (var voxel in Voxels)
            {
                if (!voxel.IsDestroyed && Vector3.Distance(voxel.Center, impactPoint) < voxel.Size)
                {
                    voxel.ApplyKineticEnergy(kineticEnergy, impactPoint);
                }
            }
        }
    }

    /// <summary>
    /// Controller for the state machine of an actor's physiology.
    /// </summary>
    public interface IActorPhysiology
    {
        BodyPart RootBodyPart { get; }
        float TotalBloodVolume { get; }
        float ConsciousnessLevel { get; } // 0.0 to 1.0
        
        void TickPhysiology(float dt);
        void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint);
    }

    public class TacticalActorPhysiology : IActorPhysiology
    {
        public BodyPart RootBodyPart { get; private set; } = null!;
        public float TotalBloodVolume { get; private set; } = 5000f; // 5L baseline
        private float _baselineBloodVolume = 5000f;
        public float ConsciousnessLevel { get; private set; } = 1.0f;
        
        public float HeartRateBpm { get; private set; } = 80f;
        public float MeanArterialPressureMmhg { get; private set; } = 93f; // 120/80
        public HemorrhageClass CurrentHemorrhageClass { get; private set; } = HemorrhageClass.Class1;

        public void SetRoot(BodyPart root) => RootBodyPart = root;

        public void TickPhysiology(float dt)
        {
            float totalBleedRate = CalculateBleedRate(RootBodyPart);
            if (totalBleedRate > 0)
            {
                TotalBloodVolume -= totalBleedRate * dt;
            }

            TickIschemia(RootBodyPart, dt);
            UpdateCardiovascularState();
        }

        private float CalculateBleedRate(BodyPart part)
        {
            float rate = part.GetActiveBleedRate();
            foreach (var child in part.Children)
                rate += CalculateBleedRate(child);
            return rate;
        }

        private void TickIschemia(BodyPart part, float dt)
        {
            if (part.HasTourniquet)
            {
                part.IschemiaDuration += dt;
                if (part.IschemiaDuration > 7200f) // 2 hours without blood flow
                {
                    part.IsNecrotic = true;
                }
            }
            foreach (var child in part.Children)
                TickIschemia(child, dt);
        }

        private void UpdateCardiovascularState()
        {
            float lostPercent = 1.0f - (TotalBloodVolume / _baselineBloodVolume);
            
            if (lostPercent < 0.15f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class1;
                HeartRateBpm = 80f + (lostPercent / 0.15f) * 20f; // Up to 100
                MeanArterialPressureMmhg = 93f; // Compensated
                ConsciousnessLevel = 1.0f;
            }
            else if (lostPercent < 0.30f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class2;
                HeartRateBpm = 100f + ((lostPercent - 0.15f) / 0.15f) * 20f; // Up to 120
                MeanArterialPressureMmhg = 93f - ((lostPercent - 0.15f) / 0.15f) * 13f; // Drops to ~80
                ConsciousnessLevel = 0.9f; // Mild anxiety
            }
            else if (lostPercent < 0.40f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class3;
                HeartRateBpm = 120f + ((lostPercent - 0.30f) / 0.10f) * 20f; // Up to 140
                MeanArterialPressureMmhg = 80f - ((lostPercent - 0.30f) / 0.10f) * 20f; // Drops to ~60
                ConsciousnessLevel = 0.6f; // Confused
            }
            else if (lostPercent < 0.50f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class4;
                HeartRateBpm = 140f - ((lostPercent - 0.40f) / 0.10f) * 40f; // Tachycardia fails, drops
                MeanArterialPressureMmhg = 60f - ((lostPercent - 0.40f) / 0.10f) * 30f; // Lethargic hypotensive
                ConsciousnessLevel = 0.2f; // Unresponsive
            }
            else
            {
                CurrentHemorrhageClass = HemorrhageClass.Fatal;
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
                ConsciousnessLevel = 0f; // Dead
            }
        }

        public void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint)
        {
            RootBodyPart.ApplyTrauma(hitPoint, kineticEnergy);
        }
    }
}
