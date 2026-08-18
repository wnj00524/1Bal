using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    public class TurnResolverTests
    {
        [Fact]
        public void InitialState_HasZeroGlobalTime_AndNoActiveActions()
        {
            var resolver = new TurnResolver();

            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Empty(resolver.GetActiveActions());
        }

        [Theory]
        [InlineData(0.0f)]
        [InlineData(-0.1f)]
        [InlineData(-5.0f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void Tick_InvalidDeltaTime_ThrowsArgumentException(float invalidDt)
        {
            var resolver = new TurnResolver();

            Assert.Throws<ArgumentException>(() => resolver.Tick(invalidDt));
        }

        [Fact]
        public void Tick_AdvancesGlobalTime_AndFiresTimeAdvancedEvent()
        {
            var resolver = new TurnResolver();
            var events = new List<TimeAdvancedEventArgs>();
            resolver.TimeAdvanced += (_, e) => events.Add(e);

            resolver.Tick(0.25f);
            resolver.Tick(0.5f);

            Assert.Equal(0.75f, resolver.GlobalTime, 4);
            Assert.Equal(2, events.Count);

            Assert.Equal(0.25f, events[0].DeltaTime, 4);
            Assert.Equal(0.0f, events[0].PreviousGlobalTime, 4);
            Assert.Equal(0.25f, events[0].CurrentGlobalTime, 4);

            Assert.Equal(0.5f, events[1].DeltaTime, 4);
            Assert.Equal(0.25f, events[1].PreviousGlobalTime, 4);
            Assert.Equal(0.75f, events[1].CurrentGlobalTime, 4);
        }

        [Fact]
        public void ScheduleAction_NullAction_ThrowsArgumentNullException()
        {
            var resolver = new TurnResolver();

            Assert.Throws<ArgumentNullException>(() => resolver.ScheduleAction(null!));
        }

        [Fact]
        public void ScheduleAction_EmptyActorId_ThrowsArgumentException()
        {
            var resolver = new TurnResolver();
            var action = new GenericTacticalAction(Guid.Empty, 1.0f);

            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(action));
        }

        [Theory]
        [InlineData(0.0f)]
        [InlineData(-1.0f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void ScheduleAction_InvalidTUCost_ThrowsArgumentException(float invalidCost)
        {
            var resolver = new TurnResolver();
            var action = new GenericTacticalAction(Guid.NewGuid(), invalidCost);

            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(action));
        }

        [Fact]
        public void ScheduleAction_NonPendingState_ThrowsInvalidOperationException()
        {
            var resolver = new TurnResolver();
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f)
            {
                State = TacticalActionState.Executing
            };

            Assert.Throws<InvalidOperationException>(() => resolver.ScheduleAction(action));
        }

        [Fact]
        public void SingleActor_FullLifecycle_ExecutionAndEvents()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            float executedDelta = 0.0f;
            int executeCount = 0;
            bool startedCalled = false;
            bool completedCalled = false;

            var action = new GenericTacticalAction(
                actorId: actorId,
                tuCost: 1.0f,
                onExecute: dt =>
                {
                    executeCount++;
                    executedDelta += dt;
                },
                onStart: () => startedCalled = true,
                onComplete: () => completedCalled = true);

            var scheduledEvents = new List<ActionEventArgs>();
            var startedEvents = new List<ActionEventArgs>();
            var progressedEvents = new List<ActionProgressEventArgs>();
            var completedEvents = new List<ActionEventArgs>();

            resolver.ActionScheduled += (_, e) => scheduledEvents.Add(e);
            resolver.ActionStarted += (_, e) => startedEvents.Add(e);
            resolver.ActionProgressed += (_, e) => progressedEvents.Add(e);
            resolver.ActionCompleted += (_, e) => completedEvents.Add(e);

            resolver.ScheduleAction(action);

            Assert.Single(scheduledEvents);
            Assert.Equal(TacticalActionState.Pending, action.State);
            Assert.True(resolver.HasActiveActions);
            Assert.Equal(1, resolver.ActiveActorCount);
            Assert.Same(action, resolver.GetCurrentAction(actorId));

            // Tick 0.4 TU
            resolver.Tick(0.4f);

            Assert.Single(startedEvents);
            Assert.True(startedCalled);
            Assert.Equal(TacticalActionState.Executing, action.State);
            Assert.Equal(0.0f, action.StartTime);
            Assert.Equal(0.4f, action.ExecutionProgress, 4);
            Assert.Equal(0.4f, action.NormalizedProgress, 4);
            Assert.False(action.IsComplete);
            Assert.Single(progressedEvents);
            Assert.Equal(0.4f, progressedEvents[0].DeltaTime, 4);
            Assert.Equal(0.4f, progressedEvents[0].CurrentProgress, 4);
            Assert.Empty(completedEvents);

            // Tick another 0.6 TU -> completes exactly at 1.0
            resolver.Tick(0.6f);

            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(1.0f, action.ExecutionProgress, 4);
            Assert.Equal(1.0f, action.NormalizedProgress, 4);
            Assert.True(action.IsComplete);
            Assert.True(completedCalled);
            Assert.Equal(1.0f, action.CompletionTime!.Value, 4);
            Assert.Single(completedEvents);
            Assert.Equal(1.0f, completedEvents[0].GlobalTime, 4);
            Assert.Equal(2, executeCount);
            Assert.Equal(1.0f, executedDelta, 4);

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Null(resolver.GetCurrentAction(actorId));
        }

        [Fact]
        public void MultiActor_ConcurrentExecution_ResolvesIndependently()
        {
            var resolver = new TurnResolver();
            var actor1 = Guid.NewGuid();
            var actor2 = Guid.NewGuid();
            var actor3 = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actor1, 1.0f);
            var action2 = new GenericTacticalAction(actor2, 1.5f);
            var action3 = new GenericTacticalAction(actor3, 2.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);
            resolver.ScheduleAction(action3);

            Assert.Equal(3, resolver.ActiveActorCount);

            // Tick 1.0 TU
            resolver.Tick(1.0f);

            // Action 1 completes
            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Equal(1.0f, action1.ExecutionProgress, 4);
            Assert.True(action1.IsComplete);

            // Action 2 at 1.0 / 1.5
            Assert.Equal(TacticalActionState.Executing, action2.State);
            Assert.Equal(1.0f, action2.ExecutionProgress, 4);
            Assert.False(action2.IsComplete);

            // Action 3 at 1.0 / 2.0
            Assert.Equal(TacticalActionState.Executing, action3.State);
            Assert.Equal(1.0f, action3.ExecutionProgress, 4);
            Assert.False(action3.IsComplete);

            Assert.Equal(2, resolver.ActiveActorCount);

            // Tick 0.5 TU
            resolver.Tick(0.5f);

            // Action 2 completes
            Assert.Equal(TacticalActionState.Completed, action2.State);
            Assert.Equal(1.5f, action2.ExecutionProgress, 4);
            Assert.True(action2.IsComplete);

            // Action 3 at 1.5 / 2.0
            Assert.Equal(TacticalActionState.Executing, action3.State);
            Assert.Equal(1.5f, action3.ExecutionProgress, 4);
            Assert.False(action3.IsComplete);

            Assert.Equal(1, resolver.ActiveActorCount);

            // Tick 0.5 TU
            resolver.Tick(0.5f);

            // Action 3 completes
            Assert.Equal(TacticalActionState.Completed, action3.State);
            Assert.Equal(2.0f, action3.ExecutionProgress, 4);
            Assert.True(action3.IsComplete);

            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(2.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void SubTickCarryover_SingleTick_ExecutesMultipleQueuedActions()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actorId, 0.3f);
            var action2 = new GenericTacticalAction(actorId, 0.4f);
            var action3 = new GenericTacticalAction(actorId, 0.5f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);
            resolver.ScheduleAction(action3);

            var startedList = new List<TacticalAction>();
            var completedList = new List<TacticalAction>();
            resolver.ActionStarted += (_, e) => startedList.Add(e.Action);
            resolver.ActionCompleted += (_, e) => completedList.Add(e.Action);

            Assert.Equal(2, resolver.GetQueuedActions(actorId).Count);

            // Tick 1.0 TU:
            // - action1 (0.3 TU) starts and completes at 0.3s (remaining dt = 0.7s)
            // - action2 (0.4 TU) starts at 0.3s and completes at 0.7s (remaining dt = 0.3s)
            // - action3 (0.5 TU) starts at 0.7s and executes for 0.3s (progress = 0.3 / 0.5, remaining dt = 0)
            resolver.Tick(1.0f);

            Assert.Equal(3, startedList.Count);
            Assert.Same(action1, startedList[0]);
            Assert.Same(action2, startedList[1]);
            Assert.Same(action3, startedList[2]);

            Assert.Equal(2, completedList.Count);
            Assert.Same(action1, completedList[0]);
            Assert.Same(action2, completedList[1]);

            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Equal(0.3f, action1.ExecutionProgress, 4);
            Assert.Equal(0.3f, action1.CompletionTime!.Value, 4);

            Assert.Equal(TacticalActionState.Completed, action2.State);
            Assert.Equal(0.4f, action2.ExecutionProgress, 4);
            Assert.Equal(0.7f, action2.CompletionTime!.Value, 4);

            Assert.Equal(TacticalActionState.Executing, action3.State);
            Assert.Equal(0.3f, action3.ExecutionProgress, 4);
            Assert.Equal(0.7f, action3.StartTime, 4);
            Assert.False(action3.IsComplete);

            Assert.Empty(resolver.GetQueuedActions(actorId));
            Assert.Same(action3, resolver.GetCurrentAction(actorId));

            // Tick 0.2 TU -> completes action3
            resolver.Tick(0.2f);

            Assert.Equal(TacticalActionState.Completed, action3.State);
            Assert.Equal(0.5f, action3.ExecutionProgress, 4);
            Assert.Equal(1.2f, action3.CompletionTime!.Value, 4);
            Assert.Equal(3, completedList.Count);
            Assert.Same(action3, completedList[2]);

            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void SubTickCarryover_QueueExhaustion_IdleRemainder()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actorId, 0.2f);
            var action2 = new GenericTacticalAction(actorId, 0.2f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);

            // Tick 1.0 TU (more than total cost of 0.4 TU)
            resolver.Tick(1.0f);

            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Equal(TacticalActionState.Completed, action2.State);
            Assert.Equal(0.2f, action1.CompletionTime!.Value, 4);
            Assert.Equal(0.4f, action2.CompletionTime!.Value, 4);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void CancelAction_ActiveAction_TransitionsToCancelledAndPromotesNext()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            bool cancelHookCalled = false;
            var action1 = new GenericTacticalAction(actorId, 1.0f, onCancel: () => cancelHookCalled = true);
            var action2 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);

            var cancelledEvents = new List<ActionEventArgs>();
            resolver.ActionCancelled += (_, e) => cancelledEvents.Add(e);

            resolver.Tick(0.3f);
            Assert.Equal(TacticalActionState.Executing, action1.State);

            bool cancelled = resolver.CancelAction(action1.Id);

            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, action1.State);
            Assert.True(cancelHookCalled);
            Assert.Single(cancelledEvents);
            Assert.Same(action1, cancelledEvents[0].Action);

            // Action 2 should now be active
            Assert.Same(action2, resolver.GetCurrentAction(actorId));
            Assert.Empty(resolver.GetQueuedActions(actorId));

            // Next tick executes action2
            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Executing, action2.State);
            Assert.Equal(0.5f, action2.ExecutionProgress, 4);
        }

        [Fact]
        public void CancelAction_QueuedAction_RemovesFromQueueWithoutAffectingActive()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            var action1 = new GenericTacticalAction(actorId, 1.0f);
            var action2 = new GenericTacticalAction(actorId, 1.0f);
            var action3 = new GenericTacticalAction(actorId, 1.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);
            resolver.ScheduleAction(action3);

            // Cancel action2 in queue
            bool cancelled = resolver.CancelAction(action2.Id);

            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, action2.State);

            var queued = resolver.GetQueuedActions(actorId);
            Assert.Single(queued);
            Assert.Same(action3, queued[0]);
            Assert.Same(action1, resolver.GetCurrentAction(actorId));

            // Complete action1 -> promotes action3
            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Same(action3, resolver.GetCurrentAction(actorId));
        }

        [Fact]
        public void CancelAction_NonExistent_ReturnsFalse()
        {
            var resolver = new TurnResolver();
            var action = new GenericTacticalAction(Guid.NewGuid(), 1.0f);
            resolver.ScheduleAction(action);

            bool cancelled = resolver.CancelAction(Guid.NewGuid());
            Assert.False(cancelled);
            Assert.False(resolver.CancelAction(Guid.Empty));
        }

        [Fact]
        public void CancelActorActions_CancelsAllActiveAndQueuedActionsForActor()
        {
            var resolver = new TurnResolver();
            var actor1 = Guid.NewGuid();
            var actor2 = Guid.NewGuid();

            var a1_1 = new GenericTacticalAction(actor1, 1.0f);
            var a1_2 = new GenericTacticalAction(actor1, 1.0f);
            var a1_3 = new GenericTacticalAction(actor1, 1.0f);

            var a2_1 = new GenericTacticalAction(actor2, 1.0f);

            resolver.ScheduleAction(a1_1);
            resolver.ScheduleAction(a1_2);
            resolver.ScheduleAction(a1_3);
            resolver.ScheduleAction(a2_1);

            var cancelledEvents = new List<ActionEventArgs>();
            resolver.ActionCancelled += (_, e) => cancelledEvents.Add(e);

            int count = resolver.CancelActorActions(actor1);

            Assert.Equal(3, count);
            Assert.Equal(TacticalActionState.Cancelled, a1_1.State);
            Assert.Equal(TacticalActionState.Cancelled, a1_2.State);
            Assert.Equal(TacticalActionState.Cancelled, a1_3.State);
            Assert.Equal(3, cancelledEvents.Count);

            Assert.Null(resolver.GetCurrentAction(actor1));
            Assert.Empty(resolver.GetQueuedActions(actor1));

            // Actor 2 is unaffected
            Assert.Same(a2_1, resolver.GetCurrentAction(actor2));
            Assert.Equal(TacticalActionState.Pending, a2_1.State);

            // Unknown actor cancel returns 0
            Assert.Equal(0, resolver.CancelActorActions(Guid.NewGuid()));
            Assert.Equal(0, resolver.CancelActorActions(Guid.Empty));
        }

        [Fact]
        public void FaultIsolation_ActionThrowsException_FailsGracefullyWithoutCrashingResolver()
        {
            var resolver = new TurnResolver();
            var badActor = Guid.NewGuid();
            var goodActor = Guid.NewGuid();

            var expectedEx = new InvalidOperationException("Simulation fault simulation");
            bool failHookCalled = false;
            Exception? caughtHookEx = null;

            var badAction = new GenericTacticalAction(
                badActor,
                1.0f,
                onExecute: _ => throw expectedEx,
                onFail: ex =>
                {
                    failHookCalled = true;
                    caughtHookEx = ex;
                });

            var goodAction = new GenericTacticalAction(goodActor, 1.0f);

            resolver.ScheduleAction(badAction);
            resolver.ScheduleAction(goodAction);

            var failedEvents = new List<ActionFailedEventArgs>();
            resolver.ActionFailed += (_, e) => failedEvents.Add(e);

            // Tick should isolate exception
            resolver.Tick(0.5f);

            // Bad action failed
            Assert.Equal(TacticalActionState.Failed, badAction.State);
            Assert.Same(expectedEx, badAction.FailureException);
            Assert.True(failHookCalled);
            Assert.Same(expectedEx, caughtHookEx);
            Assert.Single(failedEvents);
            Assert.Same(badAction, failedEvents[0].Action);
            Assert.Same(expectedEx, failedEvents[0].Exception);
            Assert.Equal("Simulation fault simulation", failedEvents[0].ErrorMessage);
            Assert.Null(resolver.GetCurrentAction(badActor));

            // Good action succeeded
            Assert.Equal(TacticalActionState.Executing, goodAction.State);
            Assert.Equal(0.5f, goodAction.ExecutionProgress, 4);

            // Global time still advanced correctly
            Assert.Equal(0.5f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void Reset_ClearsAllStateAndTimeline()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();

            resolver.ScheduleAction(new GenericTacticalAction(actor, 1.0f));
            resolver.ScheduleAction(new GenericTacticalAction(actor, 1.0f));
            resolver.Tick(0.5f);

            Assert.Equal(0.5f, resolver.GlobalTime, 4);
            Assert.True(resolver.HasActiveActions);

            resolver.Reset();

            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Empty(resolver.GetActiveActions());
            Assert.Empty(resolver.GetQueuedActions(actor));
            Assert.Null(resolver.GetCurrentAction(actor));
        }

        [Fact]
        public void MoveTacticalAction_InterpolatesPositionCorrectly()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            var start = new Vector3(0, 0, 0);
            var target = new Vector3(10, 0, 0);

            var move = new MoveTacticalAction(actorId, start, target, 2.0f);

            Assert.Equal(10.0f, move.Distance, 4);
            Assert.Equal(5.0f, move.MovementSpeed, 4);
            Assert.Equal(start, move.CurrentPosition);

            resolver.ScheduleAction(move);

            // Halfway (1.0 TU out of 2.0 TU)
            resolver.Tick(1.0f);
            Assert.Equal(0.5f, move.NormalizedProgress, 4);
            Assert.Equal(new Vector3(5, 0, 0), move.CurrentPosition);

            // Complete (another 1.0 TU)
            resolver.Tick(1.0f);
            Assert.Equal(1.0f, move.NormalizedProgress, 4);
            Assert.Equal(target, move.CurrentPosition);
            Assert.True(move.IsComplete);
        }

        [Fact]
        public void MoveTacticalAction_ConstructWithSpeed_CalculatesCorrectCost()
        {
            var actorId = Guid.NewGuid();
            var start = new Vector3(0, 0, 0);
            var target = new Vector3(0, 30, 40); // distance = 50

            var move = new MoveTacticalAction(actorId, start, target, movementSpeed: 25f, computeCostFromSpeed: true);

            Assert.Equal(50f, move.Distance, 4);
            Assert.Equal(2.0f, move.TUCost, 4);
        }

        [Fact]
        public void AimTacticalAction_RampsAimBonusWithNormalizedProgress()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var aim = new AimTacticalAction(actorId, targetId, tuCost: 4.0f, maxAimBonus: 0.8f);

            Assert.Equal(targetId, aim.TargetId);
            Assert.Equal(0.8f, aim.MaxAimBonus, 4);
            Assert.Equal(0.0f, aim.CurrentAimBonus, 4);

            resolver.ScheduleAction(aim);

            // 1 TU -> 25%
            resolver.Tick(1.0f);
            Assert.Equal(0.2f, aim.CurrentAimBonus, 4);

            // 2 more TUs -> 75%
            resolver.Tick(2.0f);
            Assert.Equal(0.6f, aim.CurrentAimBonus, 4);

            // 1 more TU -> 100%
            resolver.Tick(1.0f);
            Assert.Equal(0.8f, aim.CurrentAimBonus, 4);
            Assert.True(aim.IsComplete);
        }

        [Fact]
        public void WaitTacticalAction_IdlesUntilCostComplete()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            var wait = new WaitTacticalAction(actorId, 1.5f);

            resolver.ScheduleAction(wait);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Executing, wait.State);
            Assert.Equal(1.0f, wait.ExecutionProgress, 4);

            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, wait.State);
            Assert.Equal(1.5f, wait.ExecutionProgress, 4);
            Assert.True(wait.IsComplete);
        }

        [Fact]
        public void Determinism_MultipleActorsProcessedInSortedActorIdOrder()
        {
            var resolver = new TurnResolver();
            var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

            var executionOrder = new List<Guid>();

            // Schedule in reverse order
            resolver.ScheduleAction(new GenericTacticalAction(id3, 1.0f, onExecute: _ => executionOrder.Add(id3)));
            resolver.ScheduleAction(new GenericTacticalAction(id1, 1.0f, onExecute: _ => executionOrder.Add(id1)));
            resolver.ScheduleAction(new GenericTacticalAction(id2, 1.0f, onExecute: _ => executionOrder.Add(id2)));

            resolver.Tick(0.5f);

            Assert.Equal(3, executionOrder.Count);
            Assert.Equal(id1, executionOrder[0]);
            Assert.Equal(id2, executionOrder[1]);
            Assert.Equal(id3, executionOrder[2]);
        }

        [Fact]
        public void PrecisionTolerance_FractionalStepsWithRepeatingDecimals_ResolvesAccurately()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            // Total TU cost = 1.0f, ticked in 10 steps of 0.1f (often causes floating-point drift in naive summation)
            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            for (int i = 0; i < 10; i++)
            {
                resolver.Tick(0.1f);
            }

            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.Equal(1.0f, action.ExecutionProgress, 4);
            Assert.Equal(0.0f, action.RemainingTU, 4);
            Assert.Equal(1.0f, action.NormalizedProgress, 4);
            Assert.True(action.IsComplete);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void MultiActor_InterleavedComplexQueues_AllActorsExecuteDeterministically()
        {
            var resolver = new TurnResolver();
            var a1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var a2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

            // Actor 1: 0.2 + 0.3 + 0.5 = 1.0 TU
            var a1_act1 = new GenericTacticalAction(a1, 0.2f);
            var a1_act2 = new GenericTacticalAction(a1, 0.3f);
            var a1_act3 = new GenericTacticalAction(a1, 0.5f);

            // Actor 2: 0.4 + 0.6 = 1.0 TU
            var a2_act1 = new GenericTacticalAction(a2, 0.4f);
            var a2_act2 = new GenericTacticalAction(a2, 0.6f);

            resolver.ScheduleAction(a1_act1);
            resolver.ScheduleAction(a1_act2);
            resolver.ScheduleAction(a1_act3);

            resolver.ScheduleAction(a2_act1);
            resolver.ScheduleAction(a2_act2);

            // Tick 0.35 TU
            resolver.Tick(0.35f);

            // Actor 1: act1 completed (0.2s), act2 executing with 0.15 TU progress
            Assert.Equal(TacticalActionState.Completed, a1_act1.State);
            Assert.Equal(TacticalActionState.Executing, a1_act2.State);
            Assert.Equal(0.15f, a1_act2.ExecutionProgress, 4);
            Assert.Equal(0.15f, a1_act2.RemainingTU, 4);

            // Actor 2: act1 executing with 0.35 TU progress
            Assert.Equal(TacticalActionState.Executing, a2_act1.State);
            Assert.Equal(0.35f, a2_act1.ExecutionProgress, 4);
            Assert.Equal(0.05f, a2_act1.RemainingTU, 4);

            // Tick 0.35 TU (total 0.70s)
            resolver.Tick(0.35f);

            // Actor 1: act2 completed (consumed remaining 0.15s), act3 executing with 0.20s progress
            Assert.Equal(TacticalActionState.Completed, a1_act2.State);
            Assert.Equal(TacticalActionState.Executing, a1_act3.State);
            Assert.Equal(0.20f, a1_act3.ExecutionProgress, 4);

            // Actor 2: act1 completed (consumed remaining 0.05s), act2 executing with 0.30s progress
            Assert.Equal(TacticalActionState.Completed, a2_act1.State);
            Assert.Equal(TacticalActionState.Executing, a2_act2.State);
            Assert.Equal(0.30f, a2_act2.ExecutionProgress, 4);

            // Tick 0.30 TU (total 1.00s)
            resolver.Tick(0.30f);

            // Both actors complete all actions
            Assert.Equal(TacticalActionState.Completed, a1_act3.State);
            Assert.Equal(TacticalActionState.Completed, a2_act2.State);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void CarryoverFaultIsolation_SecondQueuedActionThrows_FirstCompletedSecondFailed()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var ex = new InvalidOperationException("Queued action fault");

            var act1 = new GenericTacticalAction(actor, 0.2f);
            var act2 = new GenericTacticalAction(actor, 0.5f, onExecute: _ => throw ex);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);

            var failedList = new List<ActionFailedEventArgs>();
            resolver.ActionFailed += (_, e) => failedList.Add(e);

            // Tick 0.5 TU: act1 completes at 0.2s, act2 starts at 0.2s and fails immediately
            resolver.Tick(0.5f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Failed, act2.State);
            Assert.Same(ex, act2.FailureException);
            Assert.Single(failedList);
            Assert.Same(act2, failedList[0].Action);
            Assert.Equal(0.2f, failedList[0].GlobalTime, 4);
            Assert.Equal(0.5f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void CancelAction_InMiddleOfFiveQueuedActions_PreservesOrderOfRemaining()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actor, 1.0f);
            var a2 = new GenericTacticalAction(actor, 1.0f);
            var a3 = new GenericTacticalAction(actor, 1.0f);
            var a4 = new GenericTacticalAction(actor, 1.0f);
            var a5 = new GenericTacticalAction(actor, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);
            resolver.ScheduleAction(a4);
            resolver.ScheduleAction(a5);

            // Cancel middle queued action (a3)
            bool cancelled = resolver.CancelAction(a3.Id);
            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, a3.State);

            var queued = resolver.GetQueuedActions(actor);
            Assert.Equal(3, queued.Count);
            Assert.Same(a2, queued[0]);
            Assert.Same(a4, queued[1]);
            Assert.Same(a5, queued[2]);
        }

        [Fact]
        public void GetActiveActions_ReturnsDeterministicSortedList()
        {
            var resolver = new TurnResolver();
            var a3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
            var a1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var a2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

            resolver.ScheduleAction(new GenericTacticalAction(a3, 1.0f));
            resolver.ScheduleAction(new GenericTacticalAction(a1, 1.0f));
            resolver.ScheduleAction(new GenericTacticalAction(a2, 1.0f));

            var active = resolver.GetActiveActions();
            Assert.Equal(3, active.Count);
            Assert.Equal(a1, active[0].ActorId);
            Assert.Equal(a2, active[1].ActorId);
            Assert.Equal(a3, active[2].ActorId);
        }

        [Fact]
        public void ScheduleAction_AfterPriorActionsComplete_CorrectStartTimeAndTimeline()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();

            var act1 = new GenericTacticalAction(actor, 1.0f);
            resolver.ScheduleAction(act1);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);

            // Now at T=1.0, schedule a new action for the same actor
            var act2 = new GenericTacticalAction(actor, 0.5f);
            resolver.ScheduleAction(act2);

            Assert.Equal(TacticalActionState.Pending, act2.State);
            Assert.Same(act2, resolver.GetCurrentAction(actor));

            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, act2.State);
            Assert.Equal(1.0f, act2.StartTime, 4);
            Assert.Equal(1.5f, act2.CompletionTime!.Value, 4);
            Assert.Equal(1.5f, resolver.GlobalTime, 4);
        }
    }
}
