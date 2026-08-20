using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.World;

namespace TacticalSim.Tests;

public class ProjectileFlightTerminationTests
{
    private static readonly WorldBounds Bounds = new(
        new Vector3(-10f, 0f, -10f),
        new Vector3(10f, 10f, 10f));

    private static BallisticProfile Profile(float mass = 0.01f) => new()
    {
        Mass = mass,
        CrossSectionalArea = 0.0001f,
        DragModel = new StandardDragCurve(0.2f)
    };

    [Fact]
    public void Evaluate_ContinuesEnergeticFlightInsideWorld()
    {
        var state = new ProjectileState { Position = new Vector3(0f, 2f, 0f), Velocity = new Vector3(0f, 0f, 100f) };

        Assert.Equal(ProjectileTerminationReason.None,
            ProjectileFlightTermination.Evaluate(state, Profile(), Bounds));
    }

    [Fact]
    public void Evaluate_StopsWhenEnergyIsDepleted()
    {
        var state = new ProjectileState { Position = new Vector3(0f, 2f, 0f), Velocity = Vector3.One };

        Assert.Equal(ProjectileTerminationReason.EnergyDepleted,
            ProjectileFlightTermination.Evaluate(state, Profile(), Bounds));
    }

    [Fact]
    public void Evaluate_StopsAtNonRicochetingGroundImpact()
    {
        var state = new ProjectileState { Position = new Vector3(0f, 0f, 0f), Velocity = new Vector3(0f, -2f, 100f) };

        Assert.Equal(ProjectileTerminationReason.GroundImpact,
            ProjectileFlightTermination.Evaluate(state, Profile(), Bounds));
    }

    [Fact]
    public void Evaluate_KeepsTrackingUpwardGroundRicochet()
    {
        var state = new ProjectileState { Position = new Vector3(0f, 0f, 0f), Velocity = new Vector3(0f, 2f, 100f) };

        Assert.Equal(ProjectileTerminationReason.None,
            ProjectileFlightTermination.Evaluate(state, Profile(), Bounds));
    }

    [Fact]
    public void Evaluate_StopsOutsideWorldBoundary()
    {
        var state = new ProjectileState { Position = new Vector3(0f, 2f, 11f), Velocity = new Vector3(0f, 0f, 100f) };

        Assert.Equal(ProjectileTerminationReason.WorldBoundary,
            ProjectileFlightTermination.Evaluate(state, Profile(), Bounds));
    }
}
