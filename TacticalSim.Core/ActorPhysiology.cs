using System;
using System.Collections.Generic;
using System.Numerics;

namespace TacticalSim.Core.Physiology
{
    public enum BodyPartType
    {
        Head,
        Neck,
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
                        OrganType.Spleen => 3.0f, // Highly vascular
                        OrganType.Kidney => 2.5f, // Renal artery/vein
                        OrganType.Lung => 0.5f,
                        OrganType.Intestines => 0.2f, // Moderate bleeding
                        OrganType.Muscle => 0.05f,
                        OrganType.Stomach => 0.1f,
                        OrganType.Bone => 0.8f,
                        OrganType.Brain => 5.0f, // Highly vascular, intracranial pressure ignores
                        OrganType.Airway => 1.0f, // Bleeds into lungs
                        OrganType.Mouth => 0.5f, // Bleeds into airway
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
            foreach (var child in Children)
            {
                child.ApplyTrauma(impactPoint, kineticEnergy);
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
        float HeartRateBpm { get; }
        float MeanArterialPressureMmhg { get; }
        HemorrhageClass CurrentHemorrhageClass { get; }

        // Respiratory System
        float BloodOxygenation { get; } // 1.0 down to 0.0
        float AirwayObstruction { get; } // 0.0 to 1.0
        float AlveolarBloodAccumulation { get; } // ml
        
        // Nervous System
        float PainLevel { get; }
        float ShockLevel { get; }
        
        // Motor System
        float MobilityLevel { get; } // 1.0 down to 0.0
        float WeaponHandlingLevel { get; } // 1.0 down to 0.0
        
        void AdministerAnalgesic(float strength);
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

        public float BloodOxygenation { get; private set; } = 1.0f;
        public float AirwayObstruction { get; private set; } = 0f;
        public float AlveolarBloodAccumulation { get; private set; } = 0f;

        public float PainLevel { get; private set; } = 0f;
        public float ShockLevel { get; private set; } = 0f;
        
        public float MobilityLevel { get; private set; } = 1.0f;
        public float WeaponHandlingLevel { get; private set; } = 1.0f;

        private float _analgesicLevel = 0f;

        public void SetRoot(BodyPart root)
        {
            RootBodyPart = root;
        }

        public void AdministerAnalgesic(float strength)
        {
            _analgesicLevel = MathF.Min(1.0f, _analgesicLevel + strength);
        }

        public void TickPhysiology(float dt)
        {
            float totalBleedRate = CalculateBleedRate(RootBodyPart, out float airwayBleedRate);
            if (totalBleedRate > 0)
            {
                TotalBloodVolume -= totalBleedRate * dt;
            }

            // ABC: Airway bleeding pools into the lungs
            if (airwayBleedRate > 0)
            {
                AlveolarBloodAccumulation += airwayBleedRate * dt;
            }

            TickIschemia(RootBodyPart, dt);
            UpdateRespiratoryState(dt);
            UpdateCardiovascularState();
            UpdateNervousSystemState(dt);
            UpdateMotorState();
        }

        private void UpdateMotorState()
        {
            float legBoneTotal = 0, legBoneDest = 0;
            float armBoneTotal = 0, armBoneDest = 0;
            float legMuscleTotal = 0, legMuscleDest = 0;
            float armMuscleTotal = 0, armMuscleDest = 0;

            CalculateMotorDamage(RootBodyPart, ref legBoneTotal, ref legBoneDest, ref legMuscleTotal, ref legMuscleDest, ref armBoneTotal, ref armBoneDest, ref armMuscleTotal, ref armMuscleDest);

            if (legBoneTotal > 0)
            {
                float boneLoss = legBoneDest / legBoneTotal;
                float muscleLoss = legMuscleTotal > 0 ? (legMuscleDest / legMuscleTotal) : 0f;
                // 20% bone destruction or 50% muscle destruction will completely disable the limb
                MobilityLevel = MathF.Max(0f, 1.0f - (boneLoss * 5.0f) - (muscleLoss * 2.0f));
            }
            
            if (armBoneTotal > 0)
            {
                float boneLoss = armBoneDest / armBoneTotal;
                float muscleLoss = armMuscleTotal > 0 ? (armMuscleDest / armMuscleTotal) : 0f;
                WeaponHandlingLevel = MathF.Max(0f, 1.0f - (boneLoss * 5.0f) - (muscleLoss * 2.0f));
            }
        }

        private void CalculateMotorDamage(BodyPart part, ref float lbTotal, ref float lbDest, ref float lmTotal, ref float lmDest, ref float abTotal, ref float abDest, ref float amTotal, ref float amDest)
        {
            bool isLeg = (part.Type == BodyPartType.LeftLeg || part.Type == BodyPartType.RightLeg);
            bool isArm = (part.Type == BodyPartType.LeftArm || part.Type == BodyPartType.RightArm);
            
            if (isLeg || isArm)
            {
                foreach (var voxel in part.Voxels)
                {
                    if (voxel.Organ == OrganType.Bone)
                    {
                        if (isLeg) { lbTotal += 1f; if (voxel.IsDestroyed) lbDest += 1f; }
                        else       { abTotal += 1f; if (voxel.IsDestroyed) abDest += 1f; }
                    }
                    else if (voxel.Organ == OrganType.Muscle)
                    {
                        if (isLeg) { lmTotal += 1f; if (voxel.IsDestroyed) lmDest += 1f; }
                        else       { amTotal += 1f; if (voxel.IsDestroyed) amDest += 1f; }
                    }
                }
            }
            
            foreach (var child in part.Children)
            {
                CalculateMotorDamage(child, ref lbTotal, ref lbDest, ref lmTotal, ref lmDest, ref abTotal, ref abDest, ref amTotal, ref amDest);
            }
        }

        private float CalculateBleedRate(BodyPart part, out float airwayBleed)
        {
            float rate = part.GetActiveBleedRate();
            float airwayRate = 0f;
            
            // Check for airway/mouth destruction which bleeds into lungs
            foreach (var voxel in part.Voxels)
            {
                if (voxel.IsDestroyed && (voxel.Organ == OrganType.Airway || voxel.Organ == OrganType.Mouth))
                {
                    float volCc = voxel.Size * voxel.Size * voxel.Size * 1_000_000f;
                    airwayRate += (voxel.Organ == OrganType.Airway ? 1.0f : 0.5f) * volCc;
                }
            }

            foreach (var child in part.Children)
            {
                rate += CalculateBleedRate(child, out float childAirwayBleed);
                airwayRate += childAirwayBleed;
            }

            airwayBleed = airwayRate;
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

        private void UpdateRespiratoryState(float dt)
        {
            // 1. Direct Airway Trauma
            float directObstruction = 0f;
            float totalAirwayVoxels = 0f;
            float destroyedAirwayVoxels = 0f;
            float lungCapacityLost = 0f;
            float totalLungVoxels = 0f;
            float destroyedLungVoxels = 0f;

            CalculateRespiratoryDamage(RootBodyPart, ref totalAirwayVoxels, ref destroyedAirwayVoxels, ref totalLungVoxels, ref destroyedLungVoxels);

            if (totalAirwayVoxels > 0)
                directObstruction = destroyedAirwayVoxels / totalAirwayVoxels;

            // 2. Blood Obstruction
            // Assume 500ml of blood in lungs causes complete obstruction
            float bloodObstruction = MathF.Min(1.0f, AlveolarBloodAccumulation / 500f);

            AirwayObstruction = MathF.Max(directObstruction, bloodObstruction);

            // 3. Lung Capacity
            if (totalLungVoxels > 0)
                lungCapacityLost = destroyedLungVoxels / totalLungVoxels;
            float remainingCapacity = 1.0f - lungCapacityLost;

            // 4. Respiration Effectiveness
            float effectiveness = (1.0f - AirwayObstruction) * remainingCapacity;

            // 5. Hypoxia Calculation
            if (effectiveness < 0.8f) // Demand threshold
            {
                // Deplete oxygen
                float depletionRate = (0.8f - effectiveness) * 0.05f; // Drops SpO2 over time
                BloodOxygenation -= depletionRate * dt;
                BloodOxygenation = MathF.Max(0f, BloodOxygenation);
            }
            else
            {
                // Recover oxygen
                BloodOxygenation += 0.05f * dt;
                BloodOxygenation = MathF.Min(1.0f, BloodOxygenation);
            }
        }

        private void CalculateRespiratoryDamage(BodyPart part, ref float airwayTotal, ref float airwayDest, ref float lungTotal, ref float lungDest)
        {
            foreach (var voxel in part.Voxels)
            {
                if (voxel.Organ == OrganType.Airway)
                {
                    airwayTotal += 1f;
                    if (voxel.IsDestroyed) airwayDest += 1f;
                }
                else if (voxel.Organ == OrganType.Lung)
                {
                    lungTotal += 1f;
                    if (voxel.IsDestroyed) lungDest += 1f;
                }
            }
            foreach (var child in part.Children)
            {
                CalculateRespiratoryDamage(child, ref airwayTotal, ref airwayDest, ref lungTotal, ref lungDest);
            }
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

            // Hypoxia override
            if (BloodOxygenation < 0.90f)
            {
                // Tachycardia from hypoxia
                HeartRateBpm = MathF.Max(HeartRateBpm, 120f + ((0.90f - BloodOxygenation) / 0.30f) * 40f);
            }

            if (BloodOxygenation < 0.80f)
            {
                ConsciousnessLevel = MathF.Min(ConsciousnessLevel, 0.8f);
            }
            if (BloodOxygenation < 0.60f)
            {
                ConsciousnessLevel = MathF.Min(ConsciousnessLevel, 0.2f);
            }
            if (BloodOxygenation < 0.40f)
            {
                // Hypoxic arrest
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
                ConsciousnessLevel = 0f;
            }
            
            // Fatal hemorrhage check ensures dead stats stay zero
            if (CurrentHemorrhageClass == HemorrhageClass.Fatal)
            {
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
                ConsciousnessLevel = 0f;
            }
        }

        private void UpdateNervousSystemState(float dt)
        {
            float rawPain = CalculateNociception(RootBodyPart);
            
            if (_analgesicLevel > 0)
            {
                _analgesicLevel -= 0.01f * dt; // Slow decay
                _analgesicLevel = MathF.Max(0f, _analgesicLevel);
            }
            
            float endorphinBuffer = 0.1f;
            
            PainLevel = MathF.Max(0f, rawPain - endorphinBuffer - _analgesicLevel);
            PainLevel = MathF.Min(1.0f, PainLevel);
            
            float bloodLossRatio = (_baselineBloodVolume - TotalBloodVolume) / _baselineBloodVolume;
            ShockLevel = MathF.Min(1.0f, (PainLevel * 0.5f) + (bloodLossRatio * 2.0f));
            
        }

        private float CalculateNociception(BodyPart part)
        {
            float pain = 0f;
            foreach (var voxel in part.Voxels)
            {
                if (voxel.IsDestroyed || voxel.DepositedEnergy > 0)
                {
                    pain += (voxel.DepositedEnergy * voxel.Tissue.PainReceptorDensity) * 0.001f;
                }
            }
            foreach (var child in part.Children)
            {
                pain += CalculateNociception(child);
            }
            return pain;
        }

        public void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint)
        {
            RootBodyPart.ApplyTrauma(hitPoint, kineticEnergy);
        }
    }
}
