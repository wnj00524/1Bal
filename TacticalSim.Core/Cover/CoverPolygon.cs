using System.Numerics;
using TacticalSim.Core.Materials;

namespace TacticalSim.Core.Cover;

/// <summary>
/// A planar cover surface in three-dimensional simulation space with a material depth.
/// </summary>
public sealed class CoverPolygon
{
    private const float GeometryTolerance = 1e-5f;
    private readonly Vector3[] _vertices;
    private readonly Vector3 _normal;
    private readonly ProjectionPlane _projection;

    public CoverPolygon(IEnumerable<Vector3> vertices, float thickness, MaterialType material)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        _vertices = vertices.ToArray();
        if (_vertices.Length < 3)
        {
            throw new ArgumentException("A cover polygon requires at least three vertices.", nameof(vertices));
        }

        if (_vertices.Any(v => !IsFinite(v)))
        {
            throw new ArgumentException("Cover vertices must be finite.", nameof(vertices));
        }

        if (!float.IsFinite(thickness) || thickness < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), "Cover thickness must be finite and non-negative.");
        }

        Vector3 areaVector = CalculateAreaVector(_vertices);
        if (areaVector.LengthSquared() <= GeometryTolerance * GeometryTolerance)
        {
            throw new ArgumentException("Cover vertices must define a non-degenerate planar surface.", nameof(vertices));
        }

        _normal = Vector3.Normalize(areaVector);
        float planeOffset = Vector3.Dot(_normal, _vertices[0]);
        if (_vertices.Any(vertex => MathF.Abs(Vector3.Dot(_normal, vertex) - planeOffset) > GeometryTolerance))
        {
            throw new ArgumentException("All cover vertices must lie on the same plane.", nameof(vertices));
        }

        _projection = SelectProjectionPlane(_normal);
        Thickness = thickness;
        Material = material;
    }

    public IReadOnlyList<Vector3> Vertices => _vertices;
    public float Thickness { get; }
    public MaterialType Material { get; }
    public Vector3 Normal => _normal;

    /// <summary>Finds where a finite 3D line segment first crosses the polygon surface.</summary>
    public bool TryIntersect(Vector3 start, Vector3 end, out CoverIntersection intersection)
    {
        intersection = default;
        if (!IsFinite(start) || !IsFinite(end))
        {
            return false;
        }

        Vector3 path = end - start;
        float denominator = Vector3.Dot(path, _normal);
        if (path.LengthSquared() <= 1e-12f || MathF.Abs(denominator) <= GeometryTolerance)
        {
            return false;
        }

        float pathFraction = Vector3.Dot(_vertices[0] - start, _normal) / denominator;
        if (pathFraction < 0f || pathFraction > 1f)
        {
            return false;
        }

        Vector3 point = start + path * pathFraction;
        if (!ContainsProjectedPoint(Project(point)))
        {
            return false;
        }

        Vector3 impactNormal = denominator > 0f ? -_normal : _normal;
        intersection = new CoverIntersection(point, impactNormal, pathFraction);
        return true;
    }

    private bool ContainsProjectedPoint(Vector2 point)
    {
        bool inside = false;
        Vector2 previous = Project(_vertices[^1]);
        foreach (Vector3 vertex in _vertices)
        {
            Vector2 current = Project(vertex);
            Vector2 edge = current - previous;
            Vector2 toPoint = point - previous;
            float cross = edge.X * toPoint.Y - edge.Y * toPoint.X;
            if (MathF.Abs(cross) <= GeometryTolerance &&
                Vector2.Dot(toPoint, point - current) <= GeometryTolerance)
            {
                return true;
            }

            if ((current.Y > point.Y) != (previous.Y > point.Y))
            {
                float crossingX = (previous.X - current.X) * (point.Y - current.Y) /
                    (previous.Y - current.Y) + current.X;
                if (point.X < crossingX)
                {
                    inside = !inside;
                }
            }

            previous = current;
        }

        return inside;
    }

    private Vector2 Project(Vector3 point) => _projection switch
    {
        ProjectionPlane.YZ => new Vector2(point.Y, point.Z),
        ProjectionPlane.XZ => new Vector2(point.X, point.Z),
        _ => new Vector2(point.X, point.Y)
    };

    private static Vector3 CalculateAreaVector(IReadOnlyList<Vector3> vertices)
    {
        Vector3 area = Vector3.Zero;
        int count = vertices.Count;
        if (count < 3)
        {
            return area;
        }

        Vector3 current = vertices[count - 1];
        for (int i = 0; i < count; i++)
        {
            Vector3 next = vertices[i];
            area.X += (current.Y - next.Y) * (current.Z + next.Z);
            area.Y += (current.Z - next.Z) * (current.X + next.X);
            area.Z += (current.X - next.X) * (current.Y + next.Y);
            current = next;
        }

        return area;
    }

    private static ProjectionPlane SelectProjectionPlane(Vector3 normal)
    {
        Vector3 absolute = Vector3.Abs(normal);
        if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
        {
            return ProjectionPlane.YZ;
        }

        return absolute.Y >= absolute.Z ? ProjectionPlane.XZ : ProjectionPlane.XY;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private enum ProjectionPlane
    {
        XY,
        XZ,
        YZ
    }
}

public readonly record struct CoverIntersection(Vector3 Point, Vector3 SurfaceNormal, float PathFraction);
