using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
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
    /// Adversarial Empirical Challenger verification suite for Fractionated TU Turn Resolver,
    /// physiological coupling, multi-actor concurrency, and sub-tick precision.
    /// Authored by challenger_1 to independently probe for bugs, race conditions, drift, and edge failures.
    /// </summary>
    public class TurnResolverEmpiricalChallengerTests
    {
        private static (TacticalEntity entity, TacticalActorPhysiology physiology, BodyPart root) CreateEntity(
            float arterialBleed = 0f,
            float venousBleed = 0f,
            BodyPartType bodyPartType = BodyPartType.Thorax)
        {
            var physiology = new TacticalActorPhysiology();
            var root = new BodyPart
            {
                Type = bodyPartType,
                ArterialBleedRate = arterialBleed,
                VenousBleedRate = venousBleed
            };
            physiology.SetRoot(root);
            var entity = new TacticalEntity(Vector3.Zero, physiology);
            return (entity, physiology, root);
        }

        #region 1. Massive Multi-Actor Random Concurrency Oracle (5000 Actions)

        [Fact]
        public void MassiveConcurrency_50Actors_100ActionsEach_RandomTicks_PreservesOrderAndExactTotalTUs()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var rng = new Random(424242);
            const int actorCount = 50;
            const int actionsPerActor = 100;

            var actors = Enumerable.Range(0, actorCount).Select(_ => Guid.NewGuid()).ToList();
            var actionsPerActorMap = new Dictionary<Guid, List<GenericTacticalAction>>();
            var expectedTotalTUs = new Dictionary<Guid, float>();

            foreach (var actor in actors)
            {
                actionsPerActorMap[actor] = new List<GenericTacticalAction>();
                float totalTu = 0f;

                for (int i = 0; i < actionsPerActor; i++)
                {
                    float cost = (float)(rng.NextDouble() * 0.45 + 0.05); // 0.05 to 0.50 TU
                    totalTu += cost;
                    var act = new GenericTacticalAction(actor, cost);
                    actionsPerActorMap[actor].Add(act);
                    resolver.ScheduleAction(act);
                }

                expectedTotalTUs[actor] = totalTu;
            }

            var completedOrder = new Dictionary<Guid, List<TacticalAction>>();
            foreach (var actor in actors)
            {
                completedOrder[actor] = new List<TacticalAction>();
            }

            resolver.ActionCompleted += (_, e) =>
            {
                completedOrder[e.Action.ActorId].Add(e.Action);
            };

            int tickCount = 0;
            const int maxTicks = 10000;

            while (resolver.HasActiveActions && tickCount < maxTicks)
            {
                float dt = (float)(rng.NextDouble() * 0.75 + 0.02); // 0.02 to 0.77 TU
                resolver.Tick(dt);
                tickCount++;
            }

            Assert.False(resolver.HasActiveActions, "All actions should complete.");
            Assert.Equal(0, resolver.ActiveActorCount);

            foreach (var actor in actors)
            {
                var scheduledList = actionsPerActorMap[actor];
                var completedList = completedOrder[actor];

                Assert.Equal(actionsPerActor, completedList.Count);

                // Check FIFO ordering
                for (int i = 0; i < actionsPerActor; i++)
                {
                    Assert.Same(scheduledList[i], completedList[i]);
                    Assert.Equal(TacticalActionState.Completed, completedList[i].State);
                    Assert.True(completedList[i].IsComplete);
                    Assert.Equal(scheduledList[i].TUCost, completedList[i].ExecutionProgress, 3);
                }
            }
        }

        #endregion

        #region 2. Fractional TU Sub-Tick Carryover Mathematical Precision & Zero-Drift

        [Fact]
        public void SubTickCarryover_ChainedDifficultFractions_CalculatesTimestampsWithoutDrift()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            // 10 actions with fractional costs: 1/3, 1/6, 1/9, 1/12, 1/15, 1/18, 1/21, 1/24, 1/27, 1/30
            var fractions = Enumerable.Range(1, 10).Select(i => 1.0f / (3.0f * i)).ToList();
            float expectedSum = fractions.Sum();

            var actions = fractions.Select(f => new GenericTacticalAction(actorId, f)).ToList();
            foreach (var a in actions)
            {
                resolver.ScheduleAction(a);
            }

            var startTimes = new List<float>();
            var completionTimes = new List<float>();

            resolver.ActionStarted += (_, e) => startTimes.Add(e.GlobalTime);
            resolver.ActionCompleted += (_, e) => completionTimes.Add(e.GlobalTime);

            // Tick in a single large step
            resolver.Tick(expectedSum + 0.5f);

            Assert.Equal(10, startTimes.Count);
            Assert.Equal(10, completionTimes.Count);

            float accumulated = 0f;
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(accumulated, startTimes[i], 3);
                accumulated += fractions[i];
                Assert.Equal(accumulated, completionTimes[i], 3);
                Assert.Equal(TacticalActionState.Completed, actions[i].State);
            }

            Assert.False(resolver.HasActiveActions);
            Assert.Equal(expectedSum + 0.5f, resolver.GlobalTime, 3);
        }

        #endregion

        #region 3. Multi-Actor Differential Hemorrhage and Dynamic Mid-Turn Interventions

        [Fact]
        public void PhysiologicalIntegration_DynamicTourniquetIntervention_HaltsBleedAndAccumulatesIschemia()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);

            // Entity 1: Arm bleed (40 ml/s)
            var (entity1, phys1, arm1) = CreateEntity(arterialBleed: 30f, venousBleed: 10f, bodyPartType: BodyPartType.LeftArm);
            world.AddEntity(entity1);

            // Entity 2: Torso bleed (40 ml/s)
            var (entity2, phys2, torso2) = CreateEntity(arterialBleed: 30f, venousBleed: 10f, bodyPartType: BodyPartType.Thorax);
            world.AddEntity(entity2);

            // Schedule actions for both
            resolver.ScheduleAction(new GenericTacticalAction(entity1.Id, 100.0f));
            resolver.ScheduleAction(new GenericTacticalAction(entity2.Id, 100.0f));

            // Tick 10s -> both bleed 400 ml (5000 -> 4600 ml)
            resolver.Tick(10.0f);
            Assert.Equal(4600f, phys1.TotalBloodVolume, 2);
            Assert.Equal(4600f, phys2.TotalBloodVolume, 2);

            // Dynamic Intervention: Apply tourniquet to arm of entity 1
            arm1.HasTourniquet = true;

            // Tick 50s: Entity 1 bleed is halted (remains 4600ml), ischemia advances 50s. Entity 2 continues bleeding (40 * 50 = 2000 ml lost -> 2600 ml)
            resolver.Tick(50.0f);
            Assert.Equal(4600f, phys1.TotalBloodVolume, 2);
            Assert.Equal(50.0f, arm1.IschemiaDuration, 2);
            Assert.False(arm1.IsNecrotic);

            Assert.Equal(2600f, phys2.TotalBloodVolume, 2);
            Assert.Equal(HemorrhageClass.Class4, phys2.CurrentHemorrhageClass);

            // Advance time to surpass 7200s necrosis threshold on arm 1
            resolver.Tick(7200.0f);
            Assert.Equal(7250.0f, arm1.IschemiaDuration, 2);
            Assert.True(arm1.IsNecrotic, "Limb under tourniquet for > 7200s must become necrotic.");

            // Entity 2 bled out completely and lost consciousness -> actions cancelled
            Assert.Equal(0f, phys2.ConsciousnessLevel);
            Assert.Null(resolver.GetCurrentAction(entity2.Id));
        }

        #endregion

        #region 4. Reentrancy and Event Handler Mutation Safety

        [Fact]
        public void Reentrancy_SchedulingNewActionInsideActionCompleted_ExecutesSmoothly()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var chainCount = 0;
            const int maxChains = 5;

            void OnActionCompleted(object? sender, ActionEventArgs e)
            {
                if (chainCount < maxChains)
                {
                    chainCount++;
                    var nextAction = new GenericTacticalAction(actorId, 1.0f);
                    ((ITurnResolver)sender!).ScheduleAction(nextAction);
                }
            }

            resolver.ActionCompleted += OnActionCompleted;

            var initialAction = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(initialAction);

            // Tick 10.0 TU: initial + 5 chained actions = 6 actions total (6.0 TU)
            resolver.Tick(10.0f);

            Assert.Equal(maxChains, chainCount);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(10.0f, resolver.GlobalTime, 4);
        }

        [Fact]
        public void Reentrancy_UnregisteringEntityInsideTimeAdvanced_DoesNotCorruptState()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var (entity, _, _) = CreateEntity(arterialBleed: 10f);
            world.AddEntity(entity);

            resolver.TimeAdvanced += (sender, e) =>
            {
                if (e.CurrentGlobalTime >= 2.0f)
                {
                    world.RemoveEntity(entity.Id);
                }
            };

            resolver.Tick(1.0f);
            Assert.Single(world.GetEntities());

            resolver.Tick(1.5f); // Total = 2.5f -> unregisters entity
            Assert.Empty(world.GetEntities());

            // Next tick runs with 0 entities safely
            resolver.Tick(1.0f);
            Assert.Equal(3.5f, resolver.GlobalTime, 4);
        }

        #endregion

        #region 5. Extreme Time Deltas & Sub-Epsilon Micro-Steps

        [Fact]
        public void ExtremeTimeSteps_ZeroCostActionValidation_RejectsInvalidValues()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actor = Guid.NewGuid();

            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(new GenericTacticalAction(actor, 0f)));
            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(new GenericTacticalAction(actor, -1f)));
            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(new GenericTacticalAction(actor, float.NaN)));
            Assert.Throws<ArgumentException>(() => resolver.ScheduleAction(new GenericTacticalAction(actor, float.PositiveInfinity)));
        }

        [Fact]
        public void MicroStepping_OneMillionOneMicroStepTicks_DeterminismAndNoOverflow()
        {
            var world = new TacticalWorld(WorldBounds.CreateDefault());
            var resolver = new TurnResolver(world);
            var actorId = Guid.NewGuid();

            var action = new GenericTacticalAction(actorId, 1.0f);
            resolver.ScheduleAction(action);

            // Advance 10,000 steps of 0.0001f
            for (int i = 0; i < 10000; i++)
            {
                resolver.Tick(0.0001f);
            }

            Assert.Equal(1.0f, resolver.GlobalTime, 2);
            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.False(resolver.HasActiveActions);
        }

        #endregion

        #region 6. DI Container Parallel Thread-Safety & Multi-Instance Isolation

        [Fact]
        public void DependencyInjection_MultipleInstances_StrictlyIsolatedState()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var resolverA = provider.GetRequiredService<ITurnResolver>();
            var resolverB = provider.GetRequiredService<ITurnResolver>();
            var world = provider.GetRequiredService<ITacticalWorld>();

            Assert.NotSame(resolverA, resolverB);

            var (entityA, _, _) = CreateEntity();
            var (entityB, _, _) = CreateEntity();

            world.AddEntity(entityA);
            world.AddEntity(entityB);

            Assert.Equal(2, world.GetEntities().Count);
            Assert.Same(entityA, world.GetEntity(entityA.Id));
            Assert.Same(entityB, world.GetEntity(entityB.Id));

            resolverA.Tick(5.0f);
            Assert.Equal(5.0f, resolverA.GlobalTime, 4);
            Assert.Equal(0.0f, resolverB.GlobalTime, 4);
        }

        #endregion
    }
}
