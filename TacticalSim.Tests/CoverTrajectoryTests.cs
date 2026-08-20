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

        Assert.True(cover.TryIntersect(new Vector2(-2, 0), new Vector2(2, 0), out var hit));
        Assert.Equal(new Vector2(-1, 0), hit.Point);
        Assert.Equal(0.25f, hit.PathFraction, 5);
        Assert.True(Vector2.Dot(hit.SurfaceNormal, Vector2.UnitX) <= 0f);
    }

    [Fact]
    public void TrajectoryCrossingCoverLosesVelocity()
    {
        var solver = new CoverTrajectorySolver(new MaterialRegistry(), new MaterialPenetrationSystem());
        var start = new ProjectileState { Position = new Vector3(-2, 0, 0), Velocity = new Vector3(800, 0, 0), Time = 1 };
        var end = new ProjectileState { Position = new Vector3(2, 0, 0), Velocity = start.Velocity, Time = 1.01f };
        var profile = new BallisticProfile { Mass = 0.01f, CrossSectionalArea = 0.00005f, DragModel = new StandardDragCurve(0.3f) };

        var result = solver.ResolveSegment(start, end, profile, [Square(MaterialType.Wood, 0.05f)]);

        Assert.Single(result.Impacts);
        Assert.Equal(PenetrationOutcome.Perforated, result.Impacts[0].Outcome);
        Assert.InRange(result.State.Velocity.Length(), 0.01f, start.Velocity.Length() - 0.01f);
    }

    [Fact]
    public void CoreRegistrationIncludesCoverTrajectorySolver()
    {
        using var provider = new ServiceCollection().AddTacticalSimCore().BuildServiceProvider();
        Assert.IsType<CoverTrajectorySolver>(provider.GetRequiredService<ICoverTrajectorySolver>());
    }

    private static CoverPolygon Square(MaterialType material, float thickness) => new(
        [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)], thickness, material);
}
