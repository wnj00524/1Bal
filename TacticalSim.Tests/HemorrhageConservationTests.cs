using System.Numerics;
using TacticalSim.Core;
using TacticalSim.Core.Physiology;
using Xunit;

namespace TacticalSim.Tests;

public class HemorrhageConservationTests
{
    [Fact]
    public void CatastrophicBleedCannotExceedCirculatingBloodVolume()
    {
        var physiology = CreatePhysiology(arterialBleedRate: 1_000_000f);

        physiology.TickPhysiology(60f);
        physiology.TickPhysiology(60f);

        Assert.Equal(0f, physiology.TotalBloodVolume);
    }

    [Fact]
    public void AlveolarBloodCannotExceedAnatomicalLungVolumeOrBloodLost()
    {
        var physiology = CreatePhysiology(arterialBleedRate: 1_000f);
        var airway = CreateVoxel(0.1f, OrganType.Airway, TissueRegistry.Airway);
        var lung = CreateVoxel(0.01f, OrganType.Lung, TissueRegistry.Lung); // 1 ml
        physiology.RootBodyPart.Voxels.Add(airway);
        physiology.RootBodyPart.Voxels.Add(lung);
        airway.ApplyKineticEnergy(10_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(10f);

        float bloodLost = 5_000f - physiology.TotalBloodVolume;
        Assert.InRange(physiology.AlveolarBloodAccumulation, 0f, 1f);
        Assert.True(physiology.AlveolarBloodAccumulation <= bloodLost);
    }

    [Fact]
    public void FallingPressureReducesSubsequentBloodLossRate()
    {
        var physiology = CreatePhysiology(arterialBleedRate: 2_000f);

        physiology.TickPhysiology(1f);
        float firstLoss = 5_000f - physiology.TotalBloodVolume;
        physiology.TickPhysiology(1f);
        float secondLoss = 5_000f - physiology.TotalBloodVolume - firstLoss;

        Assert.Equal(2_000f, firstLoss, 2);
        Assert.True(secondLoss < firstLoss);
        Assert.True(secondLoss > 0f);
    }

    [Fact]
    public void ReducedHeartRateReducesMeanArterialPressure()
    {
        var physiology = CreatePhysiology();
        var firstHeartVoxel = CreateVoxel(0.01f, OrganType.Heart, TissueRegistry.Heart);
        physiology.RootBodyPart.Voxels.Add(firstHeartVoxel);
        physiology.RootBodyPart.Voxels.Add(
            new PhysiologicalVoxel(Vector3.UnitX, 0.01f, TissueRegistry.Heart, OrganType.Heart));
        firstHeartVoxel.ApplyKineticEnergy(10_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(0f);

        Assert.Equal(40f, physiology.HeartRateBpm, 2);
        Assert.Equal(46.5f, physiology.MeanArterialPressureMmhg, 2);
    }

    private static TacticalActorPhysiology CreatePhysiology(float arterialBleedRate = 0f)
    {
        var physiology = new TacticalActorPhysiology();
        physiology.SetRoot(new BodyPart
        {
            Type = BodyPartType.Thorax,
            ArterialBleedRate = arterialBleedRate
        });
        return physiology;
    }

    private static PhysiologicalVoxel CreateVoxel(
        float size,
        OrganType organ,
        TissueProperties tissue) => new(Vector3.Zero, size, tissue, organ);
}
