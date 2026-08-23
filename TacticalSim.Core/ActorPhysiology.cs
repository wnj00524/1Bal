using System;
using System.Collections.Generic;
using System.Numerics;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Damage.Variation;
using TacticalSim.Core.Units;

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

        public FlowRate ArterialBleed => FlowRate.FromMillilitersPerSecond(ArterialBleedRate);
        public FlowRate VenousBleed => FlowRate.FromMillilitersPerSecond(VenousBleedRate);
        
        // Interventions
        public bool HasTourniquet { get; set; }
        public bool HasWoundPacking { get; private set; }
        public float IschemiaDuration { get; set; } // seconds
        public bool IsNecrotic { get; set; }

        public float GetActiveBleedRate()
        {
            if (HasTourniquet && IsExtremity(Type))
            {
                return 0f; // Tourniquet completely halts distal bleeding
            }
            
            FlowRate activeRate = ArterialBleed + VenousBleed;
            
            // Dynamically calculate from destroyed voxels
            foreach (var voxel in Voxels)
            {
                if (voxel.IsDestroyed)
                {
                    float volCc = voxel.VoxelVolume.CubicCentimeters;
                    float rate = voxel.Organ switch
                    {
                        // Cardiac muscle is continuously perfused under arterial
                        // pressure; a destroyed cubic centimeter represents a
                        // catastrophic rather than a generic soft-tissue bleed.
                        OrganType.Heart => 6.0f,
                        OrganType.Liver => 0.02f,
                        OrganType.Spleen => 0.03f, 
                        OrganType.Kidney => 0.03f, 
                        OrganType.Lung => 0.005f,
                        OrganType.Intestines => 0.002f, 
                        OrganType.Muscle => 0.01f, // 100cc muscle tear = 1 ml/sec
                        OrganType.Stomach => 0.002f,
                        OrganType.Bone => 0.015f, // Highly vascular marrow, but not an artery
                        OrganType.Brain => 0.05f, 
                        OrganType.Airway => 0.01f, 
                        OrganType.Mouth => 0.005f, 
                        _ => 0.005f
                    };
                    // Packing and sustained direct pressure can control bleeding from
                    // an accessible abdominal-wall wound. They cannot tamponade a
                    // injured solid organ or other non-compressible internal bleed.
                    if (HasWoundPacking && Type == BodyPartType.Abdomen && voxel.Organ == OrganType.Muscle)
                        rate *= 0.2f;
                    activeRate += FlowRate.FromMillilitersPerSecond(rate * volCc);
                }
            }
            
            return activeRate.MillilitersPerSecond;
        }

        private bool IsExtremity(BodyPartType type) =>
            type == BodyPartType.LeftArm || type == BodyPartType.RightArm ||
            type == BodyPartType.LeftLeg || type == BodyPartType.RightLeg;

        internal bool TryApplyTourniquet()
        {
            if (!IsExtremity(Type) || HasTourniquet || GetActiveBleedRate() <= 0f)
                return false;

            HasTourniquet = true;
            return true;
        }

        internal bool TryApplyWoundPacking()
        {
            if (Type != BodyPartType.Abdomen || HasWoundPacking ||
                !Voxels.Exists(voxel => voxel.IsDestroyed && voxel.Organ == OrganType.Muscle))
                return false;

            HasWoundPacking = true;
            return true;
        }

        /// <summary>
        /// Applies the pre-M5 point-trauma behavior for explicit model comparison.
        /// The full input energy can be deposited into more than one nearby voxel,
        /// so authoritative callers must use IProjectileInteractionService instead.
        /// </summary>
        internal void ApplyLegacyTrauma(
            Vector3 impactPoint,
            Energy kineticEnergy,
            DamageModelVersion modelVersion)
        {
            if (modelVersion != DamageModelVersion.LegacyV1)
                throw new ArgumentException("Point trauma is available only under the legacy-v1 feature flag.", nameof(modelVersion));

            // Simplified trauma routing to voxels
            foreach (var voxel in Voxels)
            {
                if (!voxel.IsDestroyed && Vector3.Distance(voxel.Center, impactPoint) < voxel.Size)
                {
                    voxel.ApplyKineticEnergy(kineticEnergy.Joules, impactPoint);
                }
            }
            foreach (var child in Children)
            {
                child.ApplyLegacyTrauma(impactPoint, kineticEnergy, modelVersion);
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
        float SystemicBleedRateMlPerSecond { get; }
        float BreathingRatePerMinute { get; }
        float AutonomicDrive { get; }
        float BrainstemFunction { get; }
        float AutonomicNerveFunction { get; }
        HemorrhageClass CurrentHemorrhageClass { get; }

        // Respiratory System
        float BloodOxygenation { get; } // 1.0 down to 0.0
        float AirwayObstruction { get; } // 0.0 to 1.0
        float AirwayPatency { get; } // 1.0 down to 0.0
        float VentilationEffectiveness { get; } // effective movement of air, 1.0 down to 0.0
        float AlveolarBloodAccumulation { get; } // ml
        float TensionPneumothoraxLevel { get; } // 0.0 to 1.0
        bool HasChestSeal { get; }

        // Circulatory and cerebral oxygen delivery
        float CirculationEffectiveness { get; } // systemic perfusion, 1.0 down to 0.0
        float CerebralOxygenation { get; } // oxygen delivery to the brain, 1.0 down to 0.0
        float BrainHypoxiaSeconds { get; }
        bool IsDead { get; }
        
        // Nervous System
        float PainLevel { get; }
        float ShockLevel { get; }
        float AnalgesicLevel { get; }
        
        // Motor System
        float MobilityLevel { get; } // 1.0 down to 0.0
        float WeaponHandlingLevel { get; } // 1.0 down to 0.0
        bool CanStand => MobilityLevel > 0f;
        
        void AdministerAnalgesic(float strength);
        void ApplyChestSeal();
        void PerformNeedleDecompression();
        bool ApplyTourniquet(BodyPartType extremity);
        bool PackExternalWound(BodyPartType bodyPart);
        void TickPhysiology(float dt);
        /// <summary>
        /// Runs the deprecated point-trauma path for an explicit legacy comparison.
        /// New impacts must be resolved by IProjectileInteractionService.
        /// </summary>
        void ProcessLegacyImpact(
            Vector3 trajectory,
            Energy kineticEnergy,
            Vector3 hitPoint,
            DamageModelVersion modelVersion);
    }

    public class TacticalActorPhysiology :
        IActorPhysiology,
        IAnatomicalInjuryTarget,
        IMusculoskeletalFunctionalTarget,
        INeurologicalFunctionalTarget
    {
        private readonly IMusculoskeletalFunctionalResolver _musculoskeletalFunctionalResolver;
        private readonly INeurologicalFunctionalResolver _neurologicalFunctionalResolver;
        private readonly float _baselineHeartRateBpm = 80f;
        private readonly float _baselineMeanArterialPressureMmhg = 93f;
        private readonly float _stressResponseMultiplier = 1f;
        public CasualtyProfile Profile { get; }

        public IAnatomicalStructureCatalog Anatomy { get; private set; } = new AnatomicalStructureCatalog([]);
        public ILesionRepository LesionRepository { get; } = new LesionRepository();
        public MusculoskeletalFunctionalState MusculoskeletalFunctionalState { get; private set; } =
            MusculoskeletalFunctionalState.Healthy;
        public NeurologicalFunctionalState NeurologicalFunctionalState { get; private set; } =
            NeurologicalFunctionalState.Healthy;
        public BodyPart RootBodyPart { get; private set; } = null!;
        public float TotalBloodVolume { get; private set; } = 5000f; // 5L baseline
        private float _baselineBloodVolume = 5000f;
        public float ConsciousnessLevel { get; private set; } = 1.0f;
        
        public float HeartRateBpm { get; private set; } = 80f;
        public float MeanArterialPressureMmhg { get; private set; } = 93f; // 120/80
        public float SystemicBleedRateMlPerSecond
        {
            get
            {
                float pressureFlowFactor = MathF.Sqrt(Math.Clamp(
                    MeanArterialPressureMmhg / _baselineMeanArterialPressureMmhg, 0f, 1f));
                return CalculateBleedRate(RootBodyPart, out _) * pressureFlowFactor;
            }
        }
        public float BreathingRatePerMinute { get; private set; } = 12f;
        public float AutonomicDrive { get; private set; } = 1f;
        public float BrainstemFunction { get; private set; } = 1f;
        public float AutonomicNerveFunction { get; private set; } = 1f;
        public HemorrhageClass CurrentHemorrhageClass { get; private set; } = HemorrhageClass.Class1;

        public float BloodOxygenation { get; private set; } = 1.0f;
        public float AirwayObstruction { get; private set; } = 0f;
        public float AirwayPatency => 1f - AirwayObstruction;
        public float VentilationEffectiveness { get; private set; } = 1f;
        public float AlveolarBloodAccumulation { get; private set; } = 0f;
        public float TensionPneumothoraxLevel { get; private set; } = 0f;
        public bool HasChestSeal { get; private set; }
        public float CirculationEffectiveness { get; private set; } = 1f;
        public float CerebralOxygenation { get; private set; } = 1f;
        public float BrainHypoxiaSeconds { get; private set; }
        public bool IsDead { get; private set; }

        public float PainLevel { get; private set; } = 0f;
        public float ShockLevel { get; private set; } = 0f;
        public float AnalgesicLevel => _analgesicLevel;
        
        public float MobilityLevel { get; private set; } = 1.0f;
        public float WeaponHandlingLevel { get; private set; } = 1.0f;
        public bool CanStand => MusculoskeletalFunctionalState.CanStand
            && NeurologicalFunctionalState.LowerLimbCapacity > 0f;

        private float _analgesicLevel = 0f;
        private float _cardiacFunction = 1f;
        private float _hypoxicBrainFunction = 1f;
        private float _legacyVoxelMobilityLevel = 1f;
        private float _legacyVoxelWeaponHandlingLevel = 1f;

        public TacticalActorPhysiology()
            : this(new MusculoskeletalFunctionalResolver(), new NeurologicalFunctionalResolver(), CasualtyProfile.Default)
        {
        }

        public TacticalActorPhysiology(
            IMusculoskeletalFunctionalResolver musculoskeletalFunctionalResolver)
            : this(musculoskeletalFunctionalResolver, new NeurologicalFunctionalResolver(), CasualtyProfile.Default)
        {
        }

        public TacticalActorPhysiology(
            IMusculoskeletalFunctionalResolver musculoskeletalFunctionalResolver,
            INeurologicalFunctionalResolver neurologicalFunctionalResolver,
            CasualtyProfile? profile = null,
            PhysiologicalVariation? variation = null)
        {
            _musculoskeletalFunctionalResolver = musculoskeletalFunctionalResolver
                ?? throw new ArgumentNullException(nameof(musculoskeletalFunctionalResolver));
            _neurologicalFunctionalResolver = neurologicalFunctionalResolver
                ?? throw new ArgumentNullException(nameof(neurologicalFunctionalResolver));
            Profile = profile ?? CasualtyProfile.Default;
            Profile.Validate();
            variation ??= new PhysiologicalVariation(1f, 0f, 0f, 1f);
            _baselineBloodVolume = Profile.BloodVolumeMilliliters * variation.BloodVolumeMultiplier;
            TotalBloodVolume = _baselineBloodVolume;
            _baselineHeartRateBpm = Profile.BaselineHeartRateBpm + variation.HeartRateOffsetBpm;
            _baselineMeanArterialPressureMmhg = Profile.BaselineMeanArterialPressureMmhg + variation.PressureOffsetMmhg;
            _stressResponseMultiplier = variation.StressResponseMultiplier * (Profile.StressResponse switch
            {
                StressResponseProfile.Blunted => 0.8f,
                StressResponseProfile.Heightened => 1.2f,
                _ => 1f
            });
            HeartRateBpm = _baselineHeartRateBpm;
            MeanArterialPressureMmhg = _baselineMeanArterialPressureMmhg;
        }

        public void SetRoot(BodyPart root)
        {
            RootBodyPart = root;
        }

        public void SetAnatomy(IAnatomicalStructureCatalog anatomy) =>
            Anatomy = anatomy ?? throw new ArgumentNullException(nameof(anatomy));

        public void AdministerAnalgesic(float strength)
        {
            _analgesicLevel = Math.Clamp(_analgesicLevel + MathF.Max(0f, strength), 0f, 1f);
        }

        public void ApplyChestSeal()
        {
            HasChestSeal = true;
        }

        public void PerformNeedleDecompression()
        {
            TensionPneumothoraxLevel = 0f;
        }

        public bool ApplyTourniquet(BodyPartType extremity) =>
            FindBodyPart(RootBodyPart, extremity)?.TryApplyTourniquet() ?? false;

        public bool PackExternalWound(BodyPartType bodyPart) =>
            FindBodyPart(RootBodyPart, bodyPart)?.TryApplyWoundPacking() ?? false;

        private static BodyPart? FindBodyPart(BodyPart part, BodyPartType type)
        {
            if (part.Type == type)
                return part;
            foreach (BodyPart child in part.Children)
            {
                BodyPart? match = FindBodyPart(child, type);
                if (match != null)
                    return match;
            }
            return null;
        }

        public void TickPhysiology(float dt)
        {
            float elapsedSeconds = MathF.Max(0f, dt);
            float totalBleedRate = CalculateBleedRate(RootBodyPart, out float airwayBleedRate);
            // Flow through an open vessel falls with perfusion pressure.  The
            // square-root relationship is the standard orifice-flow approximation;
            // it also prevents a hypotensive or arrested casualty bleeding as if
            // their circulation were still at the normal 93 mmHg MAP.
            float pressureFlowFactor = MathF.Sqrt(Math.Clamp(
                MeanArterialPressureMmhg / _baselineMeanArterialPressureMmhg, 0f, 1f));
            float requestedBloodLoss = totalBleedRate * pressureFlowFactor * elapsedSeconds;
            float actualBloodLoss = Math.Clamp(requestedBloodLoss, 0f, TotalBloodVolume);
            TotalBloodVolume = Math.Clamp(
                TotalBloodVolume - actualBloodLoss, 0f, _baselineBloodVolume);

            // ABC: Airway bleeding pools into the lungs
            if (airwayBleedRate > 0f && actualBloodLoss > 0f)
            {
                float lungCapacityMl = CalculateLungVolumeMl(RootBodyPart);
                // Some focused test/medical models omit lung voxels.  Retain the
                // established adult functional flooding capacity in that case.
                if (lungCapacityMl <= 0f)
                    lungCapacityMl = 500f;

                float airwayShare = totalBleedRate > 0f
                    ? Math.Clamp(airwayBleedRate / totalBleedRate, 0f, 1f)
                    : 0f;
                float airwayBloodLoss = actualBloodLoss * airwayShare;
                float bloodLostFromCirculation = _baselineBloodVolume - TotalBloodVolume;
                float alveolarLimit = MathF.Min(lungCapacityMl, bloodLostFromCirculation);
                AlveolarBloodAccumulation = Math.Clamp(
                    AlveolarBloodAccumulation + airwayBloodLoss, 0f, alveolarLimit);
            }

            TickIschemia(RootBodyPart, dt);
            UpdateAutonomicControl();
            UpdateCardiovascularState();
            UpdateRespiratoryState(dt);
            UpdateCerebralState(dt);
            UpdateNervousSystemState(dt);
            UpdateMotorState();

            // Unconsciousness can be temporary, but complete circulatory and
            // oxygen failure is terminal. Latch death so later state updates can
            // never present a terminal casualty as merely unconscious.
            if (HeartRateBpm <= 0f && BloodOxygenation <= 0f)
                IsDead = true;
        }

        private static float CalculateLungVolumeMl(BodyPart part)
        {
            float volumeMl = 0f;
            foreach (var voxel in part.Voxels)
            {
                if (voxel.Organ == OrganType.Lung)
                    volumeMl += voxel.Size * voxel.Size * voxel.Size * 1_000_000f;
            }

            foreach (var child in part.Children)
                volumeMl += CalculateLungVolumeMl(child);

            return volumeMl;
        }

        private void UpdateAutonomicControl()
        {
            BrainstemFunction = CalculateOrganFunction(OrganType.Brain);
            AutonomicNerveFunction = CalculateOrganFunction(OrganType.AutonomicNerve);
            AutonomicDrive = BrainstemFunction * AutonomicNerveFunction * _hypoxicBrainFunction;
            _cardiacFunction = CalculateOrganFunction(OrganType.Heart);
            BreathingRatePerMinute = 12f * AutonomicDrive;
        }

        private float CalculateOrganFunction(OrganType organ)
        {
            float total = 0f;
            float destroyed = 0f;
            CountOrganVoxels(RootBodyPart, organ, ref total, ref destroyed);
            return total > 0f ? Math.Clamp(1f - (destroyed / total), 0f, 1f) : 1f;
        }

        private static void CountOrganVoxels(
            BodyPart part,
            OrganType organ,
            ref float total,
            ref float destroyed)
        {
            foreach (var voxel in part.Voxels)
            {
                if (voxel.Organ != organ)
                    continue;

                total += 1f;
                if (voxel.IsDestroyed)
                    destroyed += 1f;
            }

            foreach (var child in part.Children)
                CountOrganVoxels(child, organ, ref total, ref destroyed);
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
                _legacyVoxelMobilityLevel = MathF.Max(0f, 1.0f - (boneLoss * 5.0f) - (muscleLoss * 2.0f));
            }
            
            if (armBoneTotal > 0)
            {
                float boneLoss = armBoneDest / armBoneTotal;
                float muscleLoss = armMuscleTotal > 0 ? (armMuscleDest / armMuscleTotal) : 0f;
                _legacyVoxelWeaponHandlingLevel = MathF.Max(0f, 1.0f - (boneLoss * 5.0f) - (muscleLoss * 2.0f));
            }

            RefreshMusculoskeletalFunctionalState();
            RefreshNeurologicalFunctionalState();
        }

        /// <summary>
        /// Re-resolves the direct fracture-to-function bridge after a discrete
        /// lesion change. Time-dependent physiology remains tick-driven.
        /// </summary>
        public void RefreshMusculoskeletalFunctionalState()
        {
            MusculoskeletalFunctionalState fractureState = _musculoskeletalFunctionalResolver.Resolve(
                LesionRepository.Lesions,
                Anatomy);

            float standingCapacity = MathF.Min(
                _legacyVoxelMobilityLevel,
                fractureState.StandingCapacity);
            float movementCapacity = MathF.Min(
                _legacyVoxelMobilityLevel,
                fractureState.MovementCapacity);
            float upperLimbCapacity = MathF.Min(
                _legacyVoxelWeaponHandlingLevel,
                fractureState.UpperLimbCapacity);

            MusculoskeletalFunctionalState = new MusculoskeletalFunctionalState(
                standingCapacity,
                movementCapacity,
                upperLimbCapacity,
                fractureState.CanStand && standingCapacity > 0f);
            MobilityLevel = movementCapacity;
            WeaponHandlingLevel = upperLimbCapacity;
        }

        /// <summary>Re-resolves level-, side-, and limb-specific nerve consequences.</summary>
        public void RefreshNeurologicalFunctionalState()
        {
            NeurologicalFunctionalState = _neurologicalFunctionalResolver.Resolve(
                LesionRepository.Lesions,
                Anatomy);
            MobilityLevel = MathF.Min(
                MusculoskeletalFunctionalState.MovementCapacity,
                NeurologicalFunctionalState.LowerLimbCapacity);
            WeaponHandlingLevel = MathF.Min(
                MusculoskeletalFunctionalState.UpperLimbCapacity,
                NeurologicalFunctionalState.UpperLimbCapacity);
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

            // A penetrating lung injury leaks air into the pleural cavity. A chest
            // seal prevents further ingress; decompression is required to remove
            // pressure that has already accumulated.
            if (destroyedLungVoxels > 0f && !HasChestSeal)
            {
                float punctureFraction = destroyedLungVoxels / totalLungVoxels;
                TensionPneumothoraxLevel = MathF.Min(1f,
                    TensionPneumothoraxLevel + (punctureFraction * 0.02f * dt));
            }

            // 4. Respiration Effectiveness
            VentilationEffectiveness = AirwayPatency
                * remainingCapacity
                * (1.0f - TensionPneumothoraxLevel)
                * AutonomicDrive;

            // Gas exchange and tissue oxygen delivery require both ventilation
            // and blood flow. A patent airway cannot oxygenate a patient in
            // cardiac arrest, and circulation cannot compensate for apnoea.
            float oxygenDelivery = VentilationEffectiveness * CirculationEffectiveness;

            // 5. Hypoxia Calculation
            if (oxygenDelivery < 0.8f) // Demand threshold
            {
                // Deplete oxygen
                float depletionRate = (0.8f - oxygenDelivery) * 0.05f; // Drops SpO2 over time
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

        private void UpdateCerebralState(float dt)
        {
            CerebralOxygenation = BloodOxygenation * CirculationEffectiveness;

            if (CerebralOxygenation < 0.6f)
            {
                BrainHypoxiaSeconds += (0.6f - CerebralOxygenation) / 0.6f * dt;
            }
            else
            {
                // A short hypoxic episode can recover, while accumulated injury
                // takes substantially longer to clear than it took to acquire.
                BrainHypoxiaSeconds = MathF.Max(0f, BrainHypoxiaSeconds - (0.1f * dt));
            }

            // Neurological control begins failing after roughly ten equivalent
            // seconds of profound cerebral hypoxia and is absent by sixty.
            _hypoxicBrainFunction = Math.Clamp(
                1f - ((BrainHypoxiaSeconds - 10f) / 50f), 0f, 1f);
            UpdateAutonomicControl();

            // Loss of cerebral perfusion is not an instantaneous binary switch:
            // usable oxygen remains briefly before consciousness falls away.
            float hypoxicConsciousness = Math.Clamp(
                1f - ((BrainHypoxiaSeconds - 5f) / 10f), 0f, 1f);
            ConsciousnessLevel = MathF.Min(ConsciousnessLevel, hypoxicConsciousness);
            if (AutonomicDrive <= 0f)
                ConsciousnessLevel = 0f;
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
            bool cardiacArrest = false;
            
            if (lostPercent < 0.15f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class1;
                HeartRateBpm = _baselineHeartRateBpm + (lostPercent / 0.15f) * 20f * _stressResponseMultiplier;
                MeanArterialPressureMmhg = _baselineMeanArterialPressureMmhg;
                ConsciousnessLevel = 1.0f;
            }
            else if (lostPercent < 0.30f)
            {
                CurrentHemorrhageClass = HemorrhageClass.Class2;
                HeartRateBpm = _baselineHeartRateBpm + 20f * _stressResponseMultiplier + ((lostPercent - 0.15f) / 0.15f) * 20f * _stressResponseMultiplier;
                MeanArterialPressureMmhg = _baselineMeanArterialPressureMmhg - ((lostPercent - 0.15f) / 0.15f) * 13f;
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
                cardiacArrest = true;
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
                cardiacArrest = true;
            }
            
            // Fatal hemorrhage check ensures dead stats stay zero
            if (CurrentHemorrhageClass == HemorrhageClass.Fatal)
            {
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
                ConsciousnessLevel = 0f;
            }

            if (!cardiacArrest)
            {
                // A denervated, otherwise viable heart retains its intrinsic
                // pacemaker rhythm (about 100 BPM); autonomic control normally
                // slows that rhythm at rest and produces tachycardia in response
                // to hemorrhage or hypoxia. Interpolating from the intrinsic rate
                // therefore blunts the *change* in rate as autonomic drive is lost,
                // instead of scaling the whole heart rate toward zero while still
                // showing an apparently intact compensatory response.
                const float intrinsicPacemakerRateBpm = 100f;
                HeartRateBpm = intrinsicPacemakerRateBpm
                    + ((HeartRateBpm - intrinsicPacemakerRateBpm) * AutonomicDrive);
            }

            float heartRateBeforeCardiacDamage = HeartRateBpm;
            HeartRateBpm *= _cardiacFunction;
            // MAP depends on cardiac output (heart rate x stroke volume).  Blood
            // volume is already represented by the hemorrhage-class pressure
            // curve above; this ratio adds the missing effect of an impaired rate
            // without double-counting hypovolemia in stroke volume.
            float heartRateOutputFactor = heartRateBeforeCardiacDamage > 0f
                ? Math.Clamp(HeartRateBpm / heartRateBeforeCardiacDamage, 0f, 1.25f)
                : 0f;
            MeanArterialPressureMmhg *= AutonomicDrive * heartRateOutputFactor;

            if (HeartRateBpm <= 0f)
            {
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
            }

            if (AutonomicDrive <= 0f)
                ConsciousnessLevel = 0f;

            if (IsDead)
            {
                HeartRateBpm = 0f;
                MeanArterialPressureMmhg = 0f;
                ConsciousnessLevel = 0f;
            }

            CirculationEffectiveness = Math.Clamp(MeanArterialPressureMmhg / _baselineMeanArterialPressureMmhg, 0f, 1f);
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

        public void ProcessLegacyImpact(
            Vector3 trajectory,
            Energy kineticEnergy,
            Vector3 hitPoint,
            DamageModelVersion modelVersion)
        {
            if (trajectory.LengthSquared() <= 0f)
                throw new ArgumentOutOfRangeException(nameof(trajectory), "A legacy impact trajectory must be non-zero.");
            if (kineticEnergy.Joules <= 0f)
                throw new ArgumentOutOfRangeException(nameof(kineticEnergy), "Legacy impact energy must be positive.");

            RootBodyPart.ApplyLegacyTrauma(hitPoint, kineticEnergy, modelVersion);
        }
    }
}
