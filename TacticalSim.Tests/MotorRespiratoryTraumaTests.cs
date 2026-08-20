using System.Numerics;
using TacticalSim.Core.Physiology;
using Xunit;

namespace TacticalSim.Tests;

public class MotorRespiratoryTraumaTests
{
    [Fact]
    public void DestroyedLegBoneImpairsMobilityButNotWeaponHandling()
    {
        var physiology = CreateExtremityPhysiology(BodyPartType.LeftLeg);
        physiology.RootBodyPart.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(1f);

        Assert.Equal(0f, physiology.MobilityLevel);
        Assert.Equal(1f, physiology.WeaponHandlingLevel);
    }

    [Fact]
    public void DestroyedArmBoneImpairsWeaponHandlingButNotMobility()
    {
        var physiology = CreateExtremityPhysiology(BodyPartType.LeftArm);
        physiology.RootBodyPart.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(1f);

        Assert.Equal(1f, physiology.MobilityLevel);
        Assert.Equal(0f, physiology.WeaponHandlingLevel);
    }

    [Fact]
    public void PuncturedLungProgressesUntilSealedAndNeedleDecompressionRelievesPressure()
    {
        var physiology = new TacticalActorPhysiology();
        var thorax = new BodyPart { Type = BodyPartType.Thorax };
        thorax.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Lung, OrganType.Lung));
        thorax.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);
        physiology.SetRoot(thorax);

        physiology.TickPhysiology(10f);
        float pressureBeforeSeal = physiology.TensionPneumothoraxLevel;
        float oxygenBeforeProgression = physiology.BloodOxygenation;
        physiology.TickPhysiology(10f);

        Assert.True(physiology.TensionPneumothoraxLevel > pressureBeforeSeal);
        Assert.True(physiology.BloodOxygenation < oxygenBeforeProgression);

        physiology.ApplyChestSeal();
        float sealedPressure = physiology.TensionPneumothoraxLevel;
        physiology.TickPhysiology(10f);
        Assert.Equal(sealedPressure, physiology.TensionPneumothoraxLevel);

        physiology.PerformNeedleDecompression();
        Assert.Equal(0f, physiology.TensionPneumothoraxLevel);
        physiology.TickPhysiology(1f);
        Assert.Equal(0f, physiology.TensionPneumothoraxLevel);
    }

    private static TacticalActorPhysiology CreateExtremityPhysiology(BodyPartType type)
    {
        var physiology = new TacticalActorPhysiology();
        var extremity = new BodyPart { Type = type };
        extremity.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Bone, OrganType.Bone));
        physiology.SetRoot(extremity);
        return physiology;
    }
}
