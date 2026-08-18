using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    /// <summary>
    /// Adversarial stress tests and boundary verification for Fractionated TU Turn Resolver (Milestone 1).
    /// Covers extreme carryover, multi-actor scaling, rapid cancellation, event tracing, exception isolation, and boundary conditions.
    /// </summary>
    public class TurnResolverStressTests
    {
        #region 1. Extreme Sub-Tick Fractionated Carryover

        [Fact]
        public void ExtremeCarryover_TenConsecutiveMicroActions_CompleteInSingleTick()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            const int actionCount = 10;
            const float microCost = 0.05f; // 10 * 0.05 = 0.50 TU total
            const float tickDt = 1.0f;     // 1.0 TU tick -> all 10 complete, 0.50 TU excess

            var actions = new List<GenericTacticalAction>();
            var executionDeltas = new List<float>();

            for (int i = 0; i < actionCount; i++)
            {
                var act = new GenericTacticalAction(
                    actorId,
                    microCost,
                    onExecute: dt => executionDeltas.Add(dt));
                actions.Add(act);
                resolver.ScheduleAction(act);
            }

            var completedOrder = new List<TacticalAction>();
            resolver.ActionCompleted += (_, e) => completedOrder.Add(e.Action);

            resolver.Tick(tickDt);

            Assert.Equal(actionCount, completedOrder.Count);
            Assert.Equal(actionCount, executionDeltas.Count);

            for (int i = 0; i < actionCount; i++)
            {
                Assert.Same(actions[i], completedOrder[i]);
                Assert.Equal(TacticalActionState.Completed, actions[i].State);
                Assert.Equal(microCost, actions[i].ExecutionProgress, 4);
                Assert.True(actions[i].IsComplete);

                float expectedCompletionTime = (i + 1) * microCost;
                Assert.Equal(expectedCompletionTime, actions[i].CompletionTime!.Value, 4);
                Assert.Equal(microCost, executionDeltas[i], 4);
            }

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Equal(tickDt, resolver.GlobalTime, 4);
        }

        [Fact]
        public void ExtremeCarryover_OneHundredChainedMicroActions_CompleteAcrossMultipleTicks()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();
            const int totalActions = 100;
            const float actionCost = 0.01f; // 100 * 0.01 = 1.00 TU total

            var actions = Enumerable.Range(0, totalActions)
                .Select(_ => new GenericTacticalAction(actorId, actionCost))
                .ToList();

            foreach (var act in actions)
            {
                resolver.ScheduleAction(act);
            }

            int completedCount = 0;
            resolver.ActionCompleted += (_, _) => completedCount++;

            // Tick in 4 chunks of 0.25 TU (each tick completes 25 actions)
            for (int tick = 1; tick <= 4; tick++)
            {
                resolver.Tick(0.25f);
                Assert.Equal(tick * 25, completedCount);
                Assert.Equal(tick * 0.25f, resolver.GlobalTime, 4);
            }

            Assert.Equal(100, completedCount);
            Assert.All(actions, a => Assert.Equal(TacticalActionState.Completed, a.State));
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void ExtremeCarryover_HeterogeneousPrimeFractions_AccumulatesAndResolvesAccurately()
        {
            var resolver = new TurnResolver();
            var actorId = Guid.NewGuid();

            // Actions with prime fractional costs: 1/7, 2/7, 4/7 -> sum = 1.0f
            float c1 = 1f / 7f;
            float c2 = 2f / 7f;
            float c3 = 4f / 7f;

            var a1 = new GenericTacticalAction(actorId, c1);
            var a2 = new GenericTacticalAction(actorId, c2);
            var a3 = new GenericTacticalAction(actorId, c3);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            // Tick 0.5 TU
            resolver.Tick(0.5f);

            // a1 (0.142857) completed, a2 (0.285714) completed (total 0.428571), a3 (0.571428) executed for (0.5 - 0.428571) = 0.071428 TU
            Assert.Equal(TacticalActionState.Completed, a1.State);
            Assert.Equal(TacticalActionState.Completed, a2.State);
            Assert.Equal(TacticalActionState.Executing, a3.State);
            Assert.Equal(0.5f - (c1 + c2), a3.ExecutionProgress, 4);

            // Tick remaining 0.5 TU
            resolver.Tick(0.5f);

            Assert.Equal(TacticalActionState.Completed, a3.State);
            Assert.Equal(c3, a3.ExecutionProgress, 4);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.False(resolver.HasActiveActions);
        }

        #endregion

        #region 2. Concurrent Multi-Actor Interleaving and Scaling

        [Fact]
        public void ConcurrentMultiActor_FiftyActorsWithDifferentQueues_ExecuteDeterministically()
        {
            var resolver = new TurnResolver();
            const int actorCount = 50;
            var actors = Enumerable.Range(1, actorCount)
                .Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}"))
                .ToList();

            var completedPerActor = new Dictionary<Guid, int>();
            foreach (var actor in actors)
            {
                completedPerActor[actor] = 0;
                float cost = MathF.Max(0.01f, (actors.IndexOf(actor) + 1) * 0.02f);
                for (int j = 0; j < 3; j++)
                {
                    resolver.ScheduleAction(new GenericTacticalAction(actor, cost));
                }
            }

            resolver.ActionCompleted += (_, e) => completedPerActor[e.Action.ActorId]++;

            const float dt = 0.05f;
            int maxTicks = 1000;
            int ticks = 0;
            while (resolver.HasActiveActions && ticks < maxTicks)
            {
                resolver.Tick(dt);
                ticks++;
            }

            Assert.False(resolver.HasActiveActions);
            Assert.All(actors, actor => Assert.Equal(3, completedPerActor[actor]));
        }

        [Fact]
        public void ConcurrentMultiActor_DynamicMidSimulationScheduling_InterleavesSmoothly()
        {
            var resolver = new TurnResolver();
            var a1 = Guid.NewGuid();
            var a2 = Guid.NewGuid();

            // Actor 1 schedules a 2.0 TU action
            resolver.ScheduleAction(new GenericTacticalAction(a1, 2.0f));

            // Tick 0.5 TU
            resolver.Tick(0.5f);
            Assert.Equal(0.5f, resolver.GlobalTime, 4);

            // Dynamically schedule action for Actor 2 at t=0.5
            var a2_act = new GenericTacticalAction(a2, 1.0f);
            resolver.ScheduleAction(a2_act);

            // Tick 0.5 TU -> Actor 1 progresses to 1.0 TU, Actor 2 progresses to 0.5 TU
            resolver.Tick(0.5f);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.Equal(1.0f, resolver.GetCurrentAction(a1)!.ExecutionProgress, 4);
            Assert.Equal(0.5f, resolver.GetCurrentAction(a2)!.ExecutionProgress, 4);
            Assert.Equal(0.5f, a2_act.StartTime, 4);

            // Tick 0.5 TU -> Actor 2 completes at t=1.5, Actor 1 at 1.5 TU
            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Completed, a2_act.State);
            Assert.Equal(1.5f, a2_act.CompletionTime!.Value, 4);
            Assert.Equal(TacticalActionState.Executing, resolver.GetCurrentAction(a1)!.State);

            // Tick 0.5 TU -> Actor 1 completes at t=2.0
            resolver.Tick(0.5f);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(2.0f, resolver.GlobalTime, 4);
        }

        #endregion

        #region 3. Rapid and Adversarial Cancellation

        [Fact]
        public void Cancellation_CancelCurrentlyExecutingAction_PromotesNextAndExecutesOnNextTick()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();

            var a1 = new GenericTacticalAction(actor, 1.0f);
            var a2 = new GenericTacticalAction(actor, 1.0f);
            var a3 = new GenericTacticalAction(actor, 1.0f);

            resolver.ScheduleAction(a1);
            resolver.ScheduleAction(a2);
            resolver.ScheduleAction(a3);

            // Partially execute a1
            resolver.Tick(0.4f);
            Assert.Equal(0.4f, a1.ExecutionProgress, 4);

            // Cancel a1 while executing
            bool cancelled = resolver.CancelAction(a1.Id);
            Assert.True(cancelled);
            Assert.Equal(TacticalActionState.Cancelled, a1.State);

            // a2 should immediately be promoted to current
            Assert.Same(a2, resolver.GetCurrentAction(actor));
            Assert.Equal(TacticalActionState.Pending, a2.State);
            Assert.Single(resolver.GetQueuedActions(actor));
            Assert.Same(a3, resolver.GetQueuedActions(actor)[0]);

            // Next tick executes a2
            resolver.Tick(0.5f);
            Assert.Equal(TacticalActionState.Executing, a2.State);
            Assert.Equal(0.4f, a2.StartTime, 4);
            Assert.Equal(0.5f, a2.ExecutionProgress, 4);
        }

        [Fact]
        public void Cancellation_CancelAllActorActionsWhileExecuting_CleansUpCompletely()
        {
            var resolver = new TurnResolver();
            var targetActor = Guid.NewGuid();
            var bystanderActor = Guid.NewGuid();

            for (int i = 0; i < 5; i++)
            {
                resolver.ScheduleAction(new GenericTacticalAction(targetActor, 1.0f));
            }
            resolver.ScheduleAction(new GenericTacticalAction(bystanderActor, 2.0f));

            resolver.Tick(0.3f);
            Assert.Equal(2, resolver.ActiveActorCount);

            int cancelledCount = resolver.CancelActorActions(targetActor);
            Assert.Equal(5, cancelledCount);
            Assert.Null(resolver.GetCurrentAction(targetActor));
            Assert.Empty(resolver.GetQueuedActions(targetActor));
            Assert.Equal(1, resolver.ActiveActorCount);

            // Bystander continues without interference
            resolver.Tick(1.7f);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(2.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void Cancellation_IdempotencyAndInvalidGuids()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var act = new GenericTacticalAction(actor, 1.0f);
            resolver.ScheduleAction(act);

            Assert.False(resolver.CancelAction(Guid.Empty));
            Assert.False(resolver.CancelAction(Guid.NewGuid()));

            // Cancel valid action
            Assert.True(resolver.CancelAction(act.Id));

            // Cancelling again returns false
            Assert.False(resolver.CancelAction(act.Id));
            Assert.Equal(0, resolver.CancelActorActions(actor));
            Assert.Equal(0, resolver.CancelActorActions(Guid.Empty));
        }

        [Fact]
        public void Cancellation_SelectivelyCancelOddIndexedQueuedActions()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var actions = Enumerable.Range(0, 6)
                .Select(_ => new GenericTacticalAction(actor, 0.5f))
                .ToList();

            foreach (var a in actions)
            {
                resolver.ScheduleAction(a);
            }

            // Cancel actions at index 1, 3, 5
            Assert.True(resolver.CancelAction(actions[1].Id));
            Assert.True(resolver.CancelAction(actions[3].Id));
            Assert.True(resolver.CancelAction(actions[5].Id));

            var queued = resolver.GetQueuedActions(actor);
            Assert.Equal(2, queued.Count);
            Assert.Same(actions[2], queued[0]);
            Assert.Same(actions[4], queued[1]);

            var completed = new List<TacticalAction>();
            resolver.ActionCompleted += (_, e) => completed.Add(e.Action);

            resolver.Tick(2.0f);

            Assert.Equal(3, completed.Count);
            Assert.Same(actions[0], completed[0]);
            Assert.Same(actions[2], completed[1]);
            Assert.Same(actions[4], completed[2]);
            Assert.False(resolver.HasActiveActions);
        }

        #endregion

        #region 4. Event Ordering and Parameter Verification Under Stress

        [Fact]
        public void EventOrdering_StrictLifecycleTrace_MatchesExpectedSequence()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var trace = new List<string>();

            var act1 = new GenericTacticalAction(actor, 0.4f);
            var act2 = new GenericTacticalAction(actor, 0.4f);

            resolver.ActionScheduled += (_, e) => trace.Add($"Scheduled:{e.Action.Id}");
            resolver.ActionStarted += (_, e) => trace.Add($"Started:{e.Action.Id}@{e.GlobalTime:F2}");
            resolver.ActionProgressed += (_, e) => trace.Add($"Progressed:{e.Action.Id}:{e.DeltaTime:F2}:{e.CurrentProgress:F2}@{e.GlobalTime:F2}");
            resolver.ActionCompleted += (_, e) => trace.Add($"Completed:{e.Action.Id}@{e.GlobalTime:F2}");
            resolver.TimeAdvanced += (_, e) => trace.Add($"TimeAdvanced:{e.DeltaTime:F2}@{e.CurrentGlobalTime:F2}");

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);

            resolver.Tick(0.6f);

            var expected = new List<string>
            {
                $"Scheduled:{act1.Id}",
                $"Scheduled:{act2.Id}",
                $"Started:{act1.Id}@0.00",
                $"Progressed:{act1.Id}:0.40:0.40@0.40",
                $"Completed:{act1.Id}@0.40",
                $"Started:{act2.Id}@0.40",
                $"Progressed:{act2.Id}:0.20:0.20@0.60",
                "TimeAdvanced:0.60@0.60"
            };

            Assert.Equal(expected, trace);
        }

        #endregion

        #region 5. Exception Isolation Under Adversarial Conditions

        [Fact]
        public void ExceptionIsolation_ActionFailsInMiddleOfSubTickCarryover_QueueRemainsIntact()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var ex = new InvalidOperationException("Sub-tick explosion");

            var act1 = new GenericTacticalAction(actor, 0.2f);
            var act2 = new GenericTacticalAction(actor, 0.3f, onExecute: _ => throw ex);
            var act3 = new GenericTacticalAction(actor, 0.4f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);
            resolver.ScheduleAction(act3);

            var events = new List<string>();
            resolver.ActionCompleted += (_, e) => events.Add($"Completed:{e.Action.Id}");
            resolver.ActionFailed += (_, e) => events.Add($"Failed:{e.Action.Id}");

            resolver.Tick(0.5f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Failed, act2.State);
            Assert.Equal(2, events.Count);
            Assert.Equal($"Completed:{act1.Id}", events[0]);
            Assert.Equal($"Failed:{act2.Id}", events[1]);

            Assert.Same(act3, resolver.GetCurrentAction(actor));
            Assert.Equal(TacticalActionState.Pending, act3.State);

            resolver.Tick(0.4f);
            Assert.Equal(TacticalActionState.Completed, act3.State);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void ExceptionIsolation_AllActorsThrowConcurrently_ResolverStateRemainsStable()
        {
            var resolver = new TurnResolver();
            var actors = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

            foreach (var a in actors)
            {
                resolver.ScheduleAction(new GenericTacticalAction(a, 1.0f, onExecute: _ => throw new ApplicationException("Simulated crash")));
            }

            int failCount = 0;
            resolver.ActionFailed += (_, _) => failCount++;

            resolver.Tick(0.5f);

            Assert.Equal(5, failCount);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Equal(0.5f, resolver.GlobalTime, 4);
        }

        #endregion

        #region 6. Boundary Values and Precision Tolerances

        [Fact]
        public void Boundary_DeltaTimeEqualToEpsilon_ProcessesWithoutInfiniteLoop()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var act = new GenericTacticalAction(actor, 1.0f);
            resolver.ScheduleAction(act);

            resolver.Tick(1e-4f);
            Assert.Equal(1e-4f, resolver.GlobalTime, 6);
            Assert.Equal(1e-4f, act.ExecutionProgress, 6);

            resolver.Tick(1e-5f);
            Assert.Equal(1.1e-4f, resolver.GlobalTime, 6);
        }

        [Fact]
        public void Boundary_LargeDeltaTime_CompletesAllActionsImmediately()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var act1 = new GenericTacticalAction(actor, 2.0f);
            var act2 = new GenericTacticalAction(actor, 3.0f);

            resolver.ScheduleAction(act1);
            resolver.ScheduleAction(act2);

            resolver.Tick(1000.0f);

            Assert.Equal(TacticalActionState.Completed, act1.State);
            Assert.Equal(TacticalActionState.Completed, act2.State);
            Assert.Equal(2.0f, act1.CompletionTime!.Value, 4);
            Assert.Equal(5.0f, act2.CompletionTime!.Value, 4);
            Assert.Equal(1000.0f, resolver.GlobalTime, 4);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void Boundary_MicroActionCost_CompletesAndClampsProperly()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();
            var act = new GenericTacticalAction(actor, 1e-4f);
            resolver.ScheduleAction(act);

            resolver.Tick(0.01f);

            Assert.Equal(TacticalActionState.Completed, act.State);
            Assert.Equal(1e-4f, act.ExecutionProgress, 6);
            Assert.Equal(0f, act.RemainingTU, 6);
            Assert.Equal(1.0f, act.NormalizedProgress, 4);
        }

        [Fact]
        public void Boundary_MultipleResetCalls_ReinitializesCompletely()
        {
            var resolver = new TurnResolver();
            var actor = Guid.NewGuid();

            for (int run = 0; run < 3; run++)
            {
                resolver.ScheduleAction(new GenericTacticalAction(actor, 1.0f));
                resolver.Tick(0.5f);
                Assert.Equal(0.5f, resolver.GlobalTime, 4);
                Assert.True(resolver.HasActiveActions);

                resolver.Reset();
                Assert.Equal(0.0f, resolver.GlobalTime);
                Assert.False(resolver.HasActiveActions);
                Assert.Equal(0, resolver.ActiveActorCount);
                Assert.Null(resolver.GetCurrentAction(actor));
            }
        }

        #endregion
    }
}
