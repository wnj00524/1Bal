using System.Numerics;
using TacticalSim.Core;
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

    [Fact]
    public void SeriousOpenChestWoundReportMakesFirstAidBenefitObvious()
    {
        var physiology = CreateSeriousChestWound();

        string untreated = MedicalAssessor.AssessTrauma(physiology).AssessmentText;
        Assert.Contains("FATAL WITHIN 5 MINUTES WITHOUT FIRST AID", untreated);

        physiology.ApplyChestSeal();
        string sealedAssessment = MedicalAssessor.AssessTrauma(physiology).AssessmentText;
        Assert.Contains("DETERIORATION HALTED; NEEDLE DECOMPRESSION REQUIRED", sealedAssessment);

        physiology.PerformNeedleDecompression();
        string decompressed = MedicalAssessor.AssessTrauma(physiology).AssessmentText;
        Assert.Contains("STABILIZED BY FIRST AID", decompressed);
    }

    [Fact]
    public void SeriousChestWoundIsFatalWithinFiveMinutesButFirstAidPreventsArrest()
    {
        var untreated = CreateSeriousChestWound();
        var treated = CreateSeriousChestWound();
        treated.ApplyChestSeal();
        treated.PerformNeedleDecompression();

        for (int elapsed = 0; elapsed < 300; elapsed += 5)
        {
            untreated.TickPhysiology(5f);
            treated.TickPhysiology(5f);
        }

        Assert.Equal(0f, untreated.ConsciousnessLevel);
        Assert.True(treated.ConsciousnessLevel > 0f);
        Assert.True(treated.BloodOxygenation >= 0.9f);
    }

    private static TacticalActorPhysiology CreateSeriousChestWound()
    {
        var physiology = new TacticalActorPhysiology();
        var thorax = new BodyPart { Type = BodyPartType.Thorax };
        for (int index = 0; index < 10; index++)
        {
            var voxel = new PhysiologicalVoxel(
                new Vector3(index * 0.01f, 0f, 0f), 0.01f, TissueRegistry.Lung, OrganType.Lung);
            if (index < 2)
                voxel.ApplyKineticEnergy(1_000f, voxel.Center, 0.000001f);
            thorax.Voxels.Add(voxel);
        }
        physiology.SetRoot(thorax);
        physiology.TickPhysiology(1f);
        return physiology;
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
