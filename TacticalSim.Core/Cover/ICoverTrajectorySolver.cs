using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Core.Cover;

public interface ICoverTrajectorySolver
{
    CoverTrajectoryResult ResolveSegment(
        in ProjectileState start,
        in ProjectileState end,
        in BallisticProfile profile,
        IEnumerable<CoverPolygon> cover);
}

public sealed class CoverTrajectoryResult
{
    public required ProjectileState State { get; init; }
    public IReadOnlyList<PenetrationResult> Impacts { get; init; } = Array.Empty<PenetrationResult>();
}
