using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Materials;

namespace TacticalSim.Tests;

public class CoverTrajectoryTests
{
    [Fact]
    public void PolygonFindsNearestSegmentIntersectionAndNormal()
    {
        var cover = Square(MaterialType.Wood, 0.05f);

        Assert.True(cover.TryIntersect(new Vector3(-2, 0, 0), new Vector3(2, 0, 0), out var hit));
        Assert.Equal(new Vector3(0, 0, 0), hit.Point);
        Assert.Equal(0.5f, hit.PathFraction, 5);
        Assert.True(Vector3.Dot(hit.SurfaceNormal, Vector3.UnitX) <= 0f);
    }

    [Fact]
    public void TrajectoryCrossingCoverLosesVelocity()
    {
        var solver = new CoverTrajectorySolver(new MaterialRegistry(), new MaterialPenetrationSystem());
        var start = new ProjectileState { Position = new Vector3(-2, 1, 0), Velocity = new Vector3(800, 0, 0), Time = 1 };
        var end = new ProjectileState { Position = new Vector3(2, 1, 0), Velocity = start.Velocity, Time = 1.01f };
        var profile = new BallisticProfile { Mass = 0.01f, CrossSectionalArea = 0.00005f, DragModel = new StandardDragCurve(0.3f) };

        var result = solver.ResolveSegment(start, end, profile, [Square(MaterialType.Wood, 0.05f)]);

        Assert.Single(result.Impacts);
        Assert.Equal(PenetrationOutcome.Perforated, result.Impacts[0].Outcome);
        Assert.InRange(result.State.Velocity.Length(), 0.01f, start.Velocity.Length() - 0.01f);
    }

    [Fact]
    public void TrajectoryAboveThreeDimensionalCoverDoesNotIntersect()
    {
        var cover = Square(MaterialType.Wood, 0.05f);

        Assert.False(cover.TryIntersect(new Vector3(-2, 3, 0), new Vector3(2, 3, 0), out _));
    }

    [Fact]
    public void SlopedCoverIntersectsThreeDimensionalTrajectory()
    {
        var cover = new CoverPolygon(
            [new(0, 0, -1), new(2, 2, -1), new(2, 2, 1), new(0, 0, 1)],
            0.1f,
            MaterialType.Steel);

        Assert.True(cover.TryIntersect(new Vector3(1, 2, 0), new Vector3(1, 0, 0), out var hit));
        Assert.Equal(new Vector3(1, 1, 0), hit.Point);
        Assert.Equal(0.5f, hit.PathFraction, 5);
        Assert.True(Vector3.Dot(hit.SurfaceNormal, -Vector3.UnitY) <= 0f);
    }

    [Fact]
    public void CoreRegistrationIncludesCoverTrajectorySolver()
    {
        using var provider = new ServiceCollection().AddTacticalSimCore().BuildServiceProvider();
        Assert.IsType<CoverTrajectorySolver>(provider.GetRequiredService<ICoverTrajectorySolver>());
    }

    private static CoverPolygon Square(MaterialType material, float thickness) => new(
        [new(0, 0, -1), new(0, 2, -1), new(0, 2, 1), new(0, 0, 1)], thickness, material);
}
