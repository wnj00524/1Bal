using System.Numerics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Core.Cover;

/// <summary>
/// A closed cover footprint on the tactical X/Z plane with a material depth.
/// </summary>
public sealed class CoverPolygon
{
    private readonly Vector2[] _vertices;

    public CoverPolygon(IEnumerable<Vector2> vertices, float thickness, MaterialType material)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        _vertices = vertices.ToArray();
        if (_vertices.Length < 3)
        {
            throw new ArgumentException("A cover polygon requires at least three vertices.", nameof(vertices));
        }

        if (_vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)))
        {
            throw new ArgumentException("Cover vertices must be finite.", nameof(vertices));
        }

        if (!float.IsFinite(thickness) || thickness < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), "Cover thickness must be finite and non-negative.");
        }

        Thickness = thickness;
        Material = material;
    }

    public IReadOnlyList<Vector2> Vertices => _vertices;
    public float Thickness { get; }
    public MaterialType Material { get; }

    /// <summary>Finds the first boundary crossed by a finite line segment.</summary>
    public bool TryIntersect(Vector2 start, Vector2 end, out CoverIntersection intersection)
    {
        intersection = default;
        Vector2 path = end - start;
        if (path.LengthSquared() <= 1e-12f)
        {
            return false;
        }

        float nearest = float.PositiveInfinity;
        Vector2 nearestPoint = default;
        Vector2 nearestNormal = default;

        for (int i = 0; i < _vertices.Length; i++)
        {
            Vector2 a = _vertices[i];
            Vector2 edge = _vertices[(i + 1) % _vertices.Length] - a;
            float denominator = Cross(path, edge);
            if (MathF.Abs(denominator) <= 1e-7f)
            {
                continue;
            }

            Vector2 offset = a - start;
            float pathFraction = Cross(offset, edge) / denominator;
            float edgeFraction = Cross(offset, path) / denominator;
            if (pathFraction < 0f || pathFraction > 1f || edgeFraction < 0f || edgeFraction > 1f || pathFraction >= nearest)
            {
                continue;
            }

            nearest = pathFraction;
            nearestPoint = start + path * pathFraction;
            nearestNormal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            if (Vector2.Dot(nearestNormal, path) > 0f)
            {
                nearestNormal = -nearestNormal;
            }
        }

        if (!float.IsFinite(nearest))
        {
            return false;
        }

        intersection = new CoverIntersection(nearestPoint, nearestNormal, nearest);
        return true;
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
}

public readonly record struct CoverIntersection(Vector2 Point, Vector2 SurfaceNormal, float PathFraction);
