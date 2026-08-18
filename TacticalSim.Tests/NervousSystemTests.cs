using System;
using System.Numerics;
using Xunit;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Tests
{
    public class NervousSystemTests
    {
        [Fact]
        public void DamageToBone_SpikesPainAndShock()
        {
            var physiology = new TacticalActorPhysiology();
            var bonePart = new BodyPart { Type = BodyPartType.LeftLeg };
            bonePart.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Bone, OrganType.None));
            physiology.SetRoot(bonePart);

            Assert.Equal(0f, physiology.PainLevel);
            Assert.Equal(0f, physiology.ShockLevel);

            bonePart.Voxels[0].ApplyKineticEnergy(500f, Vector3.Zero, 0.001f);
            physiology.TickPhysiology(10f);

            Assert.True(physiology.PainLevel > 0.8f);
            Assert.True(physiology.ShockLevel > 0.4f);
        }

        [Fact]
        public void AdministerAnalgesic_ReducesPain()
        {
            var physiology = new TacticalActorPhysiology();
            var musclePart = new BodyPart { Type = BodyPartType.LeftArm };
            musclePart.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.None));
            physiology.SetRoot(musclePart);

            musclePart.Voxels[0].ApplyKineticEnergy(200f, Vector3.Zero, 0.001f);
            physiology.TickPhysiology(10f);

            float initialPain = physiology.PainLevel;
            Assert.True(initialPain > 0f);

            physiology.AdministerAnalgesic(0.5f);
            physiology.TickPhysiology(10f);

            Assert.True(physiology.PainLevel < initialPain);
        }

        [Fact]
        public void HighPain_IncreasesShootTUCost()
        {
            var physiology = new TacticalActorPhysiology();
            var dummy = new TacticalEntity(Vector3.Zero, physiology);
            dummy.EquippedWeapon = new WeaponProfile
            {
                Name = "Test Rifle",
                BaseTUCostToFire = 10f,
                LoadedAmmunition = new AmmunitionProfile { MuzzleVelocity = 100f, Ballistics = new BallisticProfile { Mass = 0.01f, CrossSectionalArea = 0.01f, DragModel = new StandardDragCurve(1f) } }
            };

            var healthyAction = new ShootTacticalAction(dummy, Vector3.UnitZ, new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.8f, 0)));
            float healthyCost = healthyAction.TUCost;

            var finger = new BodyPart { Type = BodyPartType.LeftArm };
            finger.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Bone, OrganType.None));
            physiology.SetRoot(finger);
            finger.Voxels[0].ApplyKineticEnergy(100f, Vector3.Zero, 0.001f);
            physiology.TickPhysiology(10f);

            var painfulAction = new ShootTacticalAction(dummy, Vector3.UnitZ, new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.8f, 0)));
            float painfulCost = painfulAction.TUCost;

            Assert.True(painfulCost > healthyCost, "Pain should increase TU cost.");
        }
        
        [Fact]
        public void HighPain_CausesAccuracyDrift()
        {
            var physiology = new TacticalActorPhysiology();
            var dummy = new TacticalEntity(Vector3.Zero, physiology);
            dummy.EquippedWeapon = new WeaponProfile
            {
                Name = "Test Rifle",
                BaseTUCostToFire = 10f,
                LoadedAmmunition = new AmmunitionProfile { MuzzleVelocity = 100f, Ballistics = new BallisticProfile { Mass = 0.01f, CrossSectionalArea = 0.01f, DragModel = new StandardDragCurve(1f) } }
            };

            var healthyAction = new ShootTacticalAction(dummy, Vector3.UnitZ, new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.8f, 0)));
            healthyAction.ExecutionProgress = 100f;
            healthyAction.Execute(1f);
            var healthyState = healthyAction.FinalState;

            var arm = new BodyPart { Type = BodyPartType.LeftLeg };
            arm.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Bone, OrganType.None));
            physiology.SetRoot(arm);
            arm.Voxels[0].ApplyKineticEnergy(500f, Vector3.Zero, 0.001f);
            physiology.TickPhysiology(10f);

            var painfulAction = new ShootTacticalAction(dummy, Vector3.UnitZ, new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.8f, 0)));
            painfulAction.ExecutionProgress = 100f;
            painfulAction.Execute(1f);
            var painfulState = painfulAction.FinalState;

            Vector3 healthyDir = Vector3.Normalize(healthyState.Value.Velocity);
            Vector3 painfulDir = Vector3.Normalize(painfulState.Value.Velocity);
            
            float dotProduct = Vector3.Dot(healthyDir, painfulDir);
            Assert.True(dotProduct < 0.999f, "High pain should cause angular deviation in the trajectory.");
        }
    }
}
