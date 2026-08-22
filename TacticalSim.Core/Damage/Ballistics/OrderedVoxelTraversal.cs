using System;
using System.Collections.Generic;
using System.Numerics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// Stable M5 spatial lookup over the existing voxel anatomy. Voxels remain an
/// implementation detail: public wound contracts expose only stable structure
/// identifiers and body/structure labels.
/// </summary>
internal static class OrderedVoxelTraversal
{
    private const float DirectionEpsilon = 1e-8f;
    private const float MinimumPathMeters = 1e-7f;

    internal static IReadOnlyList<VoxelRayIntersection> FindIntersections(
        BodyPart root,
        Vector3 rayOriginMeters,
        Vector3 rayDirection,
        Distance maximumDistance)
    {
        ArgumentNullException.ThrowIfNull(root);
        ValidateVector(rayOriginMeters, nameof(rayOriginMeters));
        ValidateVector(rayDirection, nameof(rayDirection));

        if (rayDirection.LengthSquared() <= DirectionEpsilon)
            throw new ArgumentOutOfRangeException(nameof(rayDirection), "Projectile direction must be non-zero.");
        if (maximumDistance.Meters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumDistance), "Traversal distance must be positive.");

        Vector3 direction = Vector3.Normalize(rayDirection);
        var intersections = new List<VoxelRayIntersection>();
        foreach (BodyVoxelReference reference in EnumerateVoxels(root))
        {
            PhysiologicalVoxel voxel = reference.Voxel;
            if (voxel.IsDestroyed
                || !TryIntersectAabb(
                    voxel.MinBounds,
                    voxel.MaxBounds,
                    rayOriginMeters,
                    direction,
                    maximumDistance.Meters,
                    out float entryDistance,
                    out float exitDistance))
            {
                continue;
            }

            Vector3 entryPoint = rayOriginMeters + direction * entryDistance;
            Vector3 exitPoint = rayOriginMeters + direction * exitDistance;
            intersections.Add(new VoxelRayIntersection(
                voxel,
                reference.StructureId,
                reference.BodyRegion,
                reference.StructureType,
                entryDistance,
                exitDistance,
                entryPoint,
                exitPoint,
                CalculateEntryNormal(voxel, entryPoint, direction)));
        }

        intersections.Sort(static (left, right) =>
        {
            int entryOrder = left.EntryDistanceMeters.CompareTo(right.EntryDistanceMeters);
            if (entryOrder != 0) return entryOrder;

            int exitOrder = left.ExitDistanceMeters.CompareTo(right.ExitDistanceMeters);
            if (exitOrder != 0) return exitOrder;

            return StringComparer.Ordinal.Compare(left.StructureId, right.StructureId);
        });

        return intersections.AsReadOnly();
    }

    internal static IReadOnlyList<BodyVoxelReference> EnumerateVoxels(BodyPart root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var references = new List<BodyVoxelReference>();
        AddBodyPartVoxels(root, root.Type.ToString(), references);
        var structureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyVoxelReference reference in references)
        {
            if (!structureIds.Add(reference.StructureId))
            {
                throw new InvalidOperationException(
                    $"The M5 anatomy snapshot produced duplicate structure ID '{reference.StructureId}'.");
            }
        }
        return references.AsReadOnly();
    }

    private static void AddBodyPartVoxels(
        BodyPart bodyPart,
        string bodyPath,
        List<BodyVoxelReference> references)
    {
        for (int voxelIndex = 0; voxelIndex < bodyPart.Voxels.Count; voxelIndex++)
        {
            PhysiologicalVoxel voxel = bodyPart.Voxels[voxelIndex];
            // M6 will replace this spatial snapshot with explicit anatomical
            // structure IDs. For M5, derive the ID from semantic hierarchy/type and
            // body-local coordinates rather than child or voxel list positions.
            string structureId = FormattableString.Invariant(
                $"{bodyPath}/{voxel.Organ}@{voxel.Center.X:R},{voxel.Center.Y:R},{voxel.Center.Z:R}");
            references.Add(new BodyVoxelReference(
                voxel,
                structureId,
                bodyPart.Type.ToString(),
                voxel.Organ.ToString()));
        }

        foreach (BodyPart child in bodyPart.Children)
        {
            AddBodyPartVoxels(
                child,
                $"{bodyPath}/{child.Type}",
                references);
        }
    }

    private static bool TryIntersectAabb(
        Vector3 minimum,
        Vector3 maximum,
        Vector3 origin,
        Vector3 direction,
        float maximumDistance,
        out float entryDistance,
        out float exitDistance)
    {
        float entry = 0f;
        float exit = maximumDistance;

        if (!ClipAxis(minimum.X, maximum.X, origin.X, direction.X, ref entry, ref exit)
            || !ClipAxis(minimum.Y, maximum.Y, origin.Y, direction.Y, ref entry, ref exit)
            || !ClipAxis(minimum.Z, maximum.Z, origin.Z, direction.Z, ref entry, ref exit)
            || exit - entry <= MinimumPathMeters)
        {
            entryDistance = 0f;
            exitDistance = 0f;
            return false;
        }

        entryDistance = entry;
        exitDistance = exit;
        return true;
    }

    private static bool ClipAxis(
        float minimum,
        float maximum,
        float origin,
        float direction,
        ref float entry,
        ref float exit)
    {
        if (MathF.Abs(direction) <= DirectionEpsilon)
            return origin >= minimum && origin <= maximum;

        float inverse = 1f / direction;
        float first = (minimum - origin) * inverse;
        float second = (maximum - origin) * inverse;
        if (first > second)
            (first, second) = (second, first);

        entry = MathF.Max(entry, first);
        exit = MathF.Min(exit, second);
        return entry <= exit;
    }

    private static Vector3 CalculateEntryNormal(
        PhysiologicalVoxel voxel,
        Vector3 entryPoint,
        Vector3 direction)
    {
        var candidates = new (float Distance, Vector3 Normal)[]
        {
            (MathF.Abs(entryPoint.X - voxel.MinBounds.X), -Vector3.UnitX),
            (MathF.Abs(entryPoint.X - voxel.MaxBounds.X), Vector3.UnitX),
            (MathF.Abs(entryPoint.Y - voxel.MinBounds.Y), -Vector3.UnitY),
            (MathF.Abs(entryPoint.Y - voxel.MaxBounds.Y), Vector3.UnitY),
            (MathF.Abs(entryPoint.Z - voxel.MinBounds.Z), -Vector3.UnitZ),
            (MathF.Abs(entryPoint.Z - voxel.MaxBounds.Z), Vector3.UnitZ)
        };

        float bestDistance = float.PositiveInfinity;
        Vector3 bestNormal = -direction;
        foreach ((float distance, Vector3 normal) in candidates)
        {
            // Prefer a surface facing the incoming projectile when a corner or
            // edge makes more than one plane equally close.
            if (distance < bestDistance - 1e-6f
                || (MathF.Abs(distance - bestDistance) <= 1e-6f
                    && Vector3.Dot(direction, normal) < Vector3.Dot(direction, bestNormal)))
            {
                bestDistance = distance;
                bestNormal = normal;
            }
        }

        return bestNormal;
    }

    private static void ValidateVector(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }
}

internal sealed record BodyVoxelReference(
    PhysiologicalVoxel Voxel,
    string StructureId,
    string BodyRegion,
    string StructureType);

internal sealed record VoxelRayIntersection(
    PhysiologicalVoxel Voxel,
    string StructureId,
    string BodyRegion,
    string StructureType,
    float EntryDistanceMeters,
    float ExitDistanceMeters,
    Vector3 EntryPointMeters,
    Vector3 ExitPointMeters,
    Vector3 EntrySurfaceNormal)
{
    public float PathLengthMeters => ExitDistanceMeters - EntryDistanceMeters;
}
