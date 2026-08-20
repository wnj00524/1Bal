using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.World;
using Xunit;

namespace TacticalSim.Tests
{
    public class TurnResolverChallenger2Tests
    {
        [Fact]
        public void ActorResolution_100RandomActors_AlwaysExecutesStrictlySortedByActorId()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actors = Enumerable.Range(0, 100)
                .Select(_ => Guid.NewGuid())
                .ToList();

            var expectedSorted = actors.OrderBy(id => id).ToList();
            var actualExecutionOrder = new List<Guid>();

            // Schedule in arbitrary shuffled order
            foreach (var actorId in actors)
            {
                resolver.ScheduleAction(new GenericTacticalAction(
                    actorId,
                    tuCost: 1.0f,
                    onExecute: _ => actualExecutionOrder.Add(actorId)));
            }

            // Tick 0.1 TU
            resolver.Tick(0.1f);

            Assert.Equal(100, actualExecutionOrder.Count);
            Assert.Equal(expectedSorted, actualExecutionOrder);
        }

        [Fact]
        public void TimelineMonotonicity_1000FractionatedTicks_MaintainsStrictMonotonicityAndEventConsistency()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            float lastGlobalTime = 0.0f;
            int eventCount = 0;

            resolver.TimeAdvanced += (_, e) =>
            {
                eventCount++;
                Assert.True(e.CurrentGlobalTime > e.PreviousGlobalTime, "Global time must strictly increase.");
                Assert.Equal(lastGlobalTime, e.PreviousGlobalTime, 4);
                Assert.Equal(e.PreviousGlobalTime + e.DeltaTime, e.CurrentGlobalTime, 4);
                lastGlobalTime = e.CurrentGlobalTime;
            };

            float dt = 0.0333333f; // ~30 fps step
            for (int i = 0; i < 1000; i++)
            {
                resolver.Tick(dt);
                Assert.Equal(lastGlobalTime, resolver.GlobalTime, 4);
            }

            Assert.Equal(1000, eventCount);
            Assert.Equal(1000 * dt, resolver.GlobalTime, 2);
        }

        [Fact]
        public void TimelineMonotonicity_VaryingRandomDeltaTimes_NeverDecreases()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var rng = new Random(42);
            float prevTime = 0f;

            for (int i = 0; i < 500; i++)
            {
                float randomDt = (float)(rng.NextDouble() * 0.5 + 0.001);
                resolver.Tick(randomDt);

                Assert.True(resolver.GlobalTime > prevTime);
                prevTime = resolver.GlobalTime;
            }
        }

        [Fact]
        public void MoveTacticalAction_3DDirectionalInterpolation_NormalizedProgress_ExactCoordinates()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var start = new Vector3(-10f, 20f, -5f);
            var target = new Vector3(30f, -60f, 75f);
            float tuCost = 4.0f;

            var move = new MoveTacticalAction(actorId, start, target, tuCost);
            resolver.ScheduleAction(move);

            float expectedDist = Vector3.Distance(start, target);
            Assert.Equal(expectedDist, move.Distance, 3);
            Assert.Equal(expectedDist / tuCost, move.MovementSpeed, 3);
            Assert.Equal(start, move.CurrentPosition);

            // Step 1: 1.0 TU (25% progress)
            resolver.Tick(1.0f);
            Assert.Equal(0.25f, move.NormalizedProgress, 4);
            var expected25 = Vector3.Lerp(start, target, 0.25f);
            Assert.Equal(expected25.X, move.CurrentPosition.X, 3);
            Assert.Equal(expected25.Y, move.CurrentPosition.Y, 3);
            Assert.Equal(expected25.Z, move.CurrentPosition.Z, 3);

            // Step 2: 1.0 TU (50% progress)
            resolver.Tick(1.0f);
            Assert.Equal(0.50f, move.NormalizedProgress, 4);
            var expected50 = Vector3.Lerp(start, target, 0.50f);
            Assert.Equal(expected50.X, move.CurrentPosition.X, 3);
            Assert.Equal(expected50.Y, move.CurrentPosition.Y, 3);
            Assert.Equal(expected50.Z, move.CurrentPosition.Z, 3);

            // Step 3: 2.0 TU (100% progress -> complete)
            resolver.Tick(2.0f);
            Assert.Equal(1.0f, move.NormalizedProgress, 4);
            Assert.Equal(target.X, move.CurrentPosition.X, 3);
            Assert.Equal(target.Y, move.CurrentPosition.Y, 3);
            Assert.Equal(target.Z, move.CurrentPosition.Z, 3);
            Assert.True(move.IsComplete);
        }

        [Fact]
        public void MoveTacticalAction_ZeroDistance_HandledGracefully()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var pos = new Vector3(15f, -20f, 30f);

            var move = new MoveTacticalAction(actorId, pos, pos, tuCost: 1.0f);
            Assert.Equal(0.0f, move.Distance, 4);
            Assert.Equal(0.0f, move.MovementSpeed, 4);

            resolver.ScheduleAction(move);
            resolver.Tick(0.5f);
            Assert.Equal(pos, move.CurrentPosition);

            resolver.Tick(0.5f);
            Assert.Equal(pos, move.CurrentPosition);
            Assert.True(move.IsComplete);
        }

        [Fact]
        public void MoveTacticalAction_ChainedMovementsWithSubTickCarryover_TraversesWaypointsAccurately()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var p0 = new Vector3(0, 0, 0);
            var p1 = new Vector3(10, 0, 0);
            var p2 = new Vector3(10, 10, 0);
            var p3 = new Vector3(10, 10, 10);

            var leg1 = new MoveTacticalAction(actorId, p0, p1, 1.0f);
            var leg2 = new MoveTacticalAction(actorId, p1, p2, 1.0f);
            var leg3 = new MoveTacticalAction(actorId, p2, p3, 1.0f);

            resolver.ScheduleAction(leg1);
            resolver.ScheduleAction(leg2);
            resolver.ScheduleAction(leg3);

            // Tick 1.5 TU: leg1 completes fully (1.0 TU), leg2 progresses 0.5 TU halfway to p2
            resolver.Tick(1.5f);

            Assert.Equal(TacticalActionState.Completed, leg1.State);
            Assert.Equal(p1, leg1.CurrentPosition);

            Assert.Equal(TacticalActionState.Executing, leg2.State);
            Assert.Equal(0.5f, leg2.NormalizedProgress, 4);
            Assert.Equal(new Vector3(10, 5, 0), leg2.CurrentPosition);

            // Tick 1.5 TU: leg2 completes (0.5 TU remaining), leg3 completes (1.0 TU)
            resolver.Tick(1.5f);

            Assert.Equal(TacticalActionState.Completed, leg2.State);
            Assert.Equal(p2, leg2.CurrentPosition);

            Assert.Equal(TacticalActionState.Completed, leg3.State);
            Assert.Equal(p3, leg3.CurrentPosition);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void MoveTacticalAction_CostFromSpeed_CalculatesCorrectTUCostAndSpeed()
        {
            var actorId = Guid.NewGuid();
            var start = new Vector3(0, 0, 0);
            var target = new Vector3(6, 8, 0); // Distance = 10

            var move = new MoveTacticalAction(actorId, start, target, movementSpeed: 5.0f, computeCostFromSpeed: true);

            Assert.Equal(10.0f, move.Distance, 4);
            Assert.Equal(2.0f, move.TUCost, 4);
            Assert.Equal(5.0f, move.MovementSpeed, 4);
        }

        [Fact]
        public void AimTacticalAction_LinearProgressionAndScaling_MatchesExpectedBonus()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            float maxBonus = 0.75f;
            float tuCost = 3.0f;

            var aim = new AimTacticalAction(actorId, targetId, tuCost, maxBonus);
            resolver.ScheduleAction(aim);

            Assert.Equal(0.0f, aim.CurrentAimBonus, 4);

            // Tick 1.0 TU -> 1/3 progress
            resolver.Tick(1.0f);
            Assert.Equal(maxBonus * (1.0f / 3.0f), aim.CurrentAimBonus, 4);

            // Tick 1.0 TU -> 2/3 progress
            resolver.Tick(1.0f);
            Assert.Equal(maxBonus * (2.0f / 3.0f), aim.CurrentAimBonus, 4);

            // Tick 1.0 TU -> 3/3 progress (100%)
            resolver.Tick(1.0f);
            Assert.Equal(maxBonus, aim.CurrentAimBonus, 4);
            Assert.True(aim.IsComplete);
        }

        [Fact]
        public void AimTacticalAction_ChainedAimAndMove_SubTickInterleaving()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var aim = new AimTacticalAction(actorId, targetId, tuCost: 0.5f, maxAimBonus: 0.4f);
            var move = new MoveTacticalAction(actorId, Vector3.Zero, new Vector3(10, 0, 0), tuCost: 1.0f);

            resolver.ScheduleAction(aim);
            resolver.ScheduleAction(move);

            // Tick 1.0 TU: aim completes at 0.5s with full bonus; move executes for 0.5s (50% progress)
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, aim.State);
            Assert.Equal(0.4f, aim.CurrentAimBonus, 4);
            Assert.Equal(0.5f, aim.CompletionTime!.Value, 4);

            Assert.Equal(TacticalActionState.Executing, move.State);
            Assert.Equal(0.5f, move.NormalizedProgress, 4);
            Assert.Equal(new Vector3(5, 0, 0), move.CurrentPosition);
        }

        [Theory]
        [InlineData(TacticalActionState.Executing)]
        [InlineData(TacticalActionState.Completed)]
        [InlineData(TacticalActionState.Cancelled)]
        [InlineData(TacticalActionState.Failed)]
        public void EdgeCase_ScheduleAction_AllNonPendingStates_ThrowsInvalidOperationException(TacticalActionState state)
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f)
            {
                State = state
            };

            Assert.Throws<InvalidOperationException>(() => resolver.ScheduleAction(action));
        }

        [Fact]
        public void EdgeCase_ResetDuringActiveExecution_ClearsAllState_AllowsCleanRestartFromZero()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(a1, 2.0f));
            resolver.ScheduleAction(new GenericTacticalAction(a1, 2.0f)); // queued
            resolver.ScheduleAction(new GenericTacticalAction(a2, 3.0f));

            resolver.Tick(1.0f);

            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.Equal(2, resolver.ActiveActorCount);
            Assert.Single(resolver.GetQueuedActions(a1));

            // Perform Reset
            resolver.Reset();

            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.False(resolver.HasActiveActions);
            Assert.Empty(resolver.GetActiveActions());
            Assert.Empty(resolver.GetQueuedActions(a1));
            Assert.Empty(resolver.GetQueuedActions(a2));
            Assert.Null(resolver.GetCurrentAction(a1));
            Assert.Null(resolver.GetCurrentAction(a2));

            // Re-schedule and verify clean execution from T=0
            var freshAction = new GenericTacticalAction(a1, 1.0f);
            resolver.ScheduleAction(freshAction);
            resolver.Tick(1.0f);

            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.Equal(0.0f, freshAction.StartTime, 4);
            Assert.Equal(1.0f, freshAction.CompletionTime!.Value, 4);
            Assert.Equal(TacticalActionState.Completed, freshAction.State);
        }

        [Fact]
        public void EdgeCase_CancelNonExistentOrEmpty_ReturnsFalseOrZero()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var a1 = Guid.NewGuid();
            resolver.ScheduleAction(new GenericTacticalAction(a1, 1.0f));

            Assert.False(resolver.CancelAction(Guid.Empty));
            Assert.False(resolver.CancelAction(Guid.NewGuid()));
            Assert.Equal(0, resolver.CancelActorActions(Guid.Empty));
            Assert.Equal(0, resolver.CancelActorActions(Guid.NewGuid()));
        }

        [Fact]
        public void EdgeCase_CancelAlreadyCompletedOrCancelledAction_ReturnsFalse()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var a1 = Guid.NewGuid();
            var act = new GenericTacticalAction(a1, 1.0f);
            resolver.ScheduleAction(act);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, act.State);

            // Attempting to cancel already completed action
            Assert.False(resolver.CancelAction(act.Id));

            // Attempting to cancel actor whose actions are all completed
            Assert.Equal(0, resolver.CancelActorActions(a1));
        }

        [Fact]
        public void EdgeCase_LargeDeltaTime_ResolvesFiftyQueuedActionsInSingleTick()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            const int count = 50;
            var actions = new List<GenericTacticalAction>();

            for (int i = 0; i < count; i++)
            {
                var act = new GenericTacticalAction(actorId, 0.1f);
                actions.Add(act);
                resolver.ScheduleAction(act);
            }

            Assert.Equal(count - 1, resolver.GetQueuedActions(actorId).Count);

            // Tick 10.0 TU (much larger than 50 * 0.1 = 5.0 TU)
            resolver.Tick(10.0f);

            for (int i = 0; i < count; i++)
            {
                Assert.Equal(TacticalActionState.Completed, actions[i].State);
                Assert.Equal(0.1f, actions[i].ExecutionProgress, 4);
                Assert.Equal((i + 1) * 0.1f, actions[i].CompletionTime!.Value, 4);
            }

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(10.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void EdgeCase_SubEpsilonDeltaTime_HandledWithoutInfiniteLoop()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            // Tick with delta time smaller than epsilon (1e-6 < 1e-5)
            resolver.Tick(1e-6f);

            Assert.Equal(1e-6f, resolver.GlobalTime, 6);
            Assert.Equal(TacticalActionState.Pending, action.State);
        }

        [Fact]
        public void EdgeCase_MicroCostAction_ResolvesCorrectlyWithCarryover()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var micro1 = new GenericTacticalAction(actorId, 0.001f);
            var micro2 = new GenericTacticalAction(actorId, 0.001f);

            resolver.ScheduleAction(micro1);
            resolver.ScheduleAction(micro2);

            resolver.Tick(0.005f);

            Assert.Equal(TacticalActionState.Completed, micro1.State);
            Assert.Equal(TacticalActionState.Completed, micro2.State);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0.005f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void LifecycleEventOrder_StrictSequenceVerified()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var eventLog = new List<string>();

            resolver.ActionScheduled += (_, _) => eventLog.Add("Scheduled");
            resolver.ActionStarted += (_, _) => eventLog.Add("Started");
            resolver.ActionProgressed += (_, _) => eventLog.Add("Progressed");
            resolver.ActionCompleted += (_, _) => eventLog.Add("Completed");
            resolver.TimeAdvanced += (_, _) => eventLog.Add("TimeAdvanced");

            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            // Tick 0.5 TU
            resolver.Tick(0.5f);

            // Tick 0.5 TU (completes)
            resolver.Tick(0.5f);

            var expected = new List<string>
            {
                "Scheduled",
                "Started",
                "Progressed",
                "TimeAdvanced",
                "Progressed",
                "Completed",
                "TimeAdvanced"
            };

            Assert.Equal(expected, eventLog);
        }

        [Fact]
        public void ExceptionIsolation_MultipleFailingActors_OtherActorsAndTimelineUnaffected()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var failActor1 = Guid.NewGuid();
            var failActor2 = Guid.NewGuid();
            var healthyActor = Guid.NewGuid();

            var a1 = new GenericTacticalAction(failActor1, 1.0f, onExecute: _ => throw new InvalidOperationException("Fail 1"));
            var a2 = new GenericTacticalAction(failActor2, 1.0f, onExecute: _ => throw new FormatException("Fail 2"));
            var healthy = new GenericTacticalAction(healthyActor, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(healthy);

            var failedList = new List<ActionFailedEventArgs>();
            resolver.ActionFailed += (_, e) => failedList.Add(e);

            resolver.Tick(0.5f);

            Assert.Equal(2, failedList.Count);
            Assert.Equal(TacticalActionState.Failed, a1.State);
            Assert.Equal(TacticalActionState.Failed, a2.State);
            Assert.Equal(TacticalActionState.Executing, healthy.State);
            Assert.Equal(0.5f, healthy.ExecutionProgress, 4);
            Assert.Equal(0.5f, resolver.GlobalTime, 4);

            // Complete healthy actor
            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, healthy.State);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void StateClamping_ExecutionProgressNeverExceedsTUCost()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var action = new GenericTacticalAction(actorId, 0.777f);

            resolver.ScheduleAction(action);
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(0.777f, action.ExecutionProgress, 4);
            Assert.Equal(1.0f, action.NormalizedProgress, 4);
            Assert.Equal(0.0f, action.RemainingTU, 4);
        }

        [Fact]
        public void EventReentrancy_ScheduleInsideActionCompleted_ExecutesNextTick()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var act1 = new GenericTacticalAction(actorId, 1.0f);
            var act2 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ActionCompleted += (sender, e) =>
            {
                if (e.Action == act1)
                {
                    ((TurnResolver)sender!).ScheduleAction(act2);
                }
            };

            resolver.ScheduleAction(act1);

            // Tick 1.0 TU: completes act1, schedules act2 in handler
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Pending, act2.State);
            Assert.Same(act2, resolver.GetCurrentAction(actorId));

            // Next tick 1.0 TU: executes act2 to completion
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, act2.State);
            Assert.Equal(2.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void ActionSelfCancellation_InsideExecuteCallback_HandlesGracefully()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            GenericTacticalAction? selfAction = null;

            selfAction = new GenericTacticalAction(actorId, 2.0f, onExecute: _ =>
            {
                resolver.CancelAction(selfAction!.Id);
            });

            resolver.ScheduleAction(selfAction);

            // Tick should execute callback, trigger cancel, and finish tick without throwing
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Cancelled, selfAction.State);
            Assert.Null(resolver.GetCurrentAction(actorId));
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void MassiveConcurrency_50ActorsWith10ActionsEach_500ActionsInterleavedCorrectly()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            const int actorCount = 50;
            const int actionsPerActor = 10;
            const float actionCost = 0.2f;

            var actors = Enumerable.Range(0, actorCount).Select(_ => Guid.NewGuid()).ToList();
            var completedTracker = new Dictionary<Guid, int>();

            foreach (var actor in actors)
            {
                completedTracker[actor] = 0;
                for (int i = 0; i < actionsPerActor; i++)
                {
                    resolver.ScheduleAction(new GenericTacticalAction(actor, actionCost, onComplete: () =>
                    {
                        completedTracker[actor]++;
                    }));
                }
            }

            // Total cost per actor = 10 * 0.2 = 2.0 TU.
            // Tick in 20 steps of 0.1 TU = 2.0 TU total
            for (int step = 0; step < 20; step++)
            {
                resolver.Tick(0.1f);
            }

            Assert.Equal(2.0f, resolver.GlobalTime, 3);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);

            foreach (var actor in actors)
            {
                Assert.Equal(actionsPerActor, completedTracker[actor]);
            }
        }

        [Fact]
        public void MoveTacticalAction_100FractionatedSubSteps_SmoothInterpolationWithoutDrift()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();
            var start = new Vector3(0, 0, 0);
            var target = new Vector3(100f, 200f, 300f);
            float totalCost = 1.0f;

            var move = new MoveTacticalAction(actorId, start, target, totalCost);
            resolver.ScheduleAction(move);

            for (int i = 1; i <= 100; i++)
            {
                resolver.Tick(0.01f);
                float expectedFraction = i * 0.01f;
                var expectedPos = Vector3.Lerp(start, target, expectedFraction);

                Assert.Equal(expectedPos.X, move.CurrentPosition.X, 2);
                Assert.Equal(expectedPos.Y, move.CurrentPosition.Y, 2);
                Assert.Equal(expectedPos.Z, move.CurrentPosition.Z, 2);
            }

            Assert.Equal(target, move.CurrentPosition);
            Assert.True(move.IsComplete);
        }

        [Fact]
        public void AimTacticalAction_EdgeValues_NegativeAndZeroBonus()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actor1 = Guid.NewGuid();
            var actor2 = Guid.NewGuid();
            var target = Guid.NewGuid();

            var zeroBonusAim = new AimTacticalAction(actor1, target, tuCost: 1.0f, maxAimBonus: 0.0f);
            var negBonusAim = new AimTacticalAction(actor2, target, tuCost: 1.0f, maxAimBonus: -0.5f);

            resolver.ScheduleAction(zeroBonusAim);
            resolver.ScheduleAction(negBonusAim);

            resolver.Tick(0.5f);

            Assert.Equal(0.0f, zeroBonusAim.CurrentAimBonus, 4);
            Assert.Equal(-0.25f, negBonusAim.CurrentAimBonus, 4);

            resolver.Tick(0.5f);

            Assert.Equal(0.0f, zeroBonusAim.CurrentAimBonus, 4);
            Assert.Equal(-0.50f, negBonusAim.CurrentAimBonus, 4);
        }
    }
}
