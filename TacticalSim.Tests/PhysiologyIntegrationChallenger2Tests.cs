using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TacticalSim.Core;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Adversarial Stress & Empirical Challenge Suite for Physiological Integration,
    /// Trauma Progression, Tourniquet Ischemia (7200s), and Automatic Action Cancellation.
    /// Authored and executed by Challenger 2.
    /// </summary>
    public class PhysiologyIntegrationChallenger2Tests
    {
        #region Test Setup Helpers

        private static (TacticalEntity entity, TacticalActorPhysiology physiology, BodyPart root) CreateCustomEntity(
            float arterialBleed = 0f,
            float venousBleed = 0f,
            BodyPartType bodyPartType = BodyPartType.Thorax,
            bool hasTourniquet = false)
        {
            var physiology = new TacticalActorPhysiology();
            var root = new BodyPart
            {
                Type = bodyPartType,
                ArterialBleedRate = arterialBleed,
                VenousBleedRate = venousBleed,
                HasTourniquet = hasTourniquet
            };
            physiology.SetRoot(root);
            var entity = new TacticalEntity(Vector3.Zero, physiology);
            return (entity, physiology, root);
        }

        #endregion

        #region 1. Physiological Integration (IActorPhysiology.TickPhysiology)

        [Fact]
        public void Physiology_DeepHierarchicalBodyPartTree_AggregatesBleedRatesAndIschemiaAccurately()
        {
            var resolver = new TurnResolver();
            var physiology = new TacticalActorPhysiology();

            // Build a 10-level nested hierarchy:
            // Thorax -> Abdomen -> LeftLeg (Hip) -> Thigh -> Knee -> Shin -> Ankle -> Foot -> Metatarsal -> Toe
            var root = new BodyPart { Type = BodyPartType.Thorax, ArterialBleedRate = 2.0f, VenousBleedRate = 1.0f }; // 3 ml/s
            var abdomen = new BodyPart { Type = BodyPartType.Abdomen, ArterialBleedRate = 4.0f, VenousBleedRate = 2.0f, Parent = root }; // 6 ml/s
            root.Children.Add(abdomen);

            var hip = new BodyPart { Type = BodyPartType.LeftLeg, ArterialBleedRate = 1.0f, VenousBleedRate = 0.5f, Parent = abdomen }; // 1.5 ml/s
            abdomen.Children.Add(hip);

            var current = hip;
            var extremityNodes = new List<BodyPart> { hip };
            for (int i = 0; i < 7; i++)
            {
                var next = new BodyPart
                {
                    Type = BodyPartType.LeftLeg,
                    ArterialBleedRate = 1.0f,
                    VenousBleedRate = 0.5f,
                    Parent = current
                };
                current.Children.Add(next);
                extremityNodes.Add(next);
                current = next;
            }

            physiology.SetRoot(root);
            var entity = new TacticalEntity(Vector3.Zero, physiology);
            resolver.RegisterEntity(entity);

            // Total bleed: 3 (Thorax) + 6 (Abdomen) + 8 * 1.5 (Leg nodes) = 3 + 6 + 12 = 21 ml/s
            // Tick 10s: 21 * 10 = 210 ml lost -> TotalBloodVolume = 4790 ml
            resolver.Tick(10.0f);
            Assert.Equal(4790f, physiology.TotalBloodVolume, 2);

            // Apply tourniquet on hip (the root of the extremity subtree)
            hip.HasTourniquet = true;

            // Hip active bleed rate becomes 0 because it has tourniquet and is extremity.
            // But what about children? Children without HasTourniquet still report bleed unless tourniquet halts entire subtree.
            // Per BodyPart.GetActiveBleedRate(): HasTourniquet on that part halts its own bleed.
            // Let's verify: Thorax(3) + Abdomen(6) + Hip(0) + 7 children(7*1.5 = 10.5) = 19.5 ml/s.
            resolver.Tick(10.0f);
            Assert.Equal(4790f - (19.5f * 10f), physiology.TotalBloodVolume, 2);
            Assert.Equal(10.0f, hip.IschemiaDuration, 2);
        }

        [Fact]
        public void Physiology_MicroStepPrecision_OneThousandSteps_MatchesSingleMacroStep()
        {
            float bleedRate = 12.345f;
            float totalDuration = 100.0f;
            int stepCount = 1_000;
            float dt = totalDuration / stepCount; // 0.1s

            // Macro-step resolver
            var macroResolver = new TurnResolver();
            var (macroEntity, macroPhys, _) = CreateCustomEntity(arterialBleed: bleedRate);
            macroResolver.RegisterEntity(macroEntity);
            macroResolver.Tick(totalDuration);

            // Micro-step resolver
            var microResolver = new TurnResolver();
            var (microEntity, microPhys, _) = CreateCustomEntity(arterialBleed: bleedRate);
            microResolver.RegisterEntity(microEntity);

            for (int i = 0; i < stepCount; i++)
            {
                microResolver.Tick(dt);
            }

            float expectedLoss = bleedRate * totalDuration; // 1234.5 ml
            float expectedRemaining = 5000f - expectedLoss; // 3765.5 ml

            Assert.Equal(expectedRemaining, macroPhys.TotalBloodVolume, 2);
            Assert.True(MathF.Abs(expectedRemaining - microPhys.TotalBloodVolume) < 0.1f, $"Micro-step error {MathF.Abs(expectedRemaining - microPhys.TotalBloodVolume)} must be < 0.1 ml");
            Assert.True(MathF.Abs(macroPhys.TotalBloodVolume - microPhys.TotalBloodVolume) < 0.1f);
            Assert.Equal(totalDuration, microResolver.GlobalTime, 2);
        }

        [Theory]
        [InlineData(0.00f, HemorrhageClass.Class1, 80.0f, 93.0f, 1.0f)]
        [InlineData(0.14f, HemorrhageClass.Class1, 98.666f, 93.0f, 1.0f)]
        [InlineData(0.16f, HemorrhageClass.Class2, 101.333f, 92.133f, 0.9f)]
        [InlineData(0.28f, HemorrhageClass.Class2, 117.333f, 81.733f, 0.9f)]
        [InlineData(0.32f, HemorrhageClass.Class3, 124.0f, 76.0f, 0.6f)]
        [InlineData(0.38f, HemorrhageClass.Class3, 136.0f, 64.0f, 0.6f)]
        [InlineData(0.42f, HemorrhageClass.Class4, 132.0f, 54.0f, 0.2f)]
        [InlineData(0.48f, HemorrhageClass.Class4, 108.0f, 36.0f, 0.2f)]
        [InlineData(0.51f, HemorrhageClass.Fatal, 0.0f, 0.0f, 0.0f)]
        [InlineData(0.75f, HemorrhageClass.Fatal, 0.0f, 0.0f, 0.0f)]
        [InlineData(1.00f, HemorrhageClass.Fatal, 0.0f, 0.0f, 0.0f)]
        public void Physiology_CardiovascularStateTransitions_ExactThresholdVerification(
            float lostFraction,
            HemorrhageClass expectedClass,
            float expectedHR,
            float expectedMAP,
            float expectedConsciousness)
        {
            var resolver = new TurnResolver();
            float bloodLossNeeded = 5000f * lostFraction;
            float bleedRate = bloodLossNeeded / 1.0f; // in 1 second

            var (entity, phys, _) = CreateCustomEntity(arterialBleed: bleedRate);
            resolver.RegisterEntity(entity);

            resolver.Tick(1.0f);

            Assert.Equal(expectedClass, phys.CurrentHemorrhageClass);
            Assert.Equal(expectedHR, phys.HeartRateBpm, 1);
            Assert.Equal(expectedMAP, phys.MeanArterialPressureMmhg, 1);
            Assert.Equal(expectedConsciousness, phys.ConsciousnessLevel, 2);
        }

        [Fact]
        public void Physiology_MassiveBleed_UnderflowSafety_DoesNotThrow()
        {
            var resolver = new TurnResolver();
            // Massive catastrophic bleed rate: 1,000,000 ml/s
            var (entity, phys, _) = CreateCustomEntity(arterialBleed: 1_000_000f);
            resolver.RegisterEntity(entity);

            resolver.Tick(1.0f);

            Assert.Equal(HemorrhageClass.Fatal, phys.CurrentHemorrhageClass);
            Assert.Equal(0.0f, phys.ConsciousnessLevel);
            Assert.Equal(0.0f, phys.HeartRateBpm);
            Assert.Equal(0.0f, phys.MeanArterialPressureMmhg);
        }

        #endregion

        #region 2. Trauma Progression & Anatomical Dummy Integration

        [Fact]
        public void Trauma_AnatomicalDummyBuilder_HeartShot_InflictsMassiveArterialBleed()
        {
            var dummy = AnatomicalDummyBuilder.BuildDummy();
            var entity = new TacticalEntity(Vector3.Zero, dummy);
            var resolver = new TurnResolver();
            resolver.RegisterEntity(entity);

            // Heart position in dummy: center at (0.04, 0.28, 0.04)
            var heartHitPoint = new Vector3(0.04f, 0.28f, 0.04f);
            float kineticEnergy = 3500f; // High kinetic energy rifle round

            dummy.ProcessImpact(Vector3.UnitZ, kineticEnergy, heartHitPoint);

            // Verify active bleed rate on root body part increased significantly
            Assert.True(dummy.RootBodyPart.GetActiveBleedRate() > 5.0f, "Heart trauma must produce heavy arterial bleeding.");

            float initialBleed = dummy.RootBodyPart.GetActiveBleedRate();

            // Tick 2.0 seconds in resolver
            resolver.Tick(2.0f);

            float expectedBloodVolume = 5000f - (initialBleed * 2.0f);
            Assert.Equal(expectedBloodVolume, dummy.TotalBloodVolume, 2);
        }

        [Fact]
        public void Trauma_RepeatedImpactsOnDestroyedVoxels_DoesNotCrashOrProduceNaN()
        {
            var dummy = AnatomicalDummyBuilder.BuildDummy();
            var hitPoint = new Vector3(0.0f, 0.25f, 0.0f);

            // Apply 50 consecutive devastating kinetic strikes
            for (int i = 0; i < 50; i++)
            {
                dummy.ProcessImpact(Vector3.UnitZ, 5000f, hitPoint);
            }

            var resolver = new TurnResolver();
            var entity = new TacticalEntity(Vector3.Zero, dummy);
            resolver.RegisterEntity(entity);

            resolver.Tick(1.0f);

            Assert.False(float.IsNaN(dummy.TotalBloodVolume));
            Assert.False(float.IsInfinity(dummy.TotalBloodVolume));
            Assert.True(dummy.TotalBloodVolume < 5000f);
        }

        #endregion

        #region 3. Tourniquet Ischemia Necrosis Threshold (7200s)

        [Theory]
        [InlineData(7199.0f, false)]
        [InlineData(7199.99f, false)]
        [InlineData(7200.0f, false)] // Exact boundary: IschemiaDuration > 7200f is strictly false at 7200.0f
        [InlineData(7200.01f, true)]  // Strictly greater than 7200.0f -> Necrotic
        [InlineData(7201.0f, true)]
        [InlineData(10000.0f, true)]
        public void Tourniquet_Exact7200SecondsBoundary_NecrosisThresholdVerification(float elapsedSeconds, bool expectedNecrotic)
        {
            var resolver = new TurnResolver();
            var (entity, _, arm) = CreateCustomEntity(arterialBleed: 50f, bodyPartType: BodyPartType.RightArm, hasTourniquet: true);
            resolver.RegisterEntity(entity);

            resolver.Tick(elapsedSeconds);

            Assert.Equal(elapsedSeconds, arm.IschemiaDuration, 2);
            Assert.Equal(expectedNecrotic, arm.IsNecrotic);
        }

        [Fact]
        public void Tourniquet_NonExtremityBodyPart_DoesNotHaltBleeding()
        {
            var resolver = new TurnResolver();
            // Tourniquet applied on Thorax (non-extremity)
            var (entity, phys, thorax) = CreateCustomEntity(arterialBleed: 30f, bodyPartType: BodyPartType.Thorax, hasTourniquet: true);
            resolver.RegisterEntity(entity);

            // Active bleed rate should still be 30 ml/s because tourniquet on torso cannot occlude junctional hemorrhage
            Assert.Equal(30f, thorax.GetActiveBleedRate());

            resolver.Tick(10.0f);
            Assert.Equal(4700f, phys.TotalBloodVolume, 2);
            Assert.Equal(10.0f, thorax.IschemiaDuration, 2);
        }

        [Fact]
        public void Tourniquet_FourLimbsStaggeredTourniquets_IndividualNecrosisTransitions()
        {
            var resolver = new TurnResolver();
            var physiology = new TacticalActorPhysiology();
            var thorax = new BodyPart { Type = BodyPartType.Thorax };
            physiology.SetRoot(thorax);

            var leftArm = new BodyPart { Type = BodyPartType.LeftArm, ArterialBleedRate = 10f, Parent = thorax };
            var rightArm = new BodyPart { Type = BodyPartType.RightArm, ArterialBleedRate = 10f, Parent = thorax };
            var leftLeg = new BodyPart { Type = BodyPartType.LeftLeg, ArterialBleedRate = 10f, Parent = thorax };
            var rightLeg = new BodyPart { Type = BodyPartType.RightLeg, ArterialBleedRate = 10f, Parent = thorax };

            thorax.Children.Add(leftArm);
            thorax.Children.Add(rightArm);
            thorax.Children.Add(leftLeg);
            thorax.Children.Add(rightLeg);

            var entity = new TacticalEntity(Vector3.Zero, physiology);
            resolver.RegisterEntity(entity);

            // T=0: Apply tourniquet to LeftArm
            leftArm.HasTourniquet = true;

            // Tick 1000s -> T=1000
            resolver.Tick(1000f);
            // Apply tourniquet to RightArm at T=1000
            rightArm.HasTourniquet = true;

            // Tick 2000s -> T=3000
            resolver.Tick(2000f);
            // Apply tourniquet to LeftLeg at T=3000
            leftLeg.HasTourniquet = true;

            // Tick 4000s -> T=7000
            resolver.Tick(4000f);
            // Apply tourniquet to RightLeg at T=7000
            rightLeg.HasTourniquet = true;

            // At T=7000:
            // LeftArm: 7000s ischemia (not necrotic)
            // RightArm: 6000s ischemia (not necrotic)
            // LeftLeg: 4000s ischemia (not necrotic)
            // RightLeg: 0s ischemia (not necrotic)
            Assert.False(leftArm.IsNecrotic);
            Assert.False(rightArm.IsNecrotic);
            Assert.False(leftLeg.IsNecrotic);
            Assert.False(rightLeg.IsNecrotic);

            // Tick 201s -> T=7201s
            resolver.Tick(201f);
            // LeftArm: 7201s -> NECROTIC!
            // Others still alive
            Assert.True(leftArm.IsNecrotic);
            Assert.False(rightArm.IsNecrotic);
            Assert.False(leftLeg.IsNecrotic);
            Assert.False(rightLeg.IsNecrotic);

            // Tick 1000s -> T=8201s
            resolver.Tick(1000f);
            // RightArm: 7201s -> NECROTIC!
            Assert.True(leftArm.IsNecrotic);
            Assert.True(rightArm.IsNecrotic);
            Assert.False(leftLeg.IsNecrotic);
            Assert.False(rightLeg.IsNecrotic);

            // Tick 2000s -> T=10201s
            resolver.Tick(2000f);
            // LeftLeg: 7201s -> NECROTIC!
            Assert.True(leftArm.IsNecrotic);
            Assert.True(rightArm.IsNecrotic);
            Assert.True(leftLeg.IsNecrotic);
            Assert.False(rightLeg.IsNecrotic);

            // Tick 4000s -> T=14201s
            resolver.Tick(4000f);
            // RightLeg: 7201s -> NECROTIC!
            Assert.True(leftArm.IsNecrotic);
            Assert.True(rightArm.IsNecrotic);
            Assert.True(leftLeg.IsNecrotic);
            Assert.True(rightLeg.IsNecrotic);
        }

        #endregion

        #region 4. Automatic Action Cancellation on Lethal Trauma / Consciousness Loss

        [Fact]
        public void ActionCancellation_LethalTraumaMidExecution_CancelsActiveAndQueuedActionsImmediately()
        {
            var resolver = new TurnResolver();
            // Bleed rate: 3000 ml/s -> Drops from 5000ml to 2000ml in 1 second (60% lost, fatal)
            var (dyingEntity, phys, _) = CreateCustomEntity(arterialBleed: 3000f);
            resolver.RegisterEntity(dyingEntity);

            var act1 = new GenericTacticalAction(dyingEntity.Id, 5.0f);
            var act2 = new GenericTacticalAction(dyingEntity.Id, 5.0f);
            var act3 = new GenericTacticalAction(dyingEntity.Id, 5.0f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);
            resolver.ScheduleAction(act3);

            var cancelEvents = new List<ActionEventArgs>();
            resolver.ActionCancelled += (_, e) => cancelEvents.Add(e);

            resolver.Tick(1.0f);

            Assert.Equal(0.0f, phys.ConsciousnessLevel);
            Assert.Equal(HemorrhageClass.Fatal, phys.CurrentHemorrhageClass);

            Assert.Equal(TacticalActionState.Cancelled, act1.State);
            Assert.Equal(TacticalActionState.Cancelled, act2.State);
            Assert.Equal(TacticalActionState.Cancelled, act3.State);

            Assert.Equal(3, cancelEvents.Count);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Null(resolver.GetCurrentAction(dyingEntity.Id));
            Assert.Empty(resolver.GetQueuedActions(dyingEntity.Id));
        }

        [Fact]
        public void ActionCancellation_MultiActorCasualtyIsolation_FatalBleedDoesNotAffectHealthyActors()
        {
            var resolver = new TurnResolver();

            // 5 Healthy Actors
            var healthyActors = new List<(TacticalEntity entity, GenericTacticalAction action)>();
            for (int i = 0; i < 5; i++)
            {
                var (ent, _, _) = CreateCustomEntity();
                resolver.RegisterEntity(ent);
                var act = new GenericTacticalAction(ent.Id, 3.0f);
                resolver.ScheduleAction(act);
                healthyActors.Add((ent, act));
            }

            // 3 Wounded Actors:
            // Wounded 1: Bleeds out at T=1.0s (3000 ml/s)
            // Wounded 2: Bleeds out at T=2.0s (1500 ml/s)
            // Wounded 3: Bleeds out at T=3.0s (1000 ml/s)
            var (w1, phys1, _) = CreateCustomEntity(arterialBleed: 3000f);
            var (w2, phys2, _) = CreateCustomEntity(arterialBleed: 1500f);
            var (w3, phys3, _) = CreateCustomEntity(arterialBleed: 1000f);

            resolver.RegisterEntity(w1);
            resolver.RegisterEntity(w2);
            resolver.RegisterEntity(w3);

            var actW1 = new GenericTacticalAction(w1.Id, 5.0f);
            var actW2 = new GenericTacticalAction(w2.Id, 5.0f);
            var actW3 = new GenericTacticalAction(w3.Id, 5.0f);

            resolver.ScheduleAction(actW1);
            resolver.ScheduleAction(actW2);
            resolver.ScheduleAction(actW3);

            // Tick 1.0s: w1 dies and action cancels. w2, w3, healthy progress.
            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Cancelled, actW1.State);
            Assert.Equal(TacticalActionState.Executing, actW2.State);
            Assert.Equal(TacticalActionState.Executing, actW3.State);
            foreach (var (_, hAct) in healthyActors)
            {
                Assert.Equal(TacticalActionState.Executing, hAct.State);
                Assert.Equal(1.0f, hAct.ExecutionProgress, 3);
            }

            // Tick 1.0s (T=2.0s): w2 dies and cancels. w3, healthy progress.
            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Cancelled, actW2.State);
            Assert.Equal(TacticalActionState.Executing, actW3.State);
            foreach (var (_, hAct) in healthyActors)
            {
                Assert.Equal(TacticalActionState.Executing, hAct.State);
                Assert.Equal(2.0f, hAct.ExecutionProgress, 3);
            }

            // Tick 1.0s (T=3.0s): w3 dies and cancels. healthy actors COMPLETE.
            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Cancelled, actW3.State);
            foreach (var (_, hAct) in healthyActors)
            {
                Assert.Equal(TacticalActionState.Completed, hAct.State);
                Assert.Equal(3.0f, hAct.CompletionTime!.Value, 3);
            }

            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void ActionCancellation_PreIncapacitatedEntity_ActionScheduled_CancelledOnFirstTick()
        {
            var resolver = new TurnResolver();
            // Pre-incapacitated entity (fatal bleed, blood volume already 0)
            var (deadEntity, phys, _) = CreateCustomEntity(arterialBleed: 5000f);
            resolver.RegisterEntity(deadEntity);

            // Tick once to drain to 0
            resolver.Tick(1.0f);
            Assert.Equal(0.0f, phys.ConsciousnessLevel);

            // Now schedule action on dead entity
            var act = new GenericTacticalAction(deadEntity.Id, 2.0f);
            resolver.ScheduleAction(act);

            // Next tick should immediately cancel the newly scheduled action
            resolver.Tick(0.5f);

            Assert.Equal(TacticalActionState.Cancelled, act.State);
            Assert.False(resolver.HasActiveActions);
            Assert.Null(resolver.GetCurrentAction(deadEntity.Id));
        }

        [Fact]
        public void ActionCancellation_LethalTraumaAppliedInActionCallback_NextTickCancelsTargetActions()
        {
            var resolver = new TurnResolver();

            var (shooter, _, _) = CreateCustomEntity();
            var (target, targetPhys, targetRoot) = CreateCustomEntity();

            resolver.RegisterEntity(shooter);
            resolver.RegisterEntity(target);

            // Target has a 10 TU action
            var targetAction = new GenericTacticalAction(target.Id, 10.0f);
            resolver.ScheduleAction(targetAction);

            // Shooter fires at T=1.0s, dealing fatal arterial bleed to target in callback
            var shootAction = new GenericTacticalAction(shooter.Id, 1.0f, onComplete: () =>
            {
                targetRoot.ArterialBleedRate = 5000f; // Fatal hemorrhage
            });
            resolver.ScheduleAction(shootAction);

            // Tick 1.0s: shootAction completes, inflicting 5000 ml/s bleed on target
            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, shootAction.State);
            Assert.Equal(TacticalActionState.Executing, targetAction.State);

            // Tick next 1.0s: target physiology ticks with 5000 ml/s bleed -> dies -> targetAction cancelled!
            resolver.Tick(1.0f);
            Assert.Equal(0.0f, targetPhys.ConsciousnessLevel);
            Assert.Equal(TacticalActionState.Cancelled, targetAction.State);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tourniquet_DynamicLooseningAndReapplication_AccumulatesIschemiaCorrectly()
        {
            var resolver = new TurnResolver();
            var (entity, phys, arm) = CreateCustomEntity(arterialBleed: 50f, bodyPartType: BodyPartType.LeftArm, hasTourniquet: true);
            resolver.RegisterEntity(entity);

            // T=0 -> T=3000s with tourniquet on
            resolver.Tick(3000f);
            Assert.Equal(5000f, phys.TotalBloodVolume);
            Assert.Equal(3000f, arm.IschemiaDuration);
            Assert.False(arm.IsNecrotic);

            // Loosen tourniquet at T=3000s for 20s (reperfusion / re-bleed)
            arm.HasTourniquet = false;
            resolver.Tick(20f);
            // Lost: 50 ml/s * 20s = 1000 ml
            Assert.Equal(4000f, phys.TotalBloodVolume, 2);
            // Ischemia did not advance during loosened period
            Assert.Equal(3000f, arm.IschemiaDuration);
            Assert.False(arm.IsNecrotic);

            // Re-apply tourniquet at T=3020s and advance 4199s (total ischemia = 3000 + 4199 = 7199s)
            arm.HasTourniquet = true;
            resolver.Tick(4199f);
            Assert.Equal(4000f, phys.TotalBloodVolume, 2);
            Assert.Equal(7199f, arm.IschemiaDuration);
            Assert.False(arm.IsNecrotic);

            // Advance 2s more (total ischemia = 7201s > 7200s)
            resolver.Tick(2f);
            Assert.Equal(7201f, arm.IschemiaDuration);
            Assert.True(arm.IsNecrotic);
        }

        [Fact]
        public void EntityUnregistration_MidSimulationWhileBleeding_HaltsEntityTickingWithoutAffectingOthers()
        {
            var resolver = new TurnResolver();
            var (e1, phys1, _) = CreateCustomEntity(arterialBleed: 10f);
            var (e2, phys2, _) = CreateCustomEntity(arterialBleed: 20f);

            resolver.RegisterEntity(e1);
            resolver.RegisterEntity(e2);

            var act1 = new GenericTacticalAction(e1.Id, 10.0f);
            var act2 = new GenericTacticalAction(e2.Id, 10.0f);
            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);

            // Tick 2.0s
            resolver.Tick(2.0f);
            Assert.Equal(4980f, phys1.TotalBloodVolume, 2);
            Assert.Equal(4960f, phys2.TotalBloodVolume, 2);

            // Unregister e1 mid-simulation
            bool unregistered = resolver.UnregisterEntity(e1.Id);
            Assert.True(unregistered);

            // Tick 2.0s more
            resolver.Tick(2.0f);
            // e1 should NOT have ticked: remains 4980f
            Assert.Equal(4980f, phys1.TotalBloodVolume, 2);
            // e2 continues ticking: 4960 - 40 = 4920f
            Assert.Equal(4920f, phys2.TotalBloodVolume, 2);
            // e2 action progresses to 4.0 TU
            Assert.Equal(4.0f, act2.ExecutionProgress, 2);
        }

        #endregion

        #region 5. High-Stress Randomized Invariant Fuzzing

        [Fact]
        public void RandomizedFuzz_5000Iterations_PhysiologyTimelineInvariantsHoldStrictly()
        {
            var rng = new Random(1337);
            const int iterationCount = 100; // 100 simulation runs with 50 ticks each = 5000 total ticks

            for (int run = 0; run < iterationCount; run++)
            {
                var resolver = new TurnResolver();
                int actorCount = rng.Next(3, 15);
                var actors = new List<(TacticalEntity entity, TacticalActorPhysiology phys, BodyPart root)>();

                for (int a = 0; a < actorCount; a++)
                {
                    bool isLimb = rng.NextDouble() > 0.5;
                    var partType = isLimb ? (BodyPartType)rng.Next(3, 7) : (BodyPartType)rng.Next(0, 3);
                    float artBleed = (float)(rng.NextDouble() * 50.0);
                    float venBleed = (float)(rng.NextDouble() * 20.0);
                    bool hasTq = isLimb && (rng.NextDouble() > 0.6);

                    var (entity, phys, root) = CreateCustomEntity(artBleed, venBleed, partType, hasTq);
                    resolver.RegisterEntity(entity);
                    actors.Add((entity, phys, root));

                    // Schedule 1 to 5 random actions
                    int actionsCount = rng.Next(1, 6);
                    for (int k = 0; k < actionsCount; k++)
                    {
                        float cost = (float)(rng.NextDouble() * 3.0 + 0.1);
                        resolver.ScheduleAction(new GenericTacticalAction(entity.Id, cost));
                    }
                }

                // Step 50 ticks per run with random delta times
                float lastTime = 0f;
                for (int step = 0; step < 50; step++)
                {
                    float dt = (float)(rng.NextDouble() * 0.5 + 0.01);
                    resolver.Tick(dt);

                    // INVARIANT 1: Monotonic global timeline
                    Assert.True(resolver.GlobalTime > lastTime, "GlobalTime must advance monotonically.");
                    lastTime = resolver.GlobalTime;

                    // INVARIANT 2 & 3 & 4: Invariants per actor
                    foreach (var (entity, phys, root) in actors)
                    {
                        // Invariant: If consciousness <= 0, no active actions allowed
                        if (phys.ConsciousnessLevel <= 0f)
                        {
                            Assert.Null(resolver.GetCurrentAction(entity.Id));
                            Assert.Empty(resolver.GetQueuedActions(entity.Id));
                        }

                        // Invariant: Tourniquet on extremity completely halts active bleed
                        if (root.HasTourniquet && (root.Type == BodyPartType.LeftArm || root.Type == BodyPartType.RightArm ||
                                                   root.Type == BodyPartType.LeftLeg || root.Type == BodyPartType.RightLeg))
                        {
                            Assert.Equal(0f, root.GetActiveBleedRate());
                        }

                        // Invariant: Ischemia necrosis threshold at 7200s
                        if (root.HasTourniquet && root.IschemiaDuration > 7200f)
                        {
                            Assert.True(root.IsNecrotic, "Ischemia > 7200s must be necrotic.");
                        }

                        // Invariant: Cardiovascular state validity
                        Assert.False(float.IsNaN(phys.TotalBloodVolume));
                        Assert.False(float.IsNaN(phys.HeartRateBpm));
                        Assert.False(float.IsNaN(phys.MeanArterialPressureMmhg));
                        Assert.False(float.IsNaN(phys.ConsciousnessLevel));
                    }
                }
            }
        }

        #endregion
    }
}
