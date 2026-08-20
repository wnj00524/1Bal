using System.Numerics;

namespace TacticalSim.Core.World;

/// <summary>An immutable axis-aligned bounding box expressed in metres.</summary>
public readonly struct WorldBounds : IEquatable<WorldBounds>
{
    public WorldBounds(Vector3 min, Vector3 max)
    {
        if (!IsFinite(min))
            throw new ArgumentException("Minimum corner must contain only finite values.", nameof(min));
        if (!IsFinite(max))
            throw new ArgumentException("Maximum corner must contain only finite values.", nameof(max));
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException("Minimum corner must be strictly less than maximum corner on every axis.");

        Min = min;
        Max = max;
        Size = max - min;
        Centre = (min + max) * 0.5f;
        Volume = Size.X * Size.Y * Size.Z;
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Size { get; }
    public Vector3 Centre { get; }
    public float Volume { get; }

    public bool Contains(Vector3 point) => IsFinite(point)
        && point.X >= Min.X && point.X <= Max.X
        && point.Y >= Min.Y && point.Y <= Max.Y
        && point.Z >= Min.Z && point.Z <= Max.Z;

    public Vector3 Clamp(Vector3 point)
    {
        if (!IsFinite(point))
            throw new ArgumentException("Point must contain only finite values.", nameof(point));

        return Vector3.Clamp(point, Min, Max);
    }

    public static WorldBounds CreateDefault() => new(
        new Vector3(-50f, 0f, -50f),
        new Vector3(50f, 30f, 50f));

    public bool Equals(WorldBounds other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is WorldBounds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Min, Max);
    public static bool operator ==(WorldBounds left, WorldBounds right) => left.Equals(right);
    public static bool operator !=(WorldBounds left, WorldBounds right) => !left.Equals(right);
    public override string ToString() => $"WorldBounds(Min={Min}, Max={Max}, Size={Size})";

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
