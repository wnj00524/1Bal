# Task 3 — Create `TacticalWorld` Implementation

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Task 1 (WorldBounds), Task 2 (ITacticalWorld)  
**Estimated Effort**: Medium  
**File to create**: `TacticalSim.Core/World/TacticalWorld.cs`

---

## What You're Building

The concrete implementation of `ITacticalWorld`. This is the simulation's single spatial authority — it owns entities, enforces world bounds, and manages cover surfaces.

## Step-by-Step Instructions

### Step 1: Create `TacticalWorld.cs`

Create the file `TacticalSim.Core/World/TacticalWorld.cs` with the following code:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;

namespace TacticalSim.Core.World
{
    /// <summary>
    /// The bounded 3D simulation world. Manages entities and cover surfaces
    /// within defined spatial extents.
    /// </summary>
    public class TacticalWorld : ITacticalWorld
    {
        private readonly Dictionary<Guid, IEntity> _entities = new();
        private readonly List<CoverPolygon> _coverSurfaces = new();

        /// <summary>
        /// Creates a new tactical world with the given bounds.
        /// </summary>
        /// <param name="bounds">The AABB defining the world extents.</param>
        public TacticalWorld(WorldBounds bounds)
        {
            Bounds = bounds;
        }

        /// <inheritdoc />
        public WorldBounds Bounds { get; }

        // ── Events ────────────────────────────────────────────────────

        /// <inheritdoc />
        public event EventHandler<EntityEventArgs>? EntityAdded;

        /// <inheritdoc />
        public event EventHandler<EntityEventArgs>? EntityRemoved;

        // ── Entity Management ─────────────────────────────────────────

        /// <inheritdoc />
        public void AddEntity(IEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (entity.Id == Guid.Empty)
            {
                throw new ArgumentException("Entity Id cannot be empty.", nameof(entity));
            }

            // Clamp position to world bounds
            entity.Position = Bounds.Clamp(entity.Position);

            _entities[entity.Id] = entity;
            EntityAdded?.Invoke(this, new EntityEventArgs(entity, 0f));
        }

        /// <inheritdoc />
        public bool RemoveEntity(Guid entityId)
        {
            if (entityId == Guid.Empty)
            {
                return false;
            }

            if (_entities.Remove(entityId, out var entity))
            {
                EntityRemoved?.Invoke(this, new EntityEventArgs(entity, 0f));
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public IEntity? GetEntity(Guid entityId)
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity : null;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<IEntity> GetEntities()
        {
            return _entities.Values.OrderBy(e => e.Id).ToList().AsReadOnly();
        }

        /// <inheritdoc />
        public void SetEntityPosition(Guid entityId, Vector3 newPosition)
        {
            if (!_entities.TryGetValue(entityId, out var entity))
            {
                throw new KeyNotFoundException($"No entity found with ID '{entityId}'.");
            }

            entity.Position = Bounds.Clamp(newPosition);
        }

        // ── Cover Surface Management ──────────────────────────────────

        /// <inheritdoc />
        public void AddCoverSurface(CoverPolygon cover)
        {
            ArgumentNullException.ThrowIfNull(cover);
            _coverSurfaces.Add(cover);
        }

        /// <inheritdoc />
        public IReadOnlyList<CoverPolygon> GetCoverSurfaces()
        {
            return _coverSurfaces.AsReadOnly();
        }
    }
}
```

### Step 2: Verify it compiles

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build TacticalSim.Core
```

Expected: Build succeeds with 0 errors.

## Key Design Notes

- **Bounds clamping**: Both `AddEntity` and `SetEntityPosition` clamp positions to world bounds. This means if you add an entity at `(999, 999, 999)` and the world max is `(50, 30, 50)`, the entity's position becomes `(50, 30, 50)`.
- **Deterministic ordering**: `GetEntities()` returns entities ordered by `Id` (GUID), matching the existing pattern in `TurnResolver`.
- **Entity events**: We reuse `EntityEventArgs` from `TurnResolverEvents.cs`. The timestamp is `0f` because the world doesn't track simulation time — that's the `TurnResolver`'s job.
- **Thread safety**: Not thread-safe, matching the existing `TurnResolver` pattern. The simulation runs single-threaded.
- **No `Reset()`**: Entity lifecycle is managed by explicit `AddEntity`/`RemoveEntity` calls.

## Checklist

- [ ] File created at `TacticalSim.Core/World/TacticalWorld.cs`
- [ ] Namespace is `TacticalSim.Core.World`
- [ ] `AddEntity` clamps position to bounds
- [ ] `SetEntityPosition` clamps position to bounds
- [ ] `GetEntities()` returns entities ordered by ID
- [ ] `dotnet build TacticalSim.Core` succeeds
- [ ] No references to Godot, UI, or rendering libraries
