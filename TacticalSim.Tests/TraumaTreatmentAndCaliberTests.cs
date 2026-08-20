using System.Numerics;
using TacticalSim.Core;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using Xunit;

namespace TacticalSim.Tests;

public class TraumaTreatmentAndCaliberTests
{
    [Fact]
    public void OffCenterChestImpactDoesNotAutomaticallyDestroyHeartTissue()
    {
        IActorPhysiology physiology = AnatomicalDummyBuilder.BuildDummy();
        List<PhysiologicalVoxel> voxels = Flatten(physiology.RootBodyPart);

        physiology.ProcessImpact(Vector3.UnitZ, 5_000f, new Vector3(-0.12f, 0.35f, 0f));

        Assert.Contains(voxels, voxel => voxel.IsDestroyed);
        Assert.DoesNotContain(voxels, voxel => voxel.Organ == OrganType.Heart && voxel.IsDestroyed);
    }

    [Fact]
    public void CatalogIncludesLowerEnergyRoundsForComparison()
    {
        AmmunitionProfile twentyTwo = AmmunitionCatalog.TwentyTwoLongRifle;
        AmmunitionProfile threeEighty = AmmunitionCatalog.ThreeEightyAcp;
        float twentyTwoEnergy = KineticEnergy(twentyTwo);
        float threeEightyEnergy = KineticEnergy(threeEighty);
        float rifleEnergy = 0.5f * 0.004f * 900f * 900f;

        Assert.Equal(".22 LR", twentyTwo.Name);
        Assert.Equal(".380 ACP", threeEighty.Name);
        Assert.True(twentyTwoEnergy < threeEightyEnergy);
        Assert.True(threeEightyEnergy < rifleEnergy);
    }

    [Fact]
    public void TourniquetStopsExtremityBleedingButIsRejectedForAbdomen()
    {
        var physiology = CreateRegionalPhysiology();
        BodyPart arm = physiology.RootBodyPart.Children[0];
        arm.Voxels[0].ApplyKineticEnergy(1_000f, arm.Voxels[0].Center, 0.001f);

        Assert.True(arm.GetActiveBleedRate() > 0f);
        Assert.True(physiology.ApplyTourniquet(BodyPartType.LeftArm));
        Assert.Equal(0f, arm.GetActiveBleedRate());
        Assert.False(physiology.ApplyTourniquet(BodyPartType.Abdomen));
    }

    [Fact]
    public void PackingControlsExternalAbdominalWoundButNotInternalOrganBleeding()
    {
        var physiology = CreateRegionalPhysiology();
        BodyPart abdomen = physiology.RootBodyPart.Children[1];
        foreach (PhysiologicalVoxel voxel in abdomen.Voxels)
            voxel.ApplyKineticEnergy(1_000f, voxel.Center, 0.001f);
        float untreatedRate = abdomen.GetActiveBleedRate();

        Assert.True(physiology.PackExternalWound(BodyPartType.Abdomen));
        float treatedRate = abdomen.GetActiveBleedRate();

        Assert.True(treatedRate < untreatedRate);
        Assert.True(treatedRate >= 0.02f); // liver bleeding remains non-compressible
        Assert.False(physiology.PackExternalWound(BodyPartType.Abdomen));
    }

    private static TacticalActorPhysiology CreateRegionalPhysiology()
    {
        var physiology = new TacticalActorPhysiology();
        var thorax = new BodyPart { Type = BodyPartType.Thorax };
        var arm = new BodyPart { Type = BodyPartType.LeftArm, Parent = thorax };
        var abdomen = new BodyPart { Type = BodyPartType.Abdomen, Parent = thorax };
        arm.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle));
        abdomen.Voxels.Add(new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle));
        abdomen.Voxels.Add(new PhysiologicalVoxel(Vector3.UnitX, 0.01f, TissueRegistry.Liver, OrganType.Liver));
        thorax.Children.Add(arm);
        thorax.Children.Add(abdomen);
        physiology.SetRoot(thorax);
        return physiology;
    }

    private static float KineticEnergy(AmmunitionProfile ammunition) =>
        0.5f * ammunition.Ballistics.Mass * ammunition.MuzzleVelocity * ammunition.MuzzleVelocity;

    private static List<PhysiologicalVoxel> Flatten(BodyPart part)
    {
        var result = new List<PhysiologicalVoxel>(part.Voxels);
        foreach (BodyPart child in part.Children)
            result.AddRange(Flatten(child));
        return result;
    }
}
