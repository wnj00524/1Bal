using System;
using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Physiology;
using Xunit;

namespace TacticalSim.Tests;

public class InternalRicochetSolverTests
{
    private static readonly BallisticProfile Profile = new()
    {
        Mass = 0.008f,
        CrossSectionalArea = 0.00005f,
        DragModel = new StandardDragCurve(0.3f)
    };

    private static readonly TissueProperties CorticalBone = new()
    {
        Density = 1_900f,
        ShearStrength = 100f,
        Elasticity = 0.15f
    };

    [Fact]
    public void LowEnergyGlancingImpactRicochetsDeterministically()
    {
        Vector3 incoming = Vector3.Normalize(new Vector3(1f, -0.2f, 0f)) * 100f;

        BoneImpactResult first = InternalRicochetSolver.Resolve(
            incoming, Profile, Vector3.UnitY, CorticalBone, 0.01f);
        BoneImpactResult replay = InternalRicochetSolver.Resolve(
            incoming, Profile, Vector3.UnitY, CorticalBone, 0.01f);

        Assert.Equal(BoneImpactOutcome.Ricocheted, first.Outcome);
        Assert.Equal(first, replay);
        Assert.True(first.Velocity.Y > 0f);
        Assert.True(first.Velocity.Length() < incoming.Length());
        Assert.True(first.TransferredEnergy > 0f);
    }

    [Fact]
    public void HighEnergyNormalImpactShattersBoneAndKeepsDirection()
    {
        Vector3 incoming = new(0f, -800f, 0f);

        BoneImpactResult result = InternalRicochetSolver.Resolve(
            incoming, Profile, Vector3.UnitY, CorticalBone, 0.01f);

        Assert.Equal(BoneImpactOutcome.Shattered, result.Outcome);
        Assert.Equal(incoming, result.Velocity);
        Assert.True(result.TransferredEnergy > 0f);
    }

    [Fact]
    public void DenserAndThickerBoneRaisesShatterThreshold()
    {
        BoneImpactResult baseline = InternalRicochetSolver.Resolve(
            new Vector3(0f, -100f, 0f), Profile, Vector3.UnitY, CorticalBone, 0.01f);
        TissueProperties denserBone = CorticalBone;
        denserBone.Density *= 2f;
        BoneImpactResult resistant = InternalRicochetSolver.Resolve(
            new Vector3(0f, -100f, 0f), Profile, Vector3.UnitY, denserBone, 0.02f);

        Assert.Equal(baseline.ShatterThreshold * 4f, resistant.ShatterThreshold, 3);
    }

    [Fact]
    public void InvalidGeometryIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InternalRicochetSolver.Resolve(
            Vector3.UnitX, Profile, Vector3.Zero, CorticalBone, 0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => InternalRicochetSolver.Resolve(
            Vector3.UnitX, Profile, Vector3.UnitY, CorticalBone, 0f));
    }

    [Fact]
    public void BoneVoxelAppliesRicochetToProjectileState()
    {
        var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.01f, CorticalBone, OrganType.Bone);
        var projectile = new ProjectileState
        {
            Position = new Vector3(-0.02f, 0.003f, 0f),
            Velocity = Vector3.Normalize(new Vector3(1f, -0.1f, 0f)) * 100f
        };

        CavitationEvent? result = voxel.ProcessPenetration(ref projectile, Profile);

        Assert.Null(result);
        Assert.True(projectile.Velocity.X < 0f);
        Assert.True(voxel.DepositedEnergy > 0f);
    }
}
