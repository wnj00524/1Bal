using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    public class TurnResolverPhysiologyTests
    {
        private static (TacticalEntity entity, TacticalActorPhysiology physiology, BodyPart root) CreateTestEntity(
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

        private static WeaponProfile CreateTestWeapon(float muzzleVelocity = 800f, float tuCost = 10f)
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
                Name = "Tactical Rifle",
                LoadedAmmunition = ammo,
                BaseTUCostToFire = tuCost
            };
        }

        #region Entity Management & Registration Tests

        [Fact]
        public void RegisterEntity_ValidEntity_AddsEntityAndFiresEntityRegisteredEvent()
        {
            var resolver = new TurnResolver();
            var (entity, _, _) = CreateTestEntity();

            EntityEventArgs? receivedEvent = null;
            resolver.EntityRegistered += (_, e) => receivedEvent = e;

            resolver.RegisterEntity(entity);

            var registered = resolver.GetRegisteredEntities();
            Assert.Single(registered);
            Assert.Same(entity, registered.First());
            Assert.Same(entity, resolver.GetEntity(entity.Id));

            Assert.NotNull(receivedEvent);
            Assert.Same(entity, receivedEvent.Entity);
            Assert.Equal(0f, receivedEvent.Timestamp);
        }

        [Fact]
        public void RegisterEntity_NullEntity_ThrowsArgumentNullException()
        {
            var resolver = new TurnResolver();
            Assert.Throws<ArgumentNullException>(() => resolver.RegisterEntity(null!));
        }

        private class MockEntityWithEmptyId : IEntity
        {
            public Guid Id => Guid.Empty;
            public Vector3 Position { get; set; } = Vector3.Zero;
            public IActorPhysiology Physiology { get; set; } = new TacticalActorPhysiology();
            public WeaponProfile? EquippedWeapon { get; set; }
        }

        [Fact]
        public void RegisterEntity_EmptyEntityId_ThrowsArgumentException()
        {
            var resolver = new TurnResolver();
            var invalidEntity = new MockEntityWithEmptyId();

            Assert.Throws<ArgumentException>(() => resolver.RegisterEntity(invalidEntity));
        }

        [Fact]
        public void UnregisterEntity_ExistingEntity_RemovesAndFiresEntityUnregisteredEvent()
        {
            var resolver = new TurnResolver();
            var (entity, _, _) = CreateTestEntity();
            resolver.RegisterEntity(entity);

            EntityEventArgs? unregisterEvent = null;
            resolver.EntityUnregistered += (_, e) => unregisterEvent = e;

            bool removed = resolver.UnregisterEntity(entity.Id);

            Assert.True(removed);
            Assert.Empty(resolver.GetRegisteredEntities());
            Assert.Null(resolver.GetEntity(entity.Id));

            Assert.NotNull(unregisterEvent);
            Assert.Same(entity, unregisterEvent.Entity);
        }

        [Fact]
        public void UnregisterEntity_NonExistentOrEmptyGuid_ReturnsFalse()
        {
            var resolver = new TurnResolver();

            bool emptyResult = resolver.UnregisterEntity(Guid.Empty);
            Assert.False(emptyResult);

            bool nonExistentResult = resolver.UnregisterEntity(Guid.NewGuid());
            Assert.False(nonExistentResult);
        }

        [Fact]
        public void GetRegisteredEntities_ReturnsDeterministicOrderingSortedById()
        {
            var resolver = new TurnResolver();

            var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

            var phys1 = new TacticalActorPhysiology(); phys1.SetRoot(new BodyPart { Type = BodyPartType.Head });
            var phys2 = new TacticalActorPhysiology(); phys2.SetRoot(new BodyPart { Type = BodyPartType.Thorax });
            var phys3 = new TacticalActorPhysiology(); phys3.SetRoot(new BodyPart { Type = BodyPartType.Abdomen });

            var mock1 = new MockCustomIdEntity(id3, phys3);
            var mock2 = new MockCustomIdEntity(id1, phys1);
            var mock3 = new MockCustomIdEntity(id2, phys2);

            // Register in disordered order: 3, 1, 2
            resolver.RegisterEntity(mock1);
            resolver.RegisterEntity(mock2);
            resolver.RegisterEntity(mock3);

            var registered = resolver.GetRegisteredEntities().ToList();
            Assert.Equal(3, registered.Count);
            Assert.Equal(id1, registered[0].Id);
            Assert.Equal(id2, registered[1].Id);
            Assert.Equal(id3, registered[2].Id);
        }

        private class MockCustomIdEntity(Guid id, IActorPhysiology physiology) : IEntity
        {
            public Guid Id { get; } = id;
            public Vector3 Position { get; set; } = Vector3.Zero;
            public IActorPhysiology Physiology { get; } = physiology;
            public WeaponProfile? EquippedWeapon { get; set; }
        }

        [Fact]
        public void Reset_ClearsRegisteredEntitiesAlongWithActionQueuesAndGlobalTime()
        {
            var resolver = new TurnResolver();
            var (entity, _, _) = CreateTestEntity();
            resolver.RegisterEntity(entity);

            var action = new GenericTacticalAction(entity.Id, 2.0f);
            resolver.ScheduleAction(action);

            resolver.Tick(0.5f);
            Assert.Equal(0.5f, resolver.GlobalTime, 4);
            Assert.True(resolver.HasActiveActions);
            Assert.NotEmpty(resolver.GetRegisteredEntities());

            resolver.Reset();

            Assert.Equal(0.0f, resolver.GlobalTime);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
            Assert.Empty(resolver.GetRegisteredEntities());
            Assert.Null(resolver.GetEntity(entity.Id));
        }

        #endregion

        #region Physiological Ticking Integration Tests

        [Fact]
        public void Tick_RegisteredEntity_WithActiveBleed_ReducesBloodVolumeAccurately()
        {
            var resolver = new TurnResolver();
            // Total bleed rate = 15 (arterial) + 5 (venous) = 20 ml/s
            var (entity, physiology, _) = CreateTestEntity(arterialBleed: 15f, venousBleed: 5f);
            resolver.RegisterEntity(entity);

            Assert.Equal(5000f, physiology.TotalBloodVolume);

            // Step 0.5s -> 20 * 0.5 = 10 ml lost
            resolver.Tick(0.5f);
            Assert.Equal(4990f, physiology.TotalBloodVolume, 3);

            // Step 1.5s -> 20 * 1.5 = 30 ml lost (Total lost: 40 ml)
            resolver.Tick(1.5f);
            Assert.Equal(4960f, physiology.TotalBloodVolume, 3);
        }

        [Fact]
        public void Tick_RegisteredEntity_WithTourniquet_AdvancesIschemiaDuration()
        {
            var resolver = new TurnResolver();
            var (entity, physiology, arm) = CreateTestEntity(arterialBleed: 25f, bodyPartType: BodyPartType.LeftArm);
            arm.HasTourniquet = true;

            resolver.RegisterEntity(entity);

            // Bleed is completely halted by tourniquet on extremity
            Assert.Equal(0f, arm.GetActiveBleedRate());

            // Advance 100s
            resolver.Tick(100.0f);
            Assert.Equal(5000f, physiology.TotalBloodVolume);
            Assert.Equal(100.0f, arm.IschemiaDuration, 2);
            Assert.False(arm.IsNecrotic);

            // Advance another 7150s (Total = 7250s > 7200s necrosis threshold)
            resolver.Tick(7150.0f);
            Assert.Equal(7250.0f, arm.IschemiaDuration, 2);
            Assert.True(arm.IsNecrotic);
        }

        [Fact]
        public void Tick_UnregisteredEntity_PhysiologyNotTickedByResolver()
        {
            var resolver = new TurnResolver();
            var (entity1, phys1, _) = CreateTestEntity(arterialBleed: 20f);
            var (entity2, phys2, _) = CreateTestEntity(arterialBleed: 20f);

            // Only register entity1
            resolver.RegisterEntity(entity1);

            resolver.Tick(2.0f);

            // entity1 was ticked: 5000 - 40 = 4960 ml
            Assert.Equal(4960f, phys1.TotalBloodVolume, 3);

            // entity2 was NOT registered: untouched 5000 ml
            Assert.Equal(5000f, phys2.TotalBloodVolume, 3);
        }

        #endregion

        #region Incapacitation & Action Cancellation Tests

        [Fact]
        public void Tick_IncapacitatedEntity_ConsciousnessZero_AutomaticallyCancelsActiveAndQueuedActions()
        {
            var resolver = new TurnResolver();
            // Bleed rate of 3000 ml/s -> 1.0s drops blood volume from 5000ml to 2000ml (60% lost, Fatal, consciousness = 0)
            var (entity, physiology, _) = CreateTestEntity(arterialBleed: 3000f);
            resolver.RegisterEntity(entity);

            var activeAction = new GenericTacticalAction(entity.Id, 10.0f);
            var queuedAction1 = new GenericTacticalAction(entity.Id, 5.0f);
            var queuedAction2 = new GenericTacticalAction(entity.Id, 5.0f);

            resolver.ScheduleAction(activeAction);
            resolver.ScheduleAction(queuedAction1);
            resolver.ScheduleAction(queuedAction2);

            var cancelledEvents = new List<ActionEventArgs>();
            resolver.ActionCancelled += (_, e) => cancelledEvents.Add(e);

            // Tick 1.0s -> Entity bleeds heavily, consciousness drops to 0.0f
            resolver.Tick(1.0f);

            Assert.Equal(0.0f, physiology.ConsciousnessLevel);
            Assert.Equal(HemorrhageClass.Fatal, physiology.CurrentHemorrhageClass);

            // All actions must be cancelled
            Assert.Equal(TacticalActionState.Cancelled, activeAction.State);
            Assert.Equal(TacticalActionState.Cancelled, queuedAction1.State);
            Assert.Equal(TacticalActionState.Cancelled, queuedAction2.State);

            Assert.Equal(3, cancelledEvents.Count);
            Assert.False(resolver.HasActiveActions);
            Assert.Equal(0, resolver.ActiveActorCount);
        }

        [Fact]
        public void Tick_ConsciousEntity_ActionsExecuteAndCompleteNormally()
        {
            var resolver = new TurnResolver();
            // Healthy entity (0 bleed, consciousness = 1.0)
            var (entity, physiology, _) = CreateTestEntity();
            resolver.RegisterEntity(entity);

            var action1 = new GenericTacticalAction(entity.Id, 1.0f);
            var action2 = new GenericTacticalAction(entity.Id, 1.0f);

            resolver.ScheduleAction(action1);
            resolver.ScheduleAction(action2);

            resolver.Tick(1.0f);

            Assert.Equal(1.0f, physiology.ConsciousnessLevel);
            Assert.Equal(TacticalActionState.Completed, action1.State);
            Assert.Same(action2, resolver.GetCurrentAction(entity.Id));
            Assert.Equal(TacticalActionState.Pending, action2.State);

            resolver.Tick(1.0f);
            Assert.Equal(TacticalActionState.Completed, action2.State);
            Assert.Null(resolver.GetCurrentAction(entity.Id));
        }

        #endregion

        #region ShootTacticalAction Clean Progress & Ballistics Tests

        [Fact]
        public void ShootTacticalAction_ExecutionInTurnResolver_DoesNotDoubleIncrementProgress()
        {
            var resolver = new TurnResolver();
            var (shooter, _, _) = CreateTestEntity();
            shooter.EquippedWeapon = CreateTestWeapon(muzzleVelocity: 800f, tuCost: 10f);
            resolver.RegisterEntity(shooter);

            var environment = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));
            var shootAction = new ShootTacticalAction(shooter, Vector3.UnitX, environment);

            resolver.ScheduleAction(shootAction);

            // Step 4.0 TU
            resolver.Tick(4.0f);
            Assert.Equal(4.0f, shootAction.ExecutionProgress, 3);
            Assert.Equal(TacticalActionState.Executing, shootAction.State);
            Assert.Null(shootAction.FinalState);

            // Step 3.0 TU (Total = 7.0 TU)
            resolver.Tick(3.0f);
            Assert.Equal(7.0f, shootAction.ExecutionProgress, 3);
            Assert.Equal(TacticalActionState.Executing, shootAction.State);
            Assert.Null(shootAction.FinalState);

            // Step 3.0 TU (Total = 10.0 TU -> Action Completes!)
            resolver.Tick(3.0f);
            Assert.Equal(10.0f, shootAction.ExecutionProgress, 3);
            Assert.Equal(TacticalActionState.Completed, shootAction.State);
            Assert.NotNull(shootAction.FinalState);
            Assert.True(shootAction.FinalState.Value.Time > 0.9f);
        }

        [Fact]
        public void ShootTacticalAction_MissingAmmunition_FailsGracefullyInTurnResolver()
        {
            var resolver = new TurnResolver();
            var (shooter, _, _) = CreateTestEntity();
            shooter.EquippedWeapon = new WeaponProfile
            {
                Name = "Empty Rifle",
                LoadedAmmunition = null!,
                BaseTUCostToFire = 5f
            };
            resolver.RegisterEntity(shooter);

            var environment = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));
            var shootAction = new ShootTacticalAction(shooter, Vector3.UnitX, environment);

            resolver.ScheduleAction(shootAction);

            ActionFailedEventArgs? failedEvent = null;
            resolver.ActionFailed += (_, e) => failedEvent = e;

            // Tick 5.0 TU to reach firing time
            resolver.Tick(5.0f);

            Assert.Equal(TacticalActionState.Failed, shootAction.State);
            Assert.NotNull(shootAction.FailureException);
            Assert.IsType<InvalidOperationException>(shootAction.FailureException);

            Assert.NotNull(failedEvent);
            Assert.Same(shootAction, failedEvent.Action);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void ShootTacticalAction_ZeroDirection_HandledGracefully()
        {
            var (shooter, _, _) = CreateTestEntity();
            shooter.EquippedWeapon = CreateTestWeapon(muzzleVelocity: 800f, tuCost: 5f);
            var environment = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0));

            // Passing Vector3.Zero direction shouldn't produce NaN
            var shootAction = new ShootTacticalAction(shooter, Vector3.Zero, environment);
            shootAction.OnComplete();

            Assert.NotNull(shootAction.FinalState);
            Assert.False(float.IsNaN(shootAction.FinalState.Value.Velocity.X));
            Assert.False(float.IsNaN(shootAction.FinalState.Value.Velocity.Y));
            Assert.False(float.IsNaN(shootAction.FinalState.Value.Velocity.Z));
        }

        #endregion

        #region Multi-Entity Simultaneous Interleaving & DI Tests

        [Fact]
        public void TurnResolver_MultipleEntities_InterleavesPhysiologyAndActionsDeterministically()
        {
            var resolver = new TurnResolver();

            // Entity A: Moderate bleed (10 ml/s) + 2x 1.0 TU actions
            var (entityA, physA, _) = CreateTestEntity(arterialBleed: 10f);
            resolver.RegisterEntity(entityA);
            var actionA1 = new GenericTacticalAction(entityA.Id, 1.0f);
            var actionA2 = new GenericTacticalAction(entityA.Id, 1.0f);
            resolver.ScheduleAction(actionA1);
            resolver.ScheduleAction(actionA2);

            // Entity B: No bleed + 1x 1.5 TU action
            var (entityB, physB, _) = CreateTestEntity();
            resolver.RegisterEntity(entityB);
            var actionB1 = new GenericTacticalAction(entityB.Id, 1.5f);
            resolver.ScheduleAction(actionB1);

            // Tick 1.0 TU
            resolver.Tick(1.0f);

            // Entity A: actionA1 completed, actionA2 promoted; blood lost = 10 ml
            Assert.Equal(TacticalActionState.Completed, actionA1.State);
            Assert.Same(actionA2, resolver.GetCurrentAction(entityA.Id));
            Assert.Equal(TacticalActionState.Pending, actionA2.State);
            Assert.Equal(4990f, physA.TotalBloodVolume, 3);

            // Entity B: actionB1 at 1.0/1.5 TU; blood volume untouched
            Assert.Equal(TacticalActionState.Executing, actionB1.State);
            Assert.Equal(1.0f, actionB1.ExecutionProgress, 3);
            Assert.Equal(5000f, physB.TotalBloodVolume, 3);

            // Tick 1.0 TU (Total = 2.0 TU)
            resolver.Tick(1.0f);

            // Entity A: actionA2 completed; blood lost = 20 ml total
            Assert.Equal(TacticalActionState.Completed, actionA2.State);
            Assert.Equal(4980f, physA.TotalBloodVolume, 3);

            // Entity B: actionB1 completed at 1.5 TU; blood untouched
            Assert.Equal(TacticalActionState.Completed, actionB1.State);
            Assert.Equal(1.5f, actionB1.CompletionTime!.Value, 3);
            Assert.Equal(5000f, physB.TotalBloodVolume, 3);
        }

        [Fact]
        public void DependencyInjection_AddSimulationServices_ResolvesTurnResolver_WithEntitySupport()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var resolver = provider.GetRequiredService<ITurnResolver>();
            Assert.NotNull(resolver);
            Assert.IsType<TurnResolver>(resolver);

            var (entity, _, _) = CreateTestEntity(arterialBleed: 10f);
            resolver.RegisterEntity(entity);

            Assert.Single(resolver.GetRegisteredEntities());
            Assert.Same(entity, resolver.GetEntity(entity.Id));

            resolver.Tick(1.0f);
            Assert.Equal(1.0f, resolver.GlobalTime, 4);
            Assert.Equal(4990f, entity.Physiology.TotalBloodVolume, 3);
        }

        #endregion
    }
}
