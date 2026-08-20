using System.Numerics;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Tests;

public class ProjectileTelemetryTests
{
    [Fact]
    public void From_ReturnsVelocityEnergyAndWorldHeight()
    {
        var state = new ProjectileState
        {
            Position = new Vector3(4f, 1.75f, 9f),
            Velocity = new Vector3(3f, 4f, 0f)
        };
        var profile = new BallisticProfile
        {
            Mass = 2f,
            DragModel = new StandardDragCurve(0.3f)
        };

        ProjectileTelemetry telemetry = ProjectileTelemetry.From(state, profile);

        Assert.Equal(5f, telemetry.Velocity);
        Assert.Equal(25f, telemetry.KineticEnergy);
        Assert.Equal(1.75f, telemetry.Height);
    }
}
