# Task 1 — Create `WorldBounds` Value Type

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: None  
**Estimated Effort**: Small  
**File to create**: `TacticalSim.Core/World/WorldBounds.cs`

---

## What You're Building

A lightweight readonly struct that represents an axis-aligned bounding box (AABB) in 3D space. This is the fundamental geometric primitive that defines the extents of the simulation world.

## Step-by-Step Instructions

### Step 1: Create the directory

Create a new folder: `TacticalSim.Core/World/`

### Step 2: Create `WorldBounds.cs`

Create the file `TacticalSim.Core/World/WorldBounds.cs` with the following code:

```csharp
using System;
using System.Numerics;

namespace TacticalSim.Core.World
{
    /// <summary>
    /// An axis-aligned bounding box (AABB) defining the extents of the simulation world.
    /// All coordinates are in metres. Y-up convention: Y=0 is ground level.
    /// </summary>
    public readonly struct WorldBounds : IEquatable<WorldBounds>
    {
        /// <summary>
        /// Minimum corner of the bounding box (most negative X, Y, Z).
        /// </summary>
        public Vector3 Min { get; }

        /// <summary>
        /// Maximum corner of the bounding box (most positive X, Y, Z).
        /// </summary>
        public Vector3 Max { get; }

        /// <summary>
        /// Size of the bounding box on each axis (Max - Min).
        /// </summary>
        public Vector3 Size { get; }

        /// <summary>
        /// Geometric centre of the bounding box.
        /// </summary>
        public Vector3 Centre { get; }

        /// <summary>
        /// Total volume of the bounding box in cubic metres.
        /// </summary>
        public float Volume { get; }

        /// <summary>
        /// Creates a new world bounding box.
        /// </summary>
        /// <param name="min">Minimum corner. Must be strictly less than max on all axes.</param>
        /// <param name="max">Maximum corner. Must be strictly greater than min on all axes.</param>
        /// <exception cref="ArgumentException">Thrown if min >= max on any axis, or if any component is NaN/Infinity.</exception>
        public WorldBounds(Vector3 min, Vector3 max)
        {
            if (!IsFinite(min))
                throw new ArgumentException("Min must have finite components.", nameof(min));
            if (!IsFinite(max))
                throw new ArgumentException("Max must have finite components.", nameof(max));
            if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
                throw new ArgumentException($"Min ({min}) must be strictly less than Max ({max}) on all axes.");

            Min = min;
            Max = max;
            Size = max - min;
            Centre = (min + max) * 0.5f;
            Volume = Size.X * Size.Y * Size.Z;
        }

        /// <summary>
        /// Returns true if the given point is inside or on the boundary of this box.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X
                && point.Y >= Min.Y && point.Y <= Max.Y
                && point.Z >= Min.Z && point.Z <= Max.Z;
        }

        /// <summary>
        /// Clamps a point to the nearest position inside or on the boundary of this box.
        /// If the point is already inside, returns it unchanged.
        /// </summary>
        public Vector3 Clamp(Vector3 point)
        {
            return new Vector3(
                MathF.Max(Min.X, MathF.Min(Max.X, point.X)),
                MathF.Max(Min.Y, MathF.Min(Max.Y, point.Y)),
                MathF.Max(Min.Z, MathF.Min(Max.Z, point.Z))
            );
        }

        /// <summary>
        /// Creates the default world bounds for a UK detached house scenario.
        /// 100m x 100m x 30m, centred on XZ origin, ground at Y=0.
        /// Bounds: (-50, 0, -50) to (50, 30, 50)
        /// </summary>
        public static WorldBounds CreateDefault()
        {
            return new WorldBounds(
                new Vector3(-50f, 0f, -50f),
                new Vector3(50f, 30f, 50f)
            );
        }

        public bool Equals(WorldBounds other) => Min == other.Min && Max == other.Max;
        public override bool Equals(object? obj) => obj is WorldBounds other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Min, Max);
        public static bool operator ==(WorldBounds left, WorldBounds right) => left.Equals(right);
        public static bool operator !=(WorldBounds left, WorldBounds right) => !left.Equals(right);
        public override string ToString() => $"WorldBounds(Min={Min}, Max={Max}, Size={Size})";

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
```

### Step 3: Verify it compiles

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build TacticalSim.Core
```

Expected: Build succeeds with 0 errors.

## Key Design Notes

- This is a `readonly struct` (value type) — it's immutable and cheap to copy.
- Uses `System.Numerics.Vector3` — NOT Godot's Vector3. This is critical for decoupling.
- The `CreateDefault()` factory method produces the standard 100m × 100m × 30m bounds for the house scenario.
- The `Contains` and `Clamp` methods are the primary query operations other code will use.
- `IEquatable<WorldBounds>` is implemented for proper value comparison in tests.

## Checklist

- [ ] File created at `TacticalSim.Core/World/WorldBounds.cs`
- [ ] Namespace is `TacticalSim.Core.World`
- [ ] `dotnet build TacticalSim.Core` succeeds
- [ ] No references to Godot, UI, or rendering libraries
