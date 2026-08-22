using System.Numerics;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public class CumulativeImpactTests
{
    [Fact]
    public void RepeatedSubThresholdImpactsAccumulateUntilTissueTears()
    {
        const float voxelSize = 0.01f;
        TissueProperties tissue = TissueRegistry.Liver;
        var voxel = new PhysiologicalVoxel(Vector3.Zero, voxelSize, tissue, OrganType.Liver);
        float voxelVolume = voxelSize * voxelSize * voxelSize;
        float tearThreshold = tissue.ShearStrength * 0.1f * voxelVolume;
        float stretchDenominator = tissue.Density * tissue.Elasticity * 50f + 1e-4f;
        float energyPerImpact = tearThreshold * stretchDenominator * 0.6f;

        voxel.ApplyKineticEnergy(energyPerImpact, Vector3.Zero);

        Assert.Equal(energyPerImpact, voxel.DepositedEnergy, 5);
        Assert.Equal(0f, voxel.PermanentCavityVolume);

        voxel.ApplyKineticEnergy(energyPerImpact, Vector3.Zero);

        Assert.Equal(energyPerImpact * 2f, voxel.DepositedEnergy, 5);
        Assert.True(voxel.PermanentCavityVolume > 0f);
    }

    [Fact]
    public void ActorRetainsExistingWoundsWhenAnotherBodyRegionIsHit()
    {
        var firstVoxel = new PhysiologicalVoxel(
            Vector3.Zero, 0.01f, TissueRegistry.Liver, OrganType.Liver);
        var secondVoxel = new PhysiologicalVoxel(
            new Vector3(0.1f, 0f, 0f), 0.01f, TissueRegistry.Liver, OrganType.Liver);
        var root = new BodyPart { Type = BodyPartType.Abdomen };
        root.Voxels.Add(firstVoxel);
        root.Voxels.Add(secondVoxel);
        var physiology = new TacticalActorPhysiology();
        physiology.SetRoot(root);

        physiology.ProcessLegacyImpact(
            Vector3.UnitZ,
            Energy.FromJoules(1_000f),
            firstVoxel.Center,
            DamageModelVersion.LegacyV1);
        float firstWoundEnergy = firstVoxel.DepositedEnergy;
        physiology.ProcessLegacyImpact(
            Vector3.UnitZ,
            Energy.FromJoules(750f),
            secondVoxel.Center,
            DamageModelVersion.LegacyV1);

        Assert.Equal(firstWoundEnergy, firstVoxel.DepositedEnergy);
        Assert.Equal(750f, secondVoxel.DepositedEnergy);
    }
}
