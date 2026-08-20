using System.Numerics;
using TacticalSim.Core.Physiology;
using Xunit;

namespace TacticalSim.Tests;

public class NeurologicalControlTests
{
    [Fact]
    public void BrainDamageScalesAutonomicHeartAndBreathingRates()
    {
        var physiology = new TacticalActorPhysiology();
        var head = new BodyPart { Type = BodyPartType.Head };
        head.Voxels.Add(CreateVoxel(Vector3.Zero, OrganType.Brain, TissueRegistry.Brain));
        head.Voxels.Add(CreateVoxel(Vector3.UnitX, OrganType.Brain, TissueRegistry.Brain));
        physiology.SetRoot(head);

        head.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);
        physiology.TickPhysiology(0f);

        Assert.Equal(0.5f, physiology.AutonomicDrive);
        Assert.Equal(40f, physiology.HeartRateBpm);
        Assert.Equal(6f, physiology.BreathingRatePerMinute);
    }

    [Fact]
    public void CompleteBrainDestructionStopsAutonomicDrive()
    {
        var physiology = CreateSingleOrganPhysiology(OrganType.Brain, TissueRegistry.Brain);
        physiology.RootBodyPart.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(1f);

        Assert.Equal(0f, physiology.AutonomicDrive);
        Assert.Equal(0f, physiology.HeartRateBpm);
        Assert.Equal(0f, physiology.BreathingRatePerMinute);
        Assert.Equal(0f, physiology.MeanArterialPressureMmhg);
        Assert.Equal(0f, physiology.ConsciousnessLevel);
    }

    [Fact]
    public void DestroyedHeartStopsCirculationAndCausesProgressiveHypoxia()
    {
        var physiology = CreateSingleOrganPhysiology(OrganType.Heart, TissueRegistry.Muscle);
        physiology.RootBodyPart.Voxels[0].ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);

        physiology.TickPhysiology(1f);
        float initialOxygenation = physiology.BloodOxygenation;
        physiology.TickPhysiology(10f);

        Assert.Equal(0f, physiology.HeartRateBpm);
        Assert.Equal(0f, physiology.MeanArterialPressureMmhg);
        Assert.True(physiology.BloodOxygenation < initialOxygenation);
    }

    private static TacticalActorPhysiology CreateSingleOrganPhysiology(
        OrganType organ,
        TissueProperties tissue)
    {
        var physiology = new TacticalActorPhysiology();
        var root = new BodyPart { Type = BodyPartType.Thorax };
        root.Voxels.Add(CreateVoxel(Vector3.Zero, organ, tissue));
        physiology.SetRoot(root);
        return physiology;
    }

    private static PhysiologicalVoxel CreateVoxel(
        Vector3 center,
        OrganType organ,
        TissueProperties tissue) => new(center, 0.01f, tissue, organ);
}
