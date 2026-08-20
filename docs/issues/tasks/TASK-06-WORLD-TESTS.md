# Task 6 — Create World Tests

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Tasks 1, 2, 3 (World types must exist)  
**Estimated Effort**: Medium  
**File to create**: `TacticalSim.Tests/WorldTests.cs`

---

## What You're Building

A comprehensive test file covering `WorldBounds` geometry operations and `TacticalWorld` entity/cover management. These tests verify the correctness of the new world infrastructure.

## Step-by-Step Instructions

### Step 1: Create `WorldTests.cs`

Create the file `TacticalSim.Tests/WorldTests.cs` with the following test cases. Each test is provided with its complete implementation.

```csharp
using System;
using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.World;
using Xunit;

namespace TacticalSim.Tests
{
    public class WorldBoundsTests
    {
        [Fact]
        public void Constructor_ValidMinMax_CreatesBounds()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));

            Assert.Equal(new Vector3(-10, 0, -10), bounds.Min);
            Assert.Equal(new Vector3(10, 20, 10), bounds.Max);
        }

        [Fact]
        public void Constructor_MinNotLessThanMax_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new WorldBounds(new Vector3(10, 0, 0), new Vector3(5, 10, 10)));
        }

        [Fact]
        public void Constructor_EqualAxes_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new WorldBounds(new Vector3(0, 0, 0), new Vector3(10, 0, 10)));
        }

        [Fact]
        public void Constructor_NaNComponents_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new WorldBounds(new Vector3(float.NaN, 0, 0), new Vector3(10, 10, 10)));
        }

        [Fact]
        public void Constructor_InfinityComponents_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new WorldBounds(new Vector3(0, 0, 0), new Vector3(float.PositiveInfinity, 10, 10)));
        }

        [Fact]
        public void Size_ComputedCorrectly()
        {
            var bounds = new WorldBounds(new Vector3(-5, 0, -5), new Vector3(5, 10, 5));
            Assert.Equal(new Vector3(10, 10, 10), bounds.Size);
        }

        [Fact]
        public void Centre_ComputedCorrectly()
        {
            var bounds = new WorldBounds(new Vector3(-50, 0, -50), new Vector3(50, 30, 50));
            Assert.Equal(new Vector3(0, 15, 0), bounds.Centre);
        }

        [Fact]
        public void Volume_ComputedCorrectly()
        {
            var bounds = new WorldBounds(new Vector3(0, 0, 0), new Vector3(10, 10, 10));
            Assert.Equal(1000f, bounds.Volume);
        }

        [Fact]
        public void Contains_PointInside_ReturnsTrue()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));
            Assert.True(bounds.Contains(new Vector3(0, 10, 0)));
        }

        [Fact]
        public void Contains_PointOnBoundary_ReturnsTrue()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));
            Assert.True(bounds.Contains(new Vector3(-10, 0, -10)));  // Min corner
            Assert.True(bounds.Contains(new Vector3(10, 20, 10)));   // Max corner
        }

        [Fact]
        public void Contains_PointOutside_ReturnsFalse()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));
            Assert.False(bounds.Contains(new Vector3(11, 10, 0)));   // Beyond X
            Assert.False(bounds.Contains(new Vector3(0, -1, 0)));    // Below ground
            Assert.False(bounds.Contains(new Vector3(0, 10, 11)));   // Beyond Z
        }

        [Fact]
        public void Clamp_PointInside_ReturnsUnchanged()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));
            var point = new Vector3(5, 10, 3);
            Assert.Equal(point, bounds.Clamp(point));
        }

        [Fact]
        public void Clamp_PointOutside_ReturnsNearestBoundary()
        {
            var bounds = new WorldBounds(new Vector3(-10, 0, -10), new Vector3(10, 20, 10));

            Assert.Equal(new Vector3(10, 20, 10), bounds.Clamp(new Vector3(100, 100, 100)));
            Assert.Equal(new Vector3(-10, 0, -10), bounds.Clamp(new Vector3(-100, -100, -100)));
            Assert.Equal(new Vector3(5, 0, 3), bounds.Clamp(new Vector3(5, -5, 3)));  // Only Y clamped
        }

        [Fact]
        public void CreateDefault_ProducesExpectedBounds()
        {
            var bounds = WorldBounds.CreateDefault();

            Assert.Equal(new Vector3(-50, 0, -50), bounds.Min);
            Assert.Equal(new Vector3(50, 30, 50), bounds.Max);
            Assert.Equal(new Vector3(100, 30, 100), bounds.Size);
        }

        [Fact]
        public void Equality_SameBounds_AreEqual()
        {
            var a = new WorldBounds(new Vector3(-1, 0, -1), new Vector3(1, 1, 1));
            var b = new WorldBounds(new Vector3(-1, 0, -1), new Vector3(1, 1, 1));
            Assert.Equal(a, b);
            Assert.True(a == b);
        }

        [Fact]
        public void Equality_DifferentBounds_AreNotEqual()
        {
            var a = new WorldBounds(new Vector3(-1, 0, -1), new Vector3(1, 1, 1));
            var b = new WorldBounds(new Vector3(-2, 0, -2), new Vector3(2, 2, 2));
            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }
    }

    public class TacticalWorldTests
    {
        private static TacticalWorld CreateDefaultWorld() =>
            new TacticalWorld(WorldBounds.CreateDefault());

        private static TacticalEntity CreateEntity(Vector3 position) =>
            new TacticalEntity(position, new TacticalActorPhysiology());

        [Fact]
        public void AddEntity_StoresAndRetrieves()
        {
            var world = CreateDefaultWorld();
            var entity = CreateEntity(Vector3.Zero);

            world.AddEntity(entity);

            Assert.Equal(entity, world.GetEntity(entity.Id));
            Assert.Single(world.GetEntities());
        }

        [Fact]
        public void AddEntity_ClampsPositionToBounds()
        {
            var world = new TacticalWorld(new WorldBounds(
                new Vector3(-10, 0, -10), new Vector3(10, 20, 10)));
            var entity = CreateEntity(new Vector3(999, 999, 999));

            world.AddEntity(entity);

            Assert.Equal(new Vector3(10, 20, 10), entity.Position);
        }

        [Fact]
        public void AddEntity_NullEntity_Throws()
        {
            var world = CreateDefaultWorld();
            Assert.Throws<ArgumentNullException>(() => world.AddEntity(null!));
        }

        [Fact]
        public void RemoveEntity_ExistingEntity_ReturnsTrue()
        {
            var world = CreateDefaultWorld();
            var entity = CreateEntity(Vector3.Zero);
            world.AddEntity(entity);

            Assert.True(world.RemoveEntity(entity.Id));
            Assert.Null(world.GetEntity(entity.Id));
            Assert.Empty(world.GetEntities());
        }

        [Fact]
        public void RemoveEntity_NonExistent_ReturnsFalse()
        {
            var world = CreateDefaultWorld();
            Assert.False(world.RemoveEntity(Guid.NewGuid()));
        }

        [Fact]
        public void GetEntity_NonExistent_ReturnsNull()
        {
            var world = CreateDefaultWorld();
            Assert.Null(world.GetEntity(Guid.NewGuid()));
        }

        [Fact]
        public void SetEntityPosition_ClampsToWorldBounds()
        {
            var world = new TacticalWorld(new WorldBounds(
                new Vector3(-10, 0, -10), new Vector3(10, 20, 10)));
            var entity = CreateEntity(Vector3.Zero);
            world.AddEntity(entity);

            world.SetEntityPosition(entity.Id, new Vector3(100, -5, 100));

            Assert.Equal(new Vector3(10, 0, 10), entity.Position);
        }

        [Fact]
        public void SetEntityPosition_NonExistentEntity_Throws()
        {
            var world = CreateDefaultWorld();
            Assert.Throws<KeyNotFoundException>(() =>
                world.SetEntityPosition(Guid.NewGuid(), Vector3.Zero));
        }

        [Fact]
        public void EntityAdded_EventFires()
        {
            var world = CreateDefaultWorld();
            var entity = CreateEntity(Vector3.Zero);
            bool fired = false;
            world.EntityAdded += (s, e) => { fired = true; Assert.Equal(entity, e.Entity); };

            world.AddEntity(entity);

            Assert.True(fired);
        }

        [Fact]
        public void EntityRemoved_EventFires()
        {
            var world = CreateDefaultWorld();
            var entity = CreateEntity(Vector3.Zero);
            world.AddEntity(entity);
            bool fired = false;
            world.EntityRemoved += (s, e) => { fired = true; Assert.Equal(entity, e.Entity); };

            world.RemoveEntity(entity.Id);

            Assert.True(fired);
        }

        [Fact]
        public void AddCoverSurface_StoresAndRetrieves()
        {
            var world = CreateDefaultWorld();
            var cover = new CoverPolygon(
                new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
                0.2f,
                MaterialType.Wood);

            world.AddCoverSurface(cover);

            Assert.Single(world.GetCoverSurfaces());
            Assert.Equal(cover, world.GetCoverSurfaces()[0]);
        }

        [Fact]
        public void AddCoverSurface_NullCover_Throws()
        {
            var world = CreateDefaultWorld();
            Assert.Throws<ArgumentNullException>(() => world.AddCoverSurface(null!));
        }

        [Fact]
        public void GetEntities_ReturnsOrderedById()
        {
            var world = CreateDefaultWorld();
            var e1 = CreateEntity(Vector3.Zero);
            var e2 = CreateEntity(new Vector3(1, 0, 0));
            var e3 = CreateEntity(new Vector3(2, 0, 0));

            world.AddEntity(e3);
            world.AddEntity(e1);
            world.AddEntity(e2);

            var entities = world.GetEntities();
            var list = new System.Collections.Generic.List<IEntity>(entities);
            for (int i = 1; i < list.Count; i++)
            {
                Assert.True(list[i - 1].Id.CompareTo(list[i].Id) < 0,
                    "Entities should be ordered by Id.");
            }
        }
    }
}
```

### Step 2: Verify tests pass

> **Note**: These tests can only run after Tasks 1-3 are complete AND Task 4 (TurnResolver refactor) is complete (since `TacticalEntity` and `TacticalActorPhysiology` must compile). If you're running this task in isolation before Task 4, you can verify compilation with `dotnet build TacticalSim.Tests` but won't be able to run all tests until the full migration is done.

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet test --filter "FullyQualifiedName~WorldBoundsTests|FullyQualifiedName~TacticalWorldTests"
```

Expected: All world tests pass.

## Checklist

- [ ] File created at `TacticalSim.Tests/WorldTests.cs`
- [ ] `WorldBoundsTests` class covers: construction, validation, Contains, Clamp, CreateDefault, Size, Centre, Volume, equality
- [ ] `TacticalWorldTests` class covers: AddEntity, RemoveEntity, GetEntity, clamping, events, cover surfaces, ordering
- [ ] All tests pass
