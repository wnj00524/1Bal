using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Core.Cover;

/// <summary>
/// Applies cover intersections to an already integrated ballistic trajectory segment.
/// </summary>
public sealed class CoverTrajectorySolver : ICoverTrajectorySolver
{
    private readonly IMaterialRegistry _materials;
    private readonly IMaterialPenetrationSystem _penetration;

    public CoverTrajectorySolver(IMaterialRegistry materials, IMaterialPenetrationSystem penetration)
    {
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _penetration = penetration ?? throw new ArgumentNullException(nameof(penetration));
    }

    public CoverTrajectoryResult ResolveSegment(
        in ProjectileState start,
        in ProjectileState end,
        in BallisticProfile profile,
        IEnumerable<CoverPolygon> cover)
    {
        ArgumentNullException.ThrowIfNull(cover);
        var crossings = new List<(CoverPolygon Cover, CoverIntersection Hit)>();
        foreach (CoverPolygon polygon in cover)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            if (polygon.TryIntersect(start.Position, end.Position, out CoverIntersection hit))
            {
                crossings.Add((polygon, hit));
            }
        }

        crossings.Sort((left, right) => left.Hit.PathFraction.CompareTo(right.Hit.PathFraction));
        var state = end;
        Vector3 velocity = start.Velocity;
        var impacts = new List<PenetrationResult>(crossings.Count);

        foreach (var crossing in crossings)
        {
            Vector3 impactPoint = Vector3.Lerp(start.Position, end.Position, crossing.Hit.PathFraction);
            var impactState = new ProjectileState
            {
                Position = impactPoint,
                Velocity = velocity,
                Time = start.Time + (end.Time - start.Time) * crossing.Hit.PathFraction
            };
            PenetrationResult result = _penetration.CalculatePenetration(
                impactState,
                profile,
                _materials.GetMaterial(crossing.Cover.Material),
                crossing.Cover.Thickness,
                crossing.Hit.SurfaceNormal);
            impacts.Add(result);
            velocity = result.ExitVelocityVector;

            if (result.Outcome != PenetrationOutcome.Perforated)
            {
                state = result.ExitState;
                break;
            }

            state.Velocity = velocity;
        }

        return new CoverTrajectoryResult { State = state, Impacts = impacts };
    }
}
