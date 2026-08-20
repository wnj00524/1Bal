# Task 2 — Create `ITacticalWorld` Interface

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Task 1 (WorldBounds must exist)  
**Estimated Effort**: Small  
**File to create**: `TacticalSim.Core/World/ITacticalWorld.cs`

---

## What You're Building

The interface contract for the simulation world container. This defines the API that all world implementations must provide. It follows the same interface-based pattern used throughout the project (`ITurnResolver`, `IEntity`, `IActorPhysiology`).

## Step-by-Step Instructions

### Step 1: Create `ITacticalWorld.cs`

Create the file `TacticalSim.Core/World/ITacticalWorld.cs` with the following code:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;

namespace TacticalSim.Core.World
{
    /// <summary>
    /// Interface for the bounded 3D simulation world.
    /// Serves as the single spatial authority for entity and cover surface management.
    /// </summary>
    public interface ITacticalWorld
    {
        /// <summary>
        /// The axis-aligned bounding box defining the world extents.
        /// </summary>
        WorldBounds Bounds { get; }

        // ── Entity Management ──────────────────────────────────────────

        /// <summary>
        /// Adds an entity to the world. The entity's position is clamped to world bounds.
        /// </summary>
        /// <param name="entity">The entity to add. Must not be null and must have a non-empty Id.</param>
        /// <exception cref="ArgumentNullException">If entity is null.</exception>
        /// <exception cref="ArgumentException">If entity Id is empty.</exception>
        void AddEntity(IEntity entity);

        /// <summary>
        /// Removes an entity from the world by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to remove.</param>
        /// <returns>True if the entity was found and removed; otherwise false.</returns>
        bool RemoveEntity(Guid entityId);

        /// <summary>
        /// Gets a registered entity by ID, or null if not found.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve.</param>
        /// <returns>The entity, or null if not found.</returns>
        IEntity? GetEntity(Guid entityId);

        /// <summary>
        /// Gets all entities currently in the world.
        /// </summary>
        /// <returns>A readonly collection of all entities, ordered by ID for determinism.</returns>
        IReadOnlyCollection<IEntity> GetEntities();

        /// <summary>
        /// Updates an entity's position, clamping to world bounds.
        /// </summary>
        /// <param name="entityId">The ID of the entity to move.</param>
        /// <param name="newPosition">The desired new position (will be clamped to bounds).</param>
        /// <exception cref="KeyNotFoundException">If no entity with the given ID exists.</exception>
        void SetEntityPosition(Guid entityId, Vector3 newPosition);

        // ── Cover Surface Management ──────────────────────────────────

        /// <summary>
        /// Adds a cover surface (wall, floor, fence, etc.) to the world.
        /// </summary>
        /// <param name="cover">The cover polygon to add.</param>
        void AddCoverSurface(CoverPolygon cover);

        /// <summary>
        /// Gets all cover surfaces currently in the world.
        /// </summary>
        IReadOnlyList<CoverPolygon> GetCoverSurfaces();

        // ── Events ────────────────────────────────────────────────────

        /// <summary>
        /// Fired when an entity is added to the world.
        /// </summary>
        event EventHandler<EntityEventArgs>? EntityAdded;

        /// <summary>
        /// Fired when an entity is removed from the world.
        /// </summary>
        event EventHandler<EntityEventArgs>? EntityRemoved;
    }
}
```

> **IMPORTANT**: The `EntityEventArgs` class currently lives in `TacticalSim.Core.Simulation.TurnResolverEvents.cs`. You need to add a `using TacticalSim.Core.Simulation;` directive to access it. We are reusing this existing class rather than creating a duplicate.

So the final using block should be:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;
```

### Step 2: Verify it compiles

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build TacticalSim.Core
```

Expected: Build succeeds with 0 errors.

## Key Design Notes

- This interface mirrors the entity methods that currently exist on `ITurnResolver` (but renamed: `RegisterEntity` → `AddEntity`, `UnregisterEntity` → `RemoveEntity`, etc.).
- `SetEntityPosition` is new — it provides a world-level API for moving entities with bounds clamping.
- `AddCoverSurface` allows the world to hold static geometry (walls, floors, fences, etc.).
- Events use the existing `EntityEventArgs` class — no need to create new event types.

## Checklist

- [ ] File created at `TacticalSim.Core/World/ITacticalWorld.cs`
- [ ] Namespace is `TacticalSim.Core.World`
- [ ] Uses `EntityEventArgs` from `TacticalSim.Core.Simulation`
- [ ] `dotnet build TacticalSim.Core` succeeds
- [ ] No references to Godot, UI, or rendering libraries
