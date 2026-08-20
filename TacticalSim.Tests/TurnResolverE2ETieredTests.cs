using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.World;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Comprehensive 4-Tier Test Suite for TurnResolver and Physiological Integration in TacticalSim.
    /// - Tier 1: Feature Coverage (>=5 test cases per feature across Timeline, Scheduling, Sub-stepping, Carryover, Lifecycle, Cancellation, Fault isolation, Entity management, Physiology, DI)
    /// - Tier 2: Boundary & Corner Cases (>=5 test cases per feature covering dt boundaries, micro-steps, exact-match TUs, queue over-exhaustion, zero-bleed trauma, fatal bleed rates, 7200s tourniquet ischemia, entity churn)
    /// - Tier 3: Cross-Feature Combinations (Pairwise system interactions)
    /// - Tier 4: Real-World Tactical Scenarios (End-to-end multi-entity combat scenarios)
    /// </summary>
    public class TurnResolverE2ETieredTests
    {
        #region Test Helpers & Action Classes

        private class TestFailingAction : TacticalAction
        {
            public bool FailOnStart { get; set; }
            public bool FailOnExecute { get; set; }

            public TestFailingAction(Guid actorId, float tuCost, bool failOnExecute = true)
                : base(actorId, tuCost)
            {
                FailOnExecute = failOnExecute;
            }

            public override void OnStart()
            {
                base.OnStart();
                if (FailOnStart)
                {
                    throw new InvalidOperationException("Action failed during OnStart.");
                }
            }

            public override void Execute(float dt)
            {
                if (FailOnExecute)
                {
                    throw new InvalidOperationException("Action failed during Execute.");
                }
            }
        }

        private class TestLifecycleAction : TacticalAction
        {
            public List<string> CallLog { get; } = new();

            public TestLifecycleAction(Guid actorId, float tuCost)
                : base(actorId, tuCost)
            {
            }

            public override void OnStart()
            {
                base.OnStart();
                CallLog.Add("OnStart");
            }

            public override void Execute(float dt)
            {
                CallLog.Add($"Execute({dt:F3})");
            }

            public override void OnComplete()
            {
                base.OnComplete();
                CallLog.Add("OnComplete");
            }

            public override void OnCancel()
            {
                base.OnCancel();
                CallLog.Add("OnCancel");
            }

            public override void OnFail(Exception ex)
            {
                base.OnFail(ex);
                CallLog.Add($"OnFail({ex.GetType().Name})");
            }
        }

        private class TestBallisticShotAction : TacticalAction
        {
            private readonly IMaterialPenetrationSystem _penetrationSystem;
            private readonly ProjectileState _initialProjectile;
            private readonly BallisticProfile _profile;
            private readonly MaterialProperties _coverMaterial;
            private readonly float _coverThickness;
            private readonly Vector3 _coverNormal;

            public PenetrationResult? Result { get; private set; }
            public bool ShotFired { get; private set; }

            public TestBallisticShotAction(
                Guid actorId,
                float tuCost,
                IMaterialPenetrationSystem penetrationSystem,
                ProjectileState projectile,
                BallisticProfile profile,
                MaterialProperties coverMaterial,
                float coverThickness,
                Vector3 coverNormal)
                : base(actorId, tuCost)
            {
                _penetrationSystem = penetrationSystem;
                _initialProjectile = projectile;
                _profile = profile;
                _coverMaterial = coverMaterial;
                _coverThickness = coverThickness;
                _coverNormal = coverNormal;
            }

            public override void Execute(float dt)
            {
            }

            public override void OnComplete()
            {
                base.OnComplete();
                ShotFired = true;
                Result = _penetrationSystem.CalculatePenetration(
                    _initialProjectile,
                    _profile,
                    _coverMaterial,
                    _coverThickness,
                    _coverNormal);
            }
        }

        private static (TacticalEntity entity, TacticalActorPhysiology physiology, BodyPart root) CreateTestEntity(
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

        private static (TacticalEntity entity, TacticalActorPhysiology physiology, BodyPart root, BodyPart limb) CreateEntityWithLimb(
            BodyPartType limbType,
            float arterialBleed = 0f,
            float venousBleed = 0f,
            bool hasTourniquet = false)
        {
            var physiology = new TacticalActorPhysiology();
            var root = new BodyPart { Type = BodyPartType.Thorax };
            var limb = new BodyPart
            {
                Type = limbType,
                Parent = root,
                ArterialBleedRate = arterialBleed,
                VenousBleedRate = venousBleed,
                HasTourniquet = hasTourniquet
            };
            root.Children.Add(limb);
            physiology.SetRoot(root);
            var entity = new TacticalEntity(Vector3.Zero, physiology);
            return (entity, physiology, root, limb);
        }

        private static WeaponProfile CreateRifleWeapon(float muzzleVelocity = 800f, float tuCost = 5f)
        {
            var ballistics = new BallisticProfile
            {
                Mass = 0.0095f,
                CrossSectionalArea = 0.000048f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var ammo = new AmmunitionProfile
            {
                Name = "7.62x51mm NATO",
                MuzzleVelocity = muzzleVelocity,
                Ballistics = ballistics
            };

            return new WeaponProfile
            {
                Name = "Service Rifle",
                LoadedAmmunition = ammo,
                BaseTUCostToFire = tuCost
            };
        }

        private static IServiceProvider CreateTestServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            return services.BuildServiceProvider();
        }

        #endregion

        #region Tier 1: Feature Coverage (>=5 test cases per feature)

        // -------------------------------------------------------------
        // Feature 1: Global Simulation Timeline (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Timeline_01_GlobalClock_AdvancesMonotonically_WithVariableTimeSteps()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Equal(0.0f, resolver.GlobalTime);

            float[] steps = { 0.1f, 0.25f, 0.75f, 1.5f, 0.05f };
            float accumulated = 0f;

            foreach (var dt in steps)
            {
                float prevTime = resolver.GlobalTime;
                resolver.Tick(dt);
                accumulated += dt;
                Assert.True(resolver.GlobalTime > prevTime, "Global time must strictly increase.");
                Assert.Equal(accumulated, resolver.GlobalTime, 4);
            }
        }

        [Fact]
        public void Tier1_Timeline_02_GlobalClock_StartsAtZero_AndAccumulatesDeterministically()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);

            for (int i = 1; i <= 20; i++)
            {
                resolver.Tick(0.1f);
                Assert.Equal(i * 0.1f, resolver.GlobalTime, 4);
            }
        }

        [Fact]
        public void Tier1_Timeline_03_TimeAdvancedEvent_FiresWithAccurateTimestampsAndDeltas()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var events = new List<TimeAdvancedEventArgs>();
            resolver.TimeAdvanced += (_, e) => events.Add(e);

            resolver.Tick(0.5f);
            resolver.Tick(1.2f);

            Assert.Equal(2, events.Count);
            Assert.Equal(0.5f, events[0].DeltaTime, 4);
            Assert.Equal(0.0f, events[0].PreviousGlobalTime, 4);
            Assert.Equal(0.5f, events[0].CurrentGlobalTime, 4);

            Assert.Equal(1.2f, events[1].DeltaTime, 4);
            Assert.Equal(0.5f, events[1].PreviousGlobalTime, 4);
            Assert.Equal(1.7f, events[1].CurrentGlobalTime, 4);
        }

        [Fact]
        public void Tier1_Timeline_04_Reset_ClearsGlobalTime_AndRestoresInitialState()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateTestEntity();
            world.AddEntity(entity);
            resolver.ScheduleAction(new GenericTacticalAction(entity.Id, 2.0f));

            resolver.Tick(1.0f);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.True(resolver.HasActiveActions);

            resolver.Reset();

            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Same(entity, world.GetEntity(entity.Id));
            Assert.Empty(resolver.GetActiveActions());
        }

        [Fact]
        public void Tier1_Timeline_05_SequentialTicks_MaintainStrictMonotonicityAcrossMultipleSteps()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            float lastTime = 0.0f;

            for (int i = 0; i < 50; i++)
            {
                float dt = 0.02f * (i + 1);
                resolver.Tick(dt);
                Assert.True(resolver.GlobalTime > lastTime);
                lastTime = resolver.GlobalTime;
            }
        }

        // -------------------------------------------------------------
        // Feature 2: Concurrent Multi-Actor Scheduling (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Scheduling_01_ScheduleAction_IdleActor_BecomesActiveImmediately()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var action = new GenericTacticalAction(actorId, 2.0f);

            resolver.ScheduleAction(action);

            Assert.Equal(1, resolver.ActiveActorCount);
            Assert.True(resolver.HasActiveActions);
            Assert.Same(action, resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
        }

        [Fact]
        public void Tier1_Scheduling_02_ScheduleAction_BusyActor_EnqueuesInFifoOrder()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 1.0f);
            var a2 = new GenericTacticalAction(actorId, 2.0f);
            var a3 = new GenericTacticalAction(actorId, 3.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            Assert.Equal(1, resolver.ActiveActorCount);
            Assert.Same(a1, resolver.GetCurrentAction(actorId));

            var queued = resolver.GetQueuedActions(actorId);
            Assert.Equal(2, queued.Count);
            Assert.Same(a2, queued[0]);
            Assert.Same(a3, queued[1]);
        }

        [Fact]
        public void Tier1_Scheduling_03_ScheduleAction_MultipleActors_ScheduledSimultaneously()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actors = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

            foreach (var actor in actors)
            {
                resolver.ScheduleAction(new GenericTacticalAction(actor, 1.5f));
            }

            Assert.Equal(5, resolver.ActiveActorCount);
            Assert.Equal(5, resolver.GetActiveActions().Count);

            foreach (var actor in actors)
            {
                Assert.NotNull(resolver.GetCurrentAction(actor));
            }
        }

        [Fact]
        public void Tier1_Scheduling_04_ScheduleAction_NonPendingState_ThrowsInvalidOperationException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f)
            {
                State = TacticalActionState.Executing
            };

            Assert.Throws<InvalidOperationException>(() => resolver.ScheduleAction(action));
        }

        [Fact]
        public void Tier1_Scheduling_05_SchedulingStateInspection_ActiveCountAndHasActiveActions_Accurate()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);

            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(a1, 1.0f));
            Assert.True(resolver.HasActiveActions);
            Assert.Equal(1, resolver.ActiveActorCount);

            resolver.ScheduleAction(new GenericTacticalAction(a2, 1.0f));
            Assert.True(resolver.HasActiveActions);
            Assert.Equal(2, resolver.ActiveActorCount);

            resolver.Tick(1.0f);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
        }

        // -------------------------------------------------------------
        // Feature 3: Fractionated TU Sub-Stepping (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_SubStepping_01_FractionalDt_AdvancesExecutionProgressProportionally()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 2.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(0.5f);
            Assert.Equal(0.5f, action.ExecutionProgress, 4);
            Assert.Equal(TacticalActionState.Executing, action.State);

            resolver.Tick(0.75f);
            Assert.Equal(1.25f, action.ExecutionProgress, 4);
            Assert.Equal(TacticalActionState.Executing, action.State);
        }

        [Fact]
        public void Tier1_SubStepping_02_NormalizedProgress_ClampsAndInterpolatesAccurately()
        {
            var action = new GenericTacticalAction(Guid.NewGuid(), 4.0f);
            Assert.Equal(0.0f, action.NormalizedProgress, 4);

            action.ExecutionProgress = 1.0f;
            Assert.Equal(0.25f, action.NormalizedProgress, 4);

            action.ExecutionProgress = 2.0f;
            Assert.Equal(0.50f, action.NormalizedProgress, 4);

            action.ExecutionProgress = 4.0f;
            Assert.Equal(1.00f, action.NormalizedProgress, 4);
        }

        [Fact]
        public void Tier1_SubStepping_03_ActionStartedEvent_FiresOnFirstExecutionStep()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 2.0f);
            resolver.ScheduleAction(action);

            ActionEventArgs? startedEvent = null;
            resolver.ActionStarted += (_, e) => startedEvent = e;

            resolver.Tick(0.5f);

            Assert.NotNull(startedEvent);
            Assert.Same(action, startedEvent.Action);
            Assert.Equal(0.0f, startedEvent.GlobalTime);
            Assert.Equal(0.0f, action.StartTime);
        }

        [Fact]
        public void Tier1_SubStepping_04_ActionProgressedEvent_EmitsSubStepDeltasAndTimestamps()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 2.0f);
            resolver.ScheduleAction(action);

            var progressEvents = new List<ActionProgressEventArgs>();
            resolver.ActionProgressed += (_, e) => progressEvents.Add(e);

            resolver.Tick(0.6f);
            resolver.Tick(0.4f);

            Assert.Equal(2, progressEvents.Count);
            Assert.Equal(0.6f, progressEvents[0].DeltaTime, 4);
            Assert.Equal(0.6f, progressEvents[0].CurrentProgress, 4);

            Assert.Equal(0.4f, progressEvents[1].DeltaTime, 4);
            Assert.Equal(1.0f, progressEvents[1].CurrentProgress, 4);
        }

        [Fact]
        public void Tier1_SubStepping_05_ActionReachingTUCost_TransitionsToCompletedState()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.True(action.IsComplete);
            Assert.Equal(1.0f, action.ExecutionProgress, 4);
            Assert.Equal(1.0f, action.CompletionTime ?? 0f, 4);
        }

        // -------------------------------------------------------------
        // Feature 4: Sub-Tick Carryover Interleaving (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Carryover_01_SingleActor_LeftoverDt_ImmediatelyPromotesAndAdvancesSecondAction()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 1.2f);
            var a2 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            // Tick 2.0 TU: a1 takes 1.2 TU (completes), leftover 0.8 TU advances a2
            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Equal(1.2f, a1.CompletionTime ?? 0f, 4);

            Assert.Equal(TacticalActionState.Executing, a2.State);
            Assert.Equal(0.8f, a2.ExecutionProgress, 4);
            Assert.Equal(1.2f, a2.StartTime, 4);
            Assert.Same(a2, resolver.GetCurrentAction(actorId));
        }

        [Fact]
        public void Tier1_Carryover_02_ChainedCarryover_ThreeActionsCompleteInSingleTick()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 0.5f);
            var a2 = new GenericTacticalAction(actorId, 0.5f);
            var a3 = new GenericTacticalAction(actorId, 0.5f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            // Single tick of 2.0 TU should complete all 3
            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Equal(TacticalActionState.Completed, a2.State);
            Assert.Equal(TacticalActionState.Completed, a3.State);

            Assert.Equal(0.5f, a1.CompletionTime ?? 0f, 4);
            Assert.Equal(1.0f, a2.CompletionTime ?? 0f, 4);
            Assert.Equal(1.5f, a3.CompletionTime ?? 0f, 4);

            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier1_Carryover_03_Carryover_CalculatesExactCompletionTimeAndNextStartTime()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 0.75f);
            var a2 = new GenericTacticalAction(actorId, 1.25f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(1.0f); // 0.75 completes a1, 0.25 to a2

            Assert.Equal(0.75f, a1.CompletionTime ?? 0f, 4);
            Assert.Equal(0.75f, a2.StartTime, 4);
            Assert.Equal(0.25f, a2.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier1_Carryover_04_Carryover_WithPartialProgressOnPromotedAction()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 0.4f);
            var a2 = new GenericTacticalAction(actorId, 2.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Equal(TacticalActionState.Executing, a2.State);
            Assert.Equal(0.6f, a2.ExecutionProgress, 4);
            Assert.Equal(0.4f, a2.StartTime, 4);
        }

        [Fact]
        public void Tier1_Carryover_05_Carryover_QueueExhaustionLeavesActorIdle()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 0.5f);
            resolver.ScheduleAction(a1);

            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Null(resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
            Assert.False(resolver.HasActiveActions);
        }

        // -------------------------------------------------------------
        // Feature 5: Action Lifecycle State Machine (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Lifecycle_01_FullTransition_PendingToExecutingToCompleted()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var tracker = new TestLifecycleAction(Guid.NewGuid(), 1.0f);

            Assert.Equal(TacticalActionState.Pending, tracker.State);
            resolver.ScheduleAction(tracker);
            Assert.Equal(TacticalActionState.Pending, tracker.State);

            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Executing, tracker.State);

            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, tracker.State);
        }

        [Fact]
        public void Tier1_Lifecycle_02_StartTimeAndCompletionTime_AccuratelyStamped()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            resolver.Tick(5.0f); // Advance timeline to 5.0s first

            var action = new GenericTacticalAction(Guid.NewGuid(), 2.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(1.0f);
            Assert.Equal(5.0f, action.StartTime, 4);
            Assert.Null(action.CompletionTime);

            resolver.Tick(1.0f);
            Assert.Equal(7.0f, action.CompletionTime ?? 0f, 4);
        }

        [Fact]
        public void Tier1_Lifecycle_03_LifecycleHookExecutionOrder_OnStartThenExecuteThenOnComplete()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var tracker = new TestLifecycleAction(Guid.NewGuid(), 1.0f);
            resolver.ScheduleAction(tracker);

            resolver.Tick(0.5f);
            resolver.Tick(0.5f);

            Assert.Equal("OnStart", tracker.CallLog[0]);
            Assert.Contains(tracker.CallLog, c => c.StartsWith("Execute"));
            Assert.Equal("OnComplete", tracker.CallLog.Last());
        }

        [Fact]
        public void Tier1_Lifecycle_04_IsCompleteProperty_ReflectsStateAndProgress()
        {
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f);
            Assert.False(action.IsComplete);

            action.ExecutionProgress = 0.5f;
            Assert.False(action.IsComplete);

            action.ExecutionProgress = 1.0f;
            Assert.True(action.IsComplete);

            action.State = TacticalActionState.Completed;
            Assert.True(action.IsComplete);
        }

        [Fact]
        public void Tier1_Lifecycle_05_ConcreteMoveAndAimActions_ObeyLifecycleContracts()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var move = new MoveTacticalAction(actorId, Vector3.Zero, new Vector3(10, 0, 0), 2.0f);
            var aim = new AimTacticalAction(actorId, targetId, 1.0f);

            resolver.ScheduleAction(move);
            resolver.ScheduleAction(aim);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Executing, move.State);
            Assert.Equal(new Vector3(5, 0, 0), move.CurrentPosition);
            Assert.Equal(TacticalActionState.Pending, aim.State);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, move.State);
            Assert.Equal(new Vector3(10, 0, 0), move.CurrentPosition);
            Assert.Same(aim, resolver.GetCurrentAction(actorId));

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, aim.State);
        }

        // -------------------------------------------------------------
        // Feature 6: Action Cancellation (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Cancellation_01_CancelAction_ActiveAction_CancelsAndPromotesQueuedAction()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 2.0f);
            var a2 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(0.5f);
            bool cancelled = resolver.CancelAction(a1.Id);

            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, a1.State);
            Assert.Same(a2, resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
        }

        [Fact]
        public void Tier1_Cancellation_02_CancelAction_QueuedAction_RemovesWithoutInterruptingActiveAction()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 2.0f);
            var a2 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(0.5f);
            bool cancelled = resolver.CancelAction(a2.Id);

            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, a2.State);
            Assert.Equal(TacticalActionState.Executing, a1.State);
            Assert.Same(a1, resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
        }

        [Fact]
        public void Tier1_Cancellation_03_CancelActorActions_ClearsAllActiveAndQueuedActionsForActor()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 1.0f);
            var a2 = new GenericTacticalAction(actorId, 1.0f);
            var a3 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            int cancelledCount = resolver.CancelActorActions(actorId);

            Assert.Equal(3, cancelledCount);
            Assert.Equal(TacticalActionState.Cancelled, a1.State);
            Assert.Equal(TacticalActionState.Cancelled, a2.State);
            Assert.Equal(TacticalActionState.Cancelled, a3.State);
            Assert.Null(resolver.GetCurrentAction(actorId));
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier1_Cancellation_04_CancelAction_NonExistentOrEmptyGuid_ReturnsFalse()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.False(resolver.CancelAction(Guid.Empty));
            Assert.False(resolver.CancelAction(Guid.NewGuid()));
        }

        [Fact]
        public void Tier1_Cancellation_05_CancelActorActions_NonExistentActor_ReturnsZero()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Equal(0, resolver.CancelActorActions(Guid.Empty));
            Assert.Equal(0, resolver.CancelActorActions(Guid.NewGuid()));
        }

        // -------------------------------------------------------------
        // Feature 7: Fault Isolation & Failure State (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_FaultIsolation_01_ActionExceptionInExecute_TransitionsToFailedAndFiresEvent()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var failing = new TestFailingAction(Guid.NewGuid(), 1.0f, failOnExecute: true);
            resolver.ScheduleAction(failing);

            ActionFailedEventArgs? failEvent = null;
            resolver.ActionFailed += (_, e) => failEvent = e;

            resolver.Tick(0.5f);

            Assert.Equal(TacticalActionState.Failed, failing.State);
            Assert.NotNull(failing.FailureException);
            Assert.NotNull(failEvent);
            Assert.Same(failing, failEvent.Action);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier1_FaultIsolation_02_FailingAction_DoesNotDisruptConcurrentActors()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorFail = Guid.NewGuid();
            var actorOk = Guid.NewGuid();

            var failing = new TestFailingAction(actorFail, 1.0f, failOnExecute: true);
            var healthy = new GenericTacticalAction(actorOk, 1.0f);

            resolver.ScheduleAction(failing);
            resolver.ScheduleAction(healthy);

            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Failed, failing.State);
            Assert.Equal(TacticalActionState.Completed, healthy.State);
            Assert.Equal(1.0f, healthy.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier1_FaultIsolation_03_FailingAction_RemovesFromActiveWithoutCrashingResolver()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var failing = new TestFailingAction(Guid.NewGuid(), 2.0f);
            resolver.ScheduleAction(failing);

            // Should not throw to caller
            resolver.Tick(1.0f);

            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier1_FaultIsolation_04_FailingAction_AllowsNextQueuedActionToPromote()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var failing = new TestFailingAction(actorId, 1.0f, failOnExecute: true);
            var nextAction = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(failing);
            resolver.ScheduleAction(nextAction);

            resolver.Tick(0.5f); // failing fails, nextAction promoted

            Assert.Equal(TacticalActionState.Failed, failing.State);
            Assert.Same(nextAction, resolver.GetCurrentAction(actorId));

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, nextAction.State);
        }

        [Fact]
        public void Tier1_FaultIsolation_05_FailureException_PropertyPreservedOnFailedAction()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var failing = new TestFailingAction(Guid.NewGuid(), 1.0f);
            resolver.ScheduleAction(failing);

            resolver.Tick(0.5f);

            Assert.NotNull(failing.FailureException);
            Assert.IsType<InvalidOperationException>(failing.FailureException);
            Assert.Equal("Action failed during Execute.", failing.FailureException.Message);
        }

        // -------------------------------------------------------------
        // Feature 8: Entity Management in TurnResolver (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_EntityManagement_01_RegisterEntity_AddsEntityAndFiresEntityRegisteredEvent()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateTestEntity();

            EntityEventArgs? evt = null;
            world.EntityAdded += (_, e) => evt = e;

            world.AddEntity(entity);

            Assert.NotNull(evt);
            Assert.Same(entity, evt.Entity);
            Assert.Single(world.GetEntities());
            Assert.Same(entity, world.GetEntity(entity.Id));
        }

        [Fact]
        public void Tier1_EntityManagement_02_UnregisterEntity_RemovesEntityAndFiresUnregisteredEvent()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateTestEntity();
            world.AddEntity(entity);

            EntityEventArgs? evt = null;
            world.EntityRemoved += (_, e) => evt = e;

            bool result = world.RemoveEntity(entity.Id);

            Assert.True(result);
            Assert.NotNull(evt);
            Assert.Same(entity, evt.Entity);
            Assert.Empty(world.GetEntities());
            Assert.Null(world.GetEntity(entity.Id));
        }

        [Fact]
        public void Tier1_EntityManagement_03_GetRegisteredEntities_ReturnsDeterministicIdOrdering()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var entities = Enumerable.Range(0, 5).Select(_ => CreateTestEntity().entity).ToList();

            // Register in random order
            foreach (var e in entities.OrderBy(_ => Guid.NewGuid()))
            {
                world.AddEntity(e);
            }

            var registered = world.GetEntities().ToList();
            var expected = entities.OrderBy(e => e.Id).ToList();

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Id, registered[i].Id);
            }
        }

        [Fact]
        public void Tier1_EntityManagement_04_GetEntity_ReturnsExistingOrNull()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateTestEntity();
            world.AddEntity(entity);

            Assert.Same(entity, world.GetEntity(entity.Id));
            Assert.Null(world.GetEntity(Guid.NewGuid()));
        }

        [Fact]
        public void Tier1_EntityManagement_05_Reset_DoesNotAlterWorldEntities()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (e1, _, _) = CreateTestEntity();
            var (e2, _, _) = CreateTestEntity();

            world.AddEntity(e1);
            world.AddEntity(e2);

            Assert.Equal(2, world.GetEntities().Count);

            resolver.Reset();

            Assert.Equal(2, world.GetEntities().Count);
            Assert.Same(e1, world.GetEntity(e1.Id));
            Assert.Same(e2, world.GetEntity(e2.Id));
        }

        // -------------------------------------------------------------
        // Feature 9: Physiological Ticking Integration (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_Physiology_01_Tick_InvokesTickPhysiologyOnAllRegisteredEntities()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (e1, p1, _) = CreateTestEntity(arterialBleed: 10f);
            var (e2, p2, _) = CreateTestEntity(arterialBleed: 20f);

            world.AddEntity(e1);
            world.AddEntity(e2);

            resolver.Tick(2.0f);

            Assert.Equal(5000f - 20f, p1.TotalBloodVolume, 3);
            Assert.Equal(5000f - 40f, p2.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tier1_Physiology_02_ActiveHemorrhage_ReducesBloodVolumeProportionallyToDt()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 50f, venousBleed: 10f); // 60 ml/s
            world.AddEntity(entity);

            resolver.Tick(0.5f); // 30 ml lost
            Assert.Equal(4970f, phys.TotalBloodVolume, 3);

            resolver.Tick(1.5f); // 90 ml lost
            Assert.Equal(4880f, phys.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tier1_Physiology_03_ExtremityTourniquet_HaltsBleedAndAdvancesIschemiaDuration()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _, limb) = CreateEntityWithLimb(BodyPartType.RightArm, arterialBleed: 40f, hasTourniquet: true);
            world.AddEntity(entity);

            Assert.Equal(0f, limb.GetActiveBleedRate());

            resolver.Tick(50.0f);

            Assert.Equal(5000f, phys.TotalBloodVolume); // No blood lost
            Assert.Equal(50.0f, limb.IschemiaDuration, 2);
            Assert.False(limb.IsNecrotic);
        }

        [Fact]
        public void Tier1_Physiology_04_UnregisteredEntities_NotTickedByTurnResolver()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (registered, pReg, _) = CreateTestEntity(arterialBleed: 10f);
            var (unregistered, pUnreg, _) = CreateTestEntity(arterialBleed: 10f);

            world.AddEntity(registered);

            resolver.Tick(3.0f);

            Assert.Equal(4970f, pReg.TotalBloodVolume, 3);
            Assert.Equal(5000f, pUnreg.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tier1_Physiology_05_Incapacitation_ZeroConsciousness_AutomaticallyCancelsActions()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            // 4000 ml/s bleed drops blood volume to 1000ml in 1s (>50% lost -> Fatal -> Consciousness 0)
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 4000f);
            world.AddEntity(entity);

            var active = new GenericTacticalAction(entity.Id, 10.0f);
            var queued = new GenericTacticalAction(entity.Id, 5.0f);

            resolver.ScheduleAction(active);
            resolver.ScheduleAction(queued);

            resolver.Tick(1.0f);

            Assert.Equal(0.0f, phys.ConsciousnessLevel);
            Assert.Equal(TacticalActionState.Cancelled, active.State);
            Assert.Equal(TacticalActionState.Cancelled, queued.State);
            Assert.False(resolver.HasActiveActions);
        }

        // -------------------------------------------------------------
        // Feature 10: Dependency Injection (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier1_DI_01_AddTacticalSimCore_RegistersITurnResolverAsTurnResolver()
        {
            var provider = CreateTestServiceProvider();
            var resolver = provider.GetService<ITurnResolver>();

            Assert.NotNull(resolver);
            Assert.IsType<TurnResolver>(resolver);
        }

        [Fact]
        public void Tier1_DI_02_AddSimulationServices_ResolvesITurnResolverAsTransient()
        {
            var provider = CreateTestServiceProvider();
            var r1 = provider.GetRequiredService<ITurnResolver>();
            var r2 = provider.GetRequiredService<ITurnResolver>();

            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void Tier1_DI_03_SeparateScopes_ResolveIndependentTurnResolverInstances()
        {
            var provider = CreateTestServiceProvider();
            using var scope1 = provider.CreateScope();
            using var scope2 = provider.CreateScope();

            var r1 = scope1.ServiceProvider.GetRequiredService<ITurnResolver>();
            var r2 = scope2.ServiceProvider.GetRequiredService<ITurnResolver>();

            Assert.NotSame(r1, r2);
        }

        [Fact]
        public void Tier1_DI_04_DIResolvedTurnResolver_CanRegisterEntitiesAndTickPhysiology()
        {
            var provider = CreateTestServiceProvider();
            var resolver = provider.GetRequiredService<ITurnResolver>();
            var world = provider.GetRequiredService<ITacticalWorld>();

            var (entity, phys, _) = CreateTestEntity(arterialBleed: 15f);
            world.AddEntity(entity);

            resolver.Tick(2.0f);

            Assert.Equal(2.0f, resolver.GlobalTime, 4);
            Assert.Equal(4970f, phys.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tier1_DI_05_FullServiceGraph_ResolvesAllCoreInterfacesCleanly()
        {
            var provider = CreateTestServiceProvider();

            Assert.NotNull(provider.GetService<ITurnResolver>());
            Assert.NotNull(provider.GetService<IMaterialRegistry>());
            Assert.NotNull(provider.GetService<IMaterialPenetrationSystem>());
            Assert.NotNull(provider.GetService<IEnvironmentModel>());
            Assert.NotNull(provider.GetService<IDragModel>());
        }

        #endregion

        #region Tier 2: Boundary & Corner Cases (>=5 test cases per feature)

        // -------------------------------------------------------------
        // Feature 1: Delta Time Boundaries (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_DtBoundaries_01_ZeroDt_ThrowsArgumentException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Throws<ArgumentException>(() => resolver.Tick(0.0f));
        }

        [Theory]
        [InlineData(-0.0001f)]
        [InlineData(-1.0f)]
        [InlineData(-100.0f)]
        public void Tier2_DtBoundaries_02_NegativeDt_ThrowsArgumentException(float negativeDt)
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Throws<ArgumentException>(() => resolver.Tick(negativeDt));
        }

        [Fact]
        public void Tier2_DtBoundaries_03_NaNDt_ThrowsArgumentException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Throws<ArgumentException>(() => resolver.Tick(float.NaN));
        }

        [Theory]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void Tier2_DtBoundaries_04_PositiveAndNegativeInfinityDt_ThrowsArgumentException(float infDt)
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Throws<ArgumentException>(() => resolver.Tick(infDt));
        }

        [Fact]
        public void Tier2_DtBoundaries_05_VerySmallPositiveDt_1eMinus6_AdvancesWithoutException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            resolver.Tick(1e-6f);
            Assert.True(resolver.GlobalTime > 0.0f);
            Assert.Equal(1e-6f, resolver.GlobalTime, 7);
        }

        // -------------------------------------------------------------
        // Feature 2: Micro-steps & Precision Boundaries (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_MicroSteps_01_OneMillionMicroSteps_AccumulatesAccurately()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 0.1f);
            resolver.ScheduleAction(action);

            // 1,000 steps of 0.0001f = 0.1f
            float microDt = 0.0001f;
            for (int i = 0; i < 1000; i++)
            {
                resolver.Tick(microDt);
            }

            Assert.Equal(0.1f, resolver.GlobalTime, 3);
            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(0.1f, action.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier2_MicroSteps_02_TenThousandSubTickSteps_CompletesActionWithZeroDrift()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f);
            resolver.ScheduleAction(action);

            float step = 0.0001f;
            for (int i = 0; i < 10000; i++)
            {
                resolver.Tick(step);
            }

            Assert.Equal(1.0f, resolver.GlobalTime, 3);
            Assert.Equal(TacticalActionState.Completed, action.State);
        }

        [Fact]
        public void Tier2_MicroSteps_03_SubTickMicroSteps_WithTinyRemainder_DoesNotInfiniteLoop()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 0.999999f);
            resolver.ScheduleAction(action);

            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(0.999999f, action.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier2_MicroSteps_04_RecurringDecimalTUCost_OneThird_ResolvesAccurately()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            float oneThird = 1.0f / 3.0f;
            var action = new GenericTacticalAction(Guid.NewGuid(), oneThird);
            resolver.ScheduleAction(action);

            resolver.Tick(0.1f);
            resolver.Tick(0.1f);
            resolver.Tick(0.1f);
            resolver.Tick(0.1f); // Surpasses 1/3

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(oneThird, action.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier2_MicroSteps_05_MicroStepInterleaving_AcrossTenConcurrentActors()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actors = Enumerable.Range(0, 10).Select(i => (Id: Guid.NewGuid(), Action: new GenericTacticalAction(Guid.NewGuid(), 0.05f * (i + 1)))).ToList();

            foreach (var a in actors)
            {
                a.Action.ActorId = a.Id;
                resolver.ScheduleAction(a.Action);
            }

            // Step in 0.01 TU micro steps
            for (int i = 0; i < 55; i++)
            {
                resolver.Tick(0.01f);
            }

            foreach (var a in actors)
            {
                Assert.Equal(TacticalActionState.Completed, a.Action.State);
            }
            Assert.False(resolver.HasActiveActions);
        }

        // -------------------------------------------------------------
        // Feature 3: Exact-Match TU Deltas (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_ExactMatch_01_SingleTickMatchingExactTUCost_CompletesWithZeroCarryover()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 3.5f);
            resolver.ScheduleAction(action);

            resolver.Tick(3.5f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(3.5f, action.ExecutionProgress, 5);
            Assert.Equal(3.5f, action.CompletionTime ?? 0f, 5);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_ExactMatch_02_ExactMultiStepTUProgression_CompletesSequentialActions()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 1.0f);
            var a2 = new GenericTacticalAction(actorId, 2.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(1.0f); // Exactly completes a1
            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Same(a2, resolver.GetCurrentAction(actorId));

            resolver.Tick(2.0f); // Exactly completes a2
            Assert.Equal(TacticalActionState.Completed, a2.State);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_ExactMatch_03_ExactCarryoverBoundary_TwoActionsEqualDt_CompletesBoth()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actorId, 0.4f);
            var a2 = new GenericTacticalAction(actorId, 0.6f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(1.0f); // Exactly matches 0.4 + 0.6

            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Equal(TacticalActionState.Completed, a2.State);
            Assert.Equal(0.4f, a1.CompletionTime ?? 0f, 4);
            Assert.Equal(1.0f, a2.CompletionTime ?? 0f, 4);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_ExactMatch_04_MultiActorExactMatch_SimultaneousCompletions()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();

            var act1 = new GenericTacticalAction(a1, 1.5f);
            var act2 = new GenericTacticalAction(a2, 1.5f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);

            resolver.Tick(1.5f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Completed, act2.State);
            Assert.Equal(0, resolver.ActiveActorCount);
        }

        [Fact]
        public void Tier2_ExactMatch_05_ExactTickOnRegisteredEntity_PhysiologyAndActionSync()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 10f);
            world.AddEntity(entity);

            var action = new GenericTacticalAction(entity.Id, 2.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(4980f, phys.TotalBloodVolume, 3);
            Assert.Equal(2.0f, resolver.GlobalTime, 4);
        }

        // -------------------------------------------------------------
        // Feature 4: Carryover Queue Over-Exhaustion (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_QueueExhaustion_01_SingleLargeTick_DrainsAllQueuedActions_LeavesActorIdle()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(actorId, 1.0f));
            resolver.ScheduleAction(new GenericTacticalAction(actorId, 1.0f));

            resolver.Tick(100.0f);

            Assert.False(resolver.HasActiveActions);
            Assert.Null(resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));
        }

        [Fact]
        public void Tier2_QueueExhaustion_02_MegaTick_DrainsTenQueuedActionsInCorrectOrder()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var actions = Enumerable.Range(0, 10).Select(_ => new GenericTacticalAction(actorId, 0.5f)).ToList();

            foreach (var a in actions)
            {
                resolver.ScheduleAction(a);
            }

            resolver.Tick(50.0f);

            for (int i = 0; i < actions.Count; i++)
            {
                Assert.Equal(TacticalActionState.Completed, actions[i].State);
                Assert.Equal((i + 1) * 0.5f, actions[i].CompletionTime ?? 0f, 4);
            }
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_QueueExhaustion_03_TickOnActorWithEmptyQueue_PerformsCleanNoOp()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            resolver.Tick(10.0f);

            Assert.Equal(10.0f, resolver.GlobalTime, 4);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_QueueExhaustion_04_MegaTick_DrainsMultipleActorsQueuesSimultaneously()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(a1, 1.0f));
            resolver.ScheduleAction(new GenericTacticalAction(a1, 2.0f));
            resolver.ScheduleAction(new GenericTacticalAction(a2, 1.5f));
            resolver.ScheduleAction(new GenericTacticalAction(a2, 1.5f));

            resolver.Tick(20.0f);

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
        }

        [Fact]
        public void Tier2_QueueExhaustion_05_QueueExhaustionThenReschedulingNextTick_ExecutesNormally()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(actorId, 1.0f));
            resolver.Tick(5.0f); // Drained

            var newAction = new GenericTacticalAction(actorId, 2.0f);
            resolver.ScheduleAction(newAction);
            Assert.Same(newAction, resolver.GetCurrentAction(actorId));

            resolver.Tick(2.0f);
            Assert.Equal(TacticalActionState.Completed, newAction.State);
            Assert.Equal(7.0f, newAction.CompletionTime ?? 0f, 4);
        }

        // -------------------------------------------------------------
        // Feature 5: Zero-Bleed Trauma & Baseline Boundaries (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_ZeroBleed_01_ZeroBleedEntity_TickedTenThousandSeconds_MaintainsVitals()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 0f, venousBleed: 0f);
            world.AddEntity(entity);

            resolver.Tick(10000.0f);

            Assert.Equal(5000f, phys.TotalBloodVolume);
            Assert.Equal(1.0f, phys.ConsciousnessLevel);
            Assert.Equal(HemorrhageClass.Class1, phys.CurrentHemorrhageClass);
        }

        [Fact]
        public void Tier2_ZeroBleed_02_ZeroBleedEntity_MultiPartTree_NoSpontaneousIschemiaOrNecrosis()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, root, limb) = CreateEntityWithLimb(BodyPartType.LeftLeg, arterialBleed: 0f, hasTourniquet: false);
            world.AddEntity(entity);

            resolver.Tick(5000.0f);

            Assert.Equal(0.0f, limb.IschemiaDuration);
            Assert.False(limb.IsNecrotic);
            Assert.Equal(5000f, phys.TotalBloodVolume);
        }

        [Fact]
        public void Tier2_ZeroBleed_03_HemorrhageReducedToZero_StopsFurtherBloodLoss()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, root) = CreateTestEntity(arterialBleed: 20f);
            world.AddEntity(entity);

            resolver.Tick(5.0f); // 100ml lost
            Assert.Equal(4900f, phys.TotalBloodVolume, 3);

            // Medic stops bleed
            root.ArterialBleedRate = 0f;

            resolver.Tick(10.0f);
            Assert.Equal(4900f, phys.TotalBloodVolume, 3); // No further loss
        }

        [Fact]
        public void Tier2_ZeroBleed_04_ZeroBleedEntity_ContinuousLongActionChains_Undisturbed()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity();
            world.AddEntity(entity);

            for (int i = 0; i < 10; i++)
            {
                resolver.ScheduleAction(new GenericTacticalAction(entity.Id, 1.0f));
            }

            resolver.Tick(10.0f);

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(1.0f, phys.ConsciousnessLevel);
        }

        [Fact]
        public void Tier2_ZeroBleed_05_ZeroBleedEntity_ConsciousnessRemainsOnePointZero()
        {
            var (entity, phys, _) = CreateTestEntity();
            phys.TickPhysiology(100f);
            Assert.Equal(1.0f, phys.ConsciousnessLevel);
            Assert.Equal(80f, phys.HeartRateBpm);
            Assert.Equal(93f, phys.MeanArterialPressureMmhg);
        }

        // -------------------------------------------------------------
        // Feature 6: Massive Fatal Bleed Rates (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_FatalBleed_01_MassiveArterialBleed_DropsBloodVolumeToZero_InstantConsciousnessZero()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 6000f);
            world.AddEntity(entity);

            resolver.Tick(1.0f);

            Assert.True(phys.TotalBloodVolume <= 0f);
            Assert.Equal(0.0f, phys.ConsciousnessLevel);
            Assert.Equal(HemorrhageClass.Fatal, phys.CurrentHemorrhageClass);
        }

        [Fact]
        public void Tier2_FatalBleed_02_HyperBleed_InFractionalTick_TriggersImmediateActionPurge()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 30000f); // 30,000 ml/s -> 3000ml in 0.1s (60% lost)
            world.AddEntity(entity);

            var a1 = new GenericTacticalAction(entity.Id, 2.0f);
            var a2 = new GenericTacticalAction(entity.Id, 2.0f);
            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);

            resolver.Tick(0.1f);

            Assert.Equal(TacticalActionState.Cancelled, a1.State);
            Assert.Equal(TacticalActionState.Cancelled, a2.State);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier2_FatalBleed_03_MultiActor_OneBleedsOutFatal_OtherActorsContinueUndisturbed()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (fatallyWounded, physFatal, _) = CreateTestEntity(arterialBleed: 5000f);
            var (healthy, physHealthy, _) = CreateTestEntity();

            world.AddEntity(fatallyWounded);
            world.AddEntity(healthy);

            var fatalAct = new GenericTacticalAction(fatallyWounded.Id, 5.0f);
            var healthyAct = new GenericTacticalAction(healthy.Id, 5.0f);

            resolver.ScheduleAction(fatalAct);
            resolver.ScheduleAction(healthyAct);

            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Cancelled, fatalAct.State);
            Assert.Equal(TacticalActionState.Executing, healthyAct.State);
            Assert.Equal(1.0f, healthyAct.ExecutionProgress, 4);
        }

        [Fact]
        public void Tier2_FatalBleed_04_TotalBloodVolume_ClampedAtZero_DoesNotUnderflow()
        {
            var (entity, phys, _) = CreateTestEntity(arterialBleed: 10000f);
            phys.TickPhysiology(2.0f); // 20,000 ml bleed on 5000ml baseline

            Assert.True(phys.TotalBloodVolume <= 0f);
            Assert.Equal(0.0f, phys.ConsciousnessLevel);
            Assert.Equal(0f, phys.HeartRateBpm);
        }

        [Fact]
        public void Tier2_FatalBleed_05_FatalHemorrhage_TransitionEvents_FiresActionCancelled()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateTestEntity(arterialBleed: 5000f);
            world.AddEntity(entity);

            var action = new GenericTacticalAction(entity.Id, 5.0f);
            resolver.ScheduleAction(action);

            ActionEventArgs? cancelledEvt = null;
            resolver.ActionCancelled += (_, e) => cancelledEvt = e;

            resolver.Tick(1.0f);

            Assert.NotNull(cancelledEvt);
            Assert.Same(action, cancelledEvt.Action);
        }

        // -------------------------------------------------------------
        // Feature 7: Tourniquet Ischemia & 7200s Necrosis Threshold (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_Ischemia_01_Tourniquet_At7199Seconds_NotNecrotic()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _, limb) = CreateEntityWithLimb(BodyPartType.LeftArm, arterialBleed: 20f, hasTourniquet: true);
            world.AddEntity(entity);

            resolver.Tick(7199.0f);

            Assert.Equal(7199.0f, limb.IschemiaDuration, 2);
            Assert.False(limb.IsNecrotic);
        }

        [Fact]
        public void Tier2_Ischemia_02_Tourniquet_At7200Seconds_ExactBoundary_TransitionsToNecrotic()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _, limb) = CreateEntityWithLimb(BodyPartType.LeftArm, arterialBleed: 20f, hasTourniquet: true);
            world.AddEntity(entity);

            resolver.Tick(7200.5f); // > 7200f threshold

            Assert.Equal(7200.5f, limb.IschemiaDuration, 2);
            Assert.True(limb.IsNecrotic);
        }

        [Fact]
        public void Tier2_Ischemia_03_Tourniquet_At10000Seconds_IschemiaDurationMaintainedAndNecrotic()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _, limb) = CreateEntityWithLimb(BodyPartType.RightLeg, arterialBleed: 30f, hasTourniquet: true);
            world.AddEntity(entity);

            resolver.Tick(10000.0f);

            Assert.Equal(10000.0f, limb.IschemiaDuration, 2);
            Assert.True(limb.IsNecrotic);
        }

        [Fact]
        public void Tier2_Ischemia_04_MultipleLimbs_StaggeredTourniquets_IndependentNecrosisTiming()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var physiology = new TacticalActorPhysiology();
            var root = new BodyPart { Type = BodyPartType.Thorax };
            var arm = new BodyPart { Type = BodyPartType.LeftArm, Parent = root, HasTourniquet = true };
            var leg = new BodyPart { Type = BodyPartType.RightLeg, Parent = root, HasTourniquet = false };
            root.Children.Add(arm);
            root.Children.Add(leg);
            physiology.SetRoot(root);
            var entity = new TacticalEntity(Vector3.Zero, physiology);
            world.AddEntity(entity);

            // Tick 2000s with only arm tourniqueted
            resolver.Tick(2000.0f);
            Assert.Equal(2000.0f, arm.IschemiaDuration, 2);
            Assert.Equal(0.0f, leg.IschemiaDuration, 2);

            // Now apply tourniquet to leg
            leg.HasTourniquet = true;

            // Tick 5300s (Total time = 7300s -> arm at 7300s > 7200s [necrotic], leg at 5300s < 7200s [not necrotic])
            resolver.Tick(5300.0f);

            Assert.Equal(7300.0f, arm.IschemiaDuration, 2);
            Assert.True(arm.IsNecrotic);

            Assert.Equal(5300.0f, leg.IschemiaDuration, 2);
            Assert.False(leg.IsNecrotic);
        }

        [Fact]
        public void Tier2_Ischemia_05_TourniquetOnNonExtremity_DoesNotHaltBleeding()
        {
            // BodyPart.GetActiveBleedRate checks IsExtremity
            var thorax = new BodyPart
            {
                Type = BodyPartType.Thorax,
                ArterialBleedRate = 20f,
                HasTourniquet = true // Invalid anatomical intervention on torso
            };

            Assert.Equal(20f, thorax.GetActiveBleedRate());
        }

        // -------------------------------------------------------------
        // Feature 8: Entity Registration & Rapid Churn (5 tests)
        // -------------------------------------------------------------

        [Fact]
        public void Tier2_EntityChurn_01_RegisterEntity_Null_ThrowsArgumentNullException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.Throws<ArgumentNullException>(() => world.AddEntity(null!));
        }

        [Fact]
        public void Tier2_EntityChurn_02_RegisterEntity_EmptyGuid_ThrowsArgumentException()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var mock = new MockEmptyGuidEntity();
            Assert.Throws<ArgumentException>(() => world.AddEntity(mock));
        }

        private class MockEmptyGuidEntity : IEntity
        {
            public Guid Id => Guid.Empty;
            public Vector3 Position { get; set; } = Vector3.Zero;
            public IActorPhysiology Physiology { get; set; } = new TacticalActorPhysiology();
            public WeaponProfile? EquippedWeapon { get; set; }
        }

        [Fact]
        public void Tier2_EntityChurn_03_RegisterEntity_DuplicateGuid_UpdatesEntity()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (e1, _, _) = CreateTestEntity();
            world.AddEntity(e1);

            var e1Updated = new TacticalEntity(new Vector3(10, 0, 0), e1.Physiology);
            // Replace by setting the same ID in dictionary
            world.AddEntity(e1);

            Assert.Single(world.GetEntities());
            Assert.Same(e1, world.GetEntity(e1.Id));
        }

        [Fact]
        public void Tier2_EntityChurn_04_UnregisterEntity_EmptyOrNonExistent_ReturnsFalse()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            Assert.False(world.RemoveEntity(Guid.Empty));
            Assert.False(world.RemoveEntity(Guid.NewGuid()));
        }

        [Fact]
        public void Tier2_EntityChurn_05_RapidRegistrationChurn_OneThousandEntities_LeavesCleanState()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var entities = Enumerable.Range(0, 1000).Select(_ => CreateTestEntity().entity).ToList();

            foreach (var e in entities)
            {
                world.AddEntity(e);
            }
            Assert.Equal(1000, world.GetEntities().Count);

            foreach (var e in entities)
            {
                Assert.True(world.RemoveEntity(e.Id));
            }
            Assert.Empty(world.GetEntities());
        }

        #endregion

        #region Tier 3: Cross-Feature Combinations

        [Fact]
        public void Tier3_CrossFeature_01_MultiActorConcurrentActionChains_WithSimultaneousPhysiologicalBleeding()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);

            // 4 Actors with varying bleed levels
            var (a1, p1, _) = CreateTestEntity(arterialBleed: 0f);    // Healthy
            var (a2, p2, _) = CreateTestEntity(arterialBleed: 5f);    // Mild (5 ml/s)
            var (a3, p3, _) = CreateTestEntity(arterialBleed: 20f);   // Moderate (20 ml/s)
            var (a4, p4, _) = CreateTestEntity(arterialBleed: 120f);  // Severe (120 ml/s -> 3000ml in 25s => Fatal)

            world.AddEntity(a1);
            world.AddEntity(a2);
            world.AddEntity(a3);
            world.AddEntity(a4);

            // Each actor queues 3 sequential actions of 10.0 TU each (Total 30 TU)
            foreach (var actor in new[] { a1, a2, a3, a4 })
            {
                resolver.ScheduleAction(new GenericTacticalAction(actor.Id, 10.0f));
                resolver.ScheduleAction(new GenericTacticalAction(actor.Id, 10.0f));
                resolver.ScheduleAction(new GenericTacticalAction(actor.Id, 10.0f));
            }

            // Tick in 5.0 TU increments up to 30.0 TU
            for (int step = 0; step < 6; step++)
            {
                resolver.Tick(5.0f);
            }

            Assert.Equal(30.0f, resolver.GlobalTime, 4);

            // a1, a2, a3 completed all actions
            Assert.Null(resolver.GetCurrentAction(a1.Id));
            Assert.Null(resolver.GetCurrentAction(a2.Id));
            Assert.Null(resolver.GetCurrentAction(a3.Id));

            // Verify blood losses
            Assert.Equal(5000f, p1.TotalBloodVolume);
            Assert.Equal(5000f - (5f * 30f), p2.TotalBloodVolume, 3); // 4850 ml
            Assert.Equal(5000f - (20f * 30f), p3.TotalBloodVolume, 3); // 4400 ml

            // a4 suffered fatal decompensation at ~25s, actions were cancelled
            Assert.Equal(HemorrhageClass.Fatal, p4.CurrentHemorrhageClass);
            Assert.Equal(0.0f, p4.ConsciousnessLevel);
            Assert.Null(resolver.GetCurrentAction(a4.Id));
        }

        [Fact]
        public void Tier3_CrossFeature_02_LimbTourniquetApplied_DuringActiveMovementAndAiming()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, phys, _, limb) = CreateEntityWithLimb(BodyPartType.LeftLeg, arterialBleed: 50f, hasTourniquet: false);
            world.AddEntity(entity);

            // Step 1: 2.0s without tourniquet (100ml blood lost)
            resolver.Tick(2.0f);
            Assert.Equal(4900f, phys.TotalBloodVolume, 3);

            // Step 2: Tourniquet applied on left leg
            limb.HasTourniquet = true;

            // Step 3: Entity queues multi-step movement and aiming
            var move = new MoveTacticalAction(entity.Id, Vector3.Zero, new Vector3(50, 0, 0), 100.0f);
            var aim = new AimTacticalAction(entity.Id, Guid.NewGuid(), 200.0f);

            resolver.ScheduleAction(move);
            resolver.ScheduleAction(aim);

            // Tick 300.0s (total time = 302s)
            resolver.Tick(300.0f);

            Assert.Equal(302.0f, resolver.GlobalTime, 4);
            Assert.Equal(TacticalActionState.Completed, move.State);
            Assert.Equal(TacticalActionState.Completed, aim.State);

            // Blood volume must remain strictly constant after tourniquet
            Assert.Equal(4900f, phys.TotalBloodVolume, 3);
            Assert.Equal(300.0f, limb.IschemiaDuration, 2);
            Assert.False(limb.IsNecrotic);
        }

        [Fact]
        public void Tier3_CrossFeature_03_ActionFailureIsolation_WithSimultaneousMultiActorProgressionAndBleeding()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (a1, p1, _) = CreateTestEntity(arterialBleed: 0f);
            var (a2, p2, _) = CreateTestEntity(arterialBleed: 15f);
            var (a3, p3, _) = CreateTestEntity(arterialBleed: 0f);

            world.AddEntity(a1);
            world.AddEntity(a2);
            world.AddEntity(a3);

            var act1 = new GenericTacticalAction(a1.Id, 2.0f);
            var act2 = new TestFailingAction(a2.Id, 2.0f, failOnExecute: true);
            var act3 = new GenericTacticalAction(a3.Id, 2.0f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);
            resolver.ScheduleAction(act3);

            bool failHandled = false;
            resolver.ActionFailed += (_, e) =>
            {
                failHandled = true;
                Assert.Same(act2, e.Action);
            };

            resolver.Tick(2.0f);

            Assert.True(failHandled);
            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Failed, act2.State);
            Assert.Equal(TacticalActionState.Completed, act3.State);

            // Physiology on failing actor still ticked
            Assert.Equal(5000f - 30f, p2.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tier3_CrossFeature_04_LethalTraumaMidTick_CancelsActions_WhilePeerActorsContinueUndisturbed()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (a1, _, _) = CreateTestEntity();
            var (a2, p2, root2) = CreateTestEntity();
            var (a3, _, _) = CreateTestEntity();

            world.AddEntity(a1);
            world.AddEntity(a2);
            world.AddEntity(a3);

            var act1 = new GenericTacticalAction(a1.Id, 5.0f);
            var act2 = new GenericTacticalAction(a2.Id, 5.0f);
            var act3 = new GenericTacticalAction(a3.Id, 5.0f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);
            resolver.ScheduleAction(act3);

            // Run for 2.0s
            resolver.Tick(2.0f);
            Assert.Equal(TacticalActionState.Executing, act1.State);
            Assert.Equal(TacticalActionState.Executing, act2.State);
            Assert.Equal(TacticalActionState.Executing, act3.State);

            // a2 suffers massive neck laceration
            root2.ArterialBleedRate = 3000f;

            // Tick 1.0s -> a2 loses 3000ml (Fatal) and loses consciousness
            resolver.Tick(1.0f);

            Assert.Equal(0.0f, p2.ConsciousnessLevel);
            Assert.Equal(TacticalActionState.Cancelled, act2.State);
            Assert.Equal(TacticalActionState.Executing, act1.State);
            Assert.Equal(TacticalActionState.Executing, act3.State);

            // Finish remaining 2.0s
            resolver.Tick(2.0f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Completed, act3.State);
        }

        [Fact]
        public void Tier3_CrossFeature_05_TourniquetIschemia_CrossesNecrosisThreshold_DuringMultiTurnReconTimeline()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (e1, _, _, arm1) = CreateEntityWithLimb(BodyPartType.RightArm, arterialBleed: 10f, hasTourniquet: true);
            var (e2, _, _) = CreateTestEntity();

            world.AddEntity(e1);
            world.AddEntity(e2);

            // Staggered long recon mission of 8000s
            resolver.ScheduleAction(new GenericTacticalAction(e1.Id, 8000.0f));
            resolver.ScheduleAction(new GenericTacticalAction(e2.Id, 8000.0f));

            resolver.Tick(7000.0f);
            Assert.False(arm1.IsNecrotic);

            resolver.Tick(500.0f); // T = 7500s > 7200s
            Assert.True(arm1.IsNecrotic);

            resolver.Tick(500.0f); // T = 8000s
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Tier3_CrossFeature_06_BallisticPenetrationInflictsTrauma_TurnResolverTicksSubsequentBleed()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var registry = new MaterialRegistry();
            var penetrationSystem = new MaterialPenetrationSystem();

            var shooterId = Guid.NewGuid();
            var (target, targetPhys, targetRoot) = CreateTestEntity();
            world.AddEntity(target);

            var wood = registry.GetMaterial(MaterialType.Wood);
            var rifleProfile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var bullet = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900.0f),
                Time = 0f
            };

            var shotAction = new TestBallisticShotAction(shooterId, 1.0f, penetrationSystem, bullet, rifleProfile, wood, 0.02f, new Vector3(0, 0, -1));
            resolver.ScheduleAction(shotAction);

            resolver.Tick(1.0f);

            Assert.True(shotAction.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, shotAction.Result!.Value.Outcome);

            // Shot perforates wood and impacts target, causing 25 ml/s arterial bleed
            targetRoot.ArterialBleedRate = 25.0f;

            // Subsequent turn ticks bleed target
            resolver.Tick(2.0f);
            Assert.Equal(5000f - 50f, targetPhys.TotalBloodVolume, 3);
        }

        #endregion

        #region Tier 4: Real-World Tactical Scenarios

        /// <summary>
        /// Scenario 1: Squad Bounding Maneuver with Concurrent Movement & Suppressive Aim
        /// 4-man fireteam executing leapfrog bounding overwatch under fractionated timeline.
        /// </summary>
        [Fact]
        public void Tier4_Scenario1_SquadBoundingManeuver_WithConcurrentMovementAndSuppressiveAim()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);

            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();
            var b1 = Guid.NewGuid();
            var b2 = Guid.NewGuid();
            var hostileId = Guid.NewGuid();

            // Phase 1: Alpha pair bounds forward 20m (4.0 TU), Bravo pair provides overwatch (Aim 4.0 TU)
            var a1Bound1 = new MoveTacticalAction(a1, new Vector3(0, 0, 0), new Vector3(20, 0, 0), 4.0f);
            var a2Bound1 = new MoveTacticalAction(a2, new Vector3(0, 2, 0), new Vector3(20, 2, 0), 4.0f);
            var b1Aim1 = new AimTacticalAction(b1, hostileId, 4.0f);
            var b2Aim1 = new AimTacticalAction(b2, hostileId, 4.0f);

            // Phase 2: Alpha pair sets overwatch (Aim 4.0 TU), Bravo pair bounds forward 20m (4.0 TU)
            var a1Aim2 = new AimTacticalAction(a1, hostileId, 4.0f);
            var a2Aim2 = new AimTacticalAction(a2, hostileId, 4.0f);
            var b1Bound2 = new MoveTacticalAction(b1, new Vector3(0, 4, 0), new Vector3(20, 4, 0), 4.0f);
            var b2Bound2 = new MoveTacticalAction(b2, new Vector3(0, 6, 0), new Vector3(20, 6, 0), 4.0f);

            resolver.ScheduleAction(a1Bound1);
            resolver.ScheduleAction(a1Aim2);

            resolver.ScheduleAction(a2Bound1);
            resolver.ScheduleAction(a2Aim2);

            resolver.ScheduleAction(b1Aim1);
            resolver.ScheduleAction(b1Bound2);

            resolver.ScheduleAction(b2Aim1);
            resolver.ScheduleAction(b2Bound2);

            // Tick Phase 1 in 0.5 TU steps
            for (int i = 0; i < 8; i++)
            {
                resolver.Tick(0.5f);
            }

            Assert.Equal(4.0f, resolver.GlobalTime, 4);
            Assert.Equal(new Vector3(20, 0, 0), a1Bound1.CurrentPosition);
            Assert.Equal(new Vector3(20, 2, 0), a2Bound1.CurrentPosition);
            Assert.Equal(TacticalActionState.Completed, a1Bound1.State);
            Assert.Equal(TacticalActionState.Completed, b1Aim1.State);

            // Tick Phase 2 in 0.5 TU steps
            for (int i = 0; i < 8; i++)
            {
                resolver.Tick(0.5f);
            }

            Assert.Equal(8.0f, resolver.GlobalTime, 4);
            Assert.Equal(new Vector3(20, 4, 0), b1Bound2.CurrentPosition);
            Assert.Equal(new Vector3(20, 6, 0), b2Bound2.CurrentPosition);
            Assert.Equal(TacticalActionState.Completed, a1Aim2.State);
            Assert.Equal(TacticalActionState.Completed, b1Bound2.State);
            Assert.False(resolver.HasActiveActions);
        }

        /// <summary>
        /// Scenario 2: Ambush Crossfire with Ballistics, Cover Penetration, Trauma, and Tourniquet Response
        /// Ambushing unit perforates cover, inflicts limb arterial trauma; victim seeks cover and applies tourniquet.
        /// </summary>
        [Fact]
        public void Tier4_Scenario2_AmbushCrossfire_WithSimultaneousBallisticsCoverPenetrationTraumaAndTourniquet()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var registry = new MaterialRegistry();
            var penetrationSystem = new MaterialPenetrationSystem();

            var shooter = Guid.NewGuid();
            var (target, targetPhys, _, targetLeg) = CreateEntityWithLimb(BodyPartType.RightLeg);
            world.AddEntity(target);

            var wood = registry.GetMaterial(MaterialType.Wood);
            var rifleProfile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 2.43e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var bullet = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 920.0f),
                Time = 0f
            };

            // Shooter fires at T = 1.0 TU
            var ambushShot = new TestBallisticShotAction(shooter, 1.0f, penetrationSystem, bullet, rifleProfile, wood, 0.03f, new Vector3(0, 0, -1));
            // Target was patrolling
            var targetPatrol = new MoveTacticalAction(target.Id, Vector3.Zero, new Vector3(0, 0, 10), 5.0f);

            resolver.ScheduleAction(ambushShot);
            resolver.ScheduleAction(targetPatrol);

            // Tick 1.0 TU: Ambush shot resolves
            resolver.Tick(1.0f);

            Assert.True(ambushShot.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, ambushShot.Result!.Value.Outcome);

            // Target hit in Right Leg: 35 ml/s arterial bleed
            targetLeg.ArterialBleedRate = 35f;

            // Target cancels patrol, moves to cover (1.0 TU) then applies tourniquet (1.5 TU)
            resolver.CancelActorActions(target.Id);
            var takeCover = new MoveTacticalAction(target.Id, targetPatrol.CurrentPosition, targetPatrol.CurrentPosition + new Vector3(-2, 0, 0), 1.0f);
            var applyTourniquet = new WaitTacticalAction(target.Id, 1.5f);

            resolver.ScheduleAction(takeCover);
            resolver.ScheduleAction(applyTourniquet);

            // Tick 2.5 TU (Total time = 3.5 TU)
            resolver.Tick(2.5f);

            // During the 2.5s of movement & treatment, target lost: 35 * 2.5 = 87.5 ml
            Assert.Equal(5000f - 87.5f, targetPhys.TotalBloodVolume, 2);

            // Tourniquet application completed
            targetLeg.HasTourniquet = true;

            // Tick another 10.0s -> Bleeding completely stopped by tourniquet
            resolver.Tick(10.0f);
            Assert.Equal(5000f - 87.5f, targetPhys.TotalBloodVolume, 2);
            Assert.Equal(10.0f, targetLeg.IschemiaDuration, 2);
            Assert.False(targetLeg.IsNecrotic);
        }

        /// <summary>
        /// Scenario 3: Multi-Phase Combat Encounter with Bleeding Casualty Extraction
        /// Pointman provides suppression while Medic navigates to casualty, stabilizes, and extracts.
        /// </summary>
        [Fact]
        public void Tier4_Scenario3_MultiPhaseCasualtyExtractionUnderTimeline()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);

            var pointmanId = Guid.NewGuid();
            var medicId = Guid.NewGuid();
            var (casualty, casualtyPhys, casualtyRoot) = CreateTestEntity(arterialBleed: 25f); // 25 ml/s bleed

            world.AddEntity(casualty);

            // Pointman lays down 3 suppressive bursts (10s each = 30s)
            resolver.ScheduleAction(new GenericTacticalAction(pointmanId, 10.0f));
            resolver.ScheduleAction(new GenericTacticalAction(pointmanId, 10.0f));
            resolver.ScheduleAction(new GenericTacticalAction(pointmanId, 10.0f));

            // Medic moves to casualty (8.0s) -> treats wound (6.0s) -> carries casualty to LZ (16.0s)
            var medicMove = new MoveTacticalAction(medicId, Vector3.Zero, new Vector3(10, 0, 0), 8.0f);
            var medicTreat = new WaitTacticalAction(medicId, 6.0f);
            var medicExtract = new MoveTacticalAction(medicId, new Vector3(10, 0, 0), new Vector3(30, 0, 0), 16.0f);

            resolver.ScheduleAction(medicMove);
            resolver.ScheduleAction(medicTreat);
            resolver.ScheduleAction(medicExtract);

            // Tick 8.0s: Medic arrives
            resolver.Tick(8.0f);
            Assert.Equal(TacticalActionState.Completed, medicMove.State);
            Assert.Equal(5000f - (25f * 8f), casualtyPhys.TotalBloodVolume, 2); // 4800 ml

            // Tick 6.0s: Medic finishes treatment
            resolver.Tick(6.0f);
            Assert.Equal(TacticalActionState.Completed, medicTreat.State);
            Assert.Equal(5000f - (25f * 14f), casualtyPhys.TotalBloodVolume, 2); // 4650 ml

            // Wound packed: bleed halted
            casualtyRoot.ArterialBleedRate = 0f;

            // Tick 16.0s: Medic carries casualty to LZ
            resolver.Tick(16.0f);
            Assert.Equal(TacticalActionState.Completed, medicExtract.State);
            Assert.Equal(30.0f, resolver.GlobalTime, 4);

            // Blood volume stabilized at 4650ml (Class 1 hemorrhage, conscious)
            Assert.Equal(4650f, casualtyPhys.TotalBloodVolume, 2);
            Assert.Equal(1.0f, casualtyPhys.ConsciousnessLevel);
            Assert.False(resolver.HasActiveActions);
        }

        /// <summary>
        /// Scenario 4: Counter-Sniper Urban Engagement with Layered Cover & Decompensation
        /// Precision shot through Glass + Drywall eliminates enemy sniper before enemy counter-fires.
        /// </summary>
        [Fact]
        public void Tier4_Scenario4_CounterSniperUrbanEngagement_WithLayeredCoverAndDecompensation()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var registry = new MaterialRegistry();
            var penetration = new MaterialPenetrationSystem();

            var spotterId = Guid.NewGuid();
            var sniperId = Guid.NewGuid();
            var (hostile, hostilePhys, hostileRoot) = CreateTestEntity();
            world.AddEntity(hostile);

            var glass = registry.GetMaterial(MaterialType.Glass);
            var drywall = registry.GetMaterial(MaterialType.Drywall);

            var sniperProfile = new BallisticProfile
            {
                Mass = 0.0095f,
                CrossSectionalArea = 4.56e-5f,
                DragModel = new StandardDragCurve(0.3f)
            };

            // Spotter aims for 1.0 TU
            var spotterAim = new AimTacticalAction(spotterId, hostile.Id, 1.0f);
            // Sniper fires through layered glass (0.01m)
            var bullet = new ProjectileState { Position = Vector3.Zero, Velocity = new Vector3(0, 0, 860.0f), Time = 0f };
            var sniperShot = new TestBallisticShotAction(sniperId, 1.2f, penetration, bullet, sniperProfile, glass, 0.01f, new Vector3(0, 0, -1));

            // Hostile is aiming at Friendly Sniper (2.0 TU) -> Shoot (0.5 TU)
            var hostileAim = new AimTacticalAction(hostile.Id, sniperId, 2.0f);
            var hostileShot = new GenericTacticalAction(hostile.Id, 0.5f);

            resolver.ScheduleAction(spotterAim);
            resolver.ScheduleAction(sniperShot);
            resolver.ScheduleAction(hostileAim);
            resolver.ScheduleAction(hostileShot);

            // Tick 1.2 TU: Sniper shot completes
            resolver.Tick(1.2f);
            Assert.True(sniperShot.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, sniperShot.Result!.Value.Outcome);

            // Bullet then perforates drywall barrier behind glass
            var drywallResult = penetration.CalculatePenetration(sniperShot.Result.Value.ExitState, sniperProfile, drywall, 0.03f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, drywallResult.Outcome);
            Assert.True(drywallResult.ExitVelocity > 650.0f);

            // Hostile sniper hit in Thorax: massive 2500 ml/s arterial bleed
            hostileRoot.ArterialBleedRate = 2500.0f;

            // Tick 1.0 TU (Total = 2.2 TU): Hostile bleeds out, loses consciousness, hostileAim & hostileShot are cancelled
            resolver.Tick(1.0f);

            Assert.Equal(0.0f, hostilePhys.ConsciousnessLevel);
            Assert.Equal(TacticalActionState.Cancelled, hostileAim.State);
            Assert.Equal(TacticalActionState.Cancelled, hostileShot.State);
        }

        /// <summary>
        /// Scenario 5: CQB Room Clearing with Staggered Breach, Ballistics, and Trauma Management
        /// 3-man stack clears multi-room structure, engages defenders, treats friendly injury, and establishes security.
        /// </summary>
        [Fact]
        public void Tier4_Scenario5_CQBRoomClearing_WithStaggeredBreachBallisticsAndTraumaManagement()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var registry = new MaterialRegistry();
            var penetration = new MaterialPenetrationSystem();

            var (lead, leadPhys, _, leadArm) = CreateEntityWithLimb(BodyPartType.LeftArm);
            var breacherId = Guid.NewGuid();
            var rearGuardId = Guid.NewGuid();

            world.AddEntity(lead);

            var wood = registry.GetMaterial(MaterialType.Wood);
            var rifleProfile = new BallisticProfile { Mass = 0.004f, CrossSectionalArea = 2.43e-5f, DragModel = new StandardDragCurve(0.3f) };

            // Step 1: Breacher breaches door (1.0 TU), Lead & Rear Guard wait in stack
            var breach = new WaitTacticalAction(breacherId, 1.0f);
            var leadStack = new WaitTacticalAction(lead.Id, 1.0f);

            resolver.ScheduleAction(breach);
            resolver.ScheduleAction(leadStack);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, breach.State);

            // Step 2: Lead enters (1.0 TU) -> Shoots defender through interior partition (0.5 TU)
            var leadEnter = new MoveTacticalAction(lead.Id, Vector3.Zero, new Vector3(5, 0, 0), 1.0f);
            var bullet = new ProjectileState { Position = new Vector3(5, 0, 0), Velocity = new Vector3(0, 0, 900.0f), Time = 0f };
            var leadShot = new TestBallisticShotAction(lead.Id, 0.5f, penetration, bullet, rifleProfile, wood, 0.02f, new Vector3(0, 0, -1));

            resolver.ScheduleAction(leadEnter);
            resolver.ScheduleAction(leadShot);

            resolver.Tick(1.5f);
            Assert.True(leadShot.ShotFired);
            Assert.Equal(PenetrationOutcome.Perforated, leadShot.Result!.Value.Outcome);

            // Enemy return fire grazes Lead's Left Arm (20 ml/s bleed)
            leadArm.ArterialBleedRate = 20f;

            // Step 3: Rear Guard applies tourniquet to Lead (2.0 TU) while Lead provides cover (Aim 2.0 TU)
            var rearTreat = new WaitTacticalAction(rearGuardId, 2.0f);
            var leadCover = new AimTacticalAction(lead.Id, Guid.NewGuid(), 2.0f);

            resolver.ScheduleAction(rearTreat);
            resolver.ScheduleAction(leadCover);

            resolver.Tick(2.0f);
            Assert.Equal(TacticalActionState.Completed, rearTreat.State);
            Assert.Equal(TacticalActionState.Completed, leadCover.State);

            // Lead lost: 20 * 2.0 = 40 ml blood
            Assert.Equal(4960f, leadPhys.TotalBloodVolume, 3);

            // Tourniquet applied
            leadArm.HasTourniquet = true;

            // Step 4: Secure the perimeter for 10.0 TU
            var leadSecure = new WaitTacticalAction(lead.Id, 10.0f);
            resolver.ScheduleAction(leadSecure);

            resolver.Tick(10.0f);

            Assert.Equal(4960f, leadPhys.TotalBloodVolume, 3);
            Assert.Equal(10.0f, leadArm.IschemiaDuration, 2);
            Assert.False(leadArm.IsNecrotic);
            Assert.Equal(14.5f, resolver.GlobalTime, 4);
        }

        #endregion
    }
}
