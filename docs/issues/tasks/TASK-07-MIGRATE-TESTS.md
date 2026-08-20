# Task 7 — Migrate All Existing Tests

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Tasks 4 (TurnResolver refactored), 5 (DI updated)  
**Estimated Effort**: Large (but entirely mechanical)  
**Files to modify**: 9 test files + 1 DI test file + 1 architecture test file

---

## What You're Doing

Every test file that instantiates `new TurnResolver()` must be updated because `TurnResolver` now requires an `ITacticalWorld` parameter. Some files also call `resolver.RegisterEntity(...)` which must change to `world.AddEntity(...)`.

This is a **mechanical, repetitive refactor**. There is no design thinking required — just follow the pattern below and apply it to every file.

> **TIP**: Use your IDE's find-and-replace to speed this up. The patterns are consistent.

## The Pattern

Every test method follows one of two patterns. Here's how to migrate each:

### Pattern A: Tests that only create a `TurnResolver` (no entity registration)

**Before:**
```csharp
var resolver = new TurnResolver();
```

**After:**
```csharp
var world = new TacticalWorld(WorldBounds.CreateDefault());
var resolver = new TurnResolver(world);
```

### Pattern B: Tests that create a `TurnResolver` AND register entities

**Before:**
```csharp
var resolver = new TurnResolver();
var entity = new TacticalEntity(Vector3.Zero, new TacticalActorPhysiology());
resolver.RegisterEntity(entity);
```

**After:**
```csharp
var world = new TacticalWorld(WorldBounds.CreateDefault());
var resolver = new TurnResolver(world);
var entity = new TacticalEntity(Vector3.Zero, new TacticalActorPhysiology());
world.AddEntity(entity);
```

### Additional replacements within each file

| Old code | New code |
|---|---|
| `resolver.RegisterEntity(entity)` | `world.AddEntity(entity)` |
| `resolver.RegisterEntity(someVar)` | `world.AddEntity(someVar)` |
| `resolver.UnregisterEntity(id)` | `world.RemoveEntity(id)` |
| `resolver.GetEntity(id)` | `world.GetEntity(id)` |
| `resolver.GetRegisteredEntities()` | `world.GetEntities()` |
| `resolver.EntityRegistered += ...` | `world.EntityAdded += ...` |
| `resolver.EntityUnregistered += ...` | `world.EntityRemoved += ...` |

### Required `using` directives

Add this to the top of every modified test file (if not already present):

```csharp
using TacticalSim.Core.World;
```

---

## Files to Migrate

Work through each file in order. For each file, the table shows what operations are needed.

### File 1: `TurnResolverTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `RegisterEntity` calls | 0 |
| Other entity methods | 0 |

**Instructions**: Add `using TacticalSim.Core.World;`. Before each `new TurnResolver()`, add `var world = new TacticalWorld(WorldBounds.CreateDefault());`. Change `new TurnResolver()` to `new TurnResolver(world)`.

---

### File 2: `TurnResolverAdversarialTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `RegisterEntity` calls | 0 |
| Other entity methods | 0 |

**Instructions**: Same as File 1.

---

### File 3: `TurnResolverChallenger2Tests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `RegisterEntity` calls | 0 |
| Other entity methods | 0 |

**Instructions**: Same as File 1.

---

### File 4: `TurnResolverStressTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `RegisterEntity` calls | 0 |
| Other entity methods | 0 |

**Instructions**: Same as File 1.

---

### File 5: `E2ETacticalSimulationTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `RegisterEntity` calls | 0 |
| Other entity methods | 0 |

**Instructions**: Same as File 1.

---

### File 6: `TurnResolverPhysiologyTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `resolver.RegisterEntity(...)` → `world.AddEntity(...)` | Yes |
| `resolver.GetEntity(...)` → `world.GetEntity(...)` | Yes |
| `resolver.GetRegisteredEntities()` → `world.GetEntities()` | Yes |
| `resolver.EntityRegistered` → `world.EntityAdded` | Yes |
| `resolver.EntityUnregistered` → `world.EntityRemoved` | Yes |

**Instructions**: This file has the full set of changes. Add `using TacticalSim.Core.World;`. Create `var world = ...` before each resolver. Replace all entity method calls as per the table above.

---

### File 7: `TurnResolverE2ETieredTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `resolver.RegisterEntity(...)` → `world.AddEntity(...)` | Yes |
| `resolver.GetEntity(...)` → `world.GetEntity(...)` | Yes |
| `resolver.GetRegisteredEntities()` → `world.GetEntities()` | Yes |
| `resolver.EntityRegistered` → `world.EntityAdded` | Yes |
| `resolver.EntityUnregistered` → `world.EntityRemoved` | Yes |

**Instructions**: Same as File 6.

---

### File 8: `TurnResolverEmpiricalChallengerTests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `resolver.RegisterEntity(...)` → `world.AddEntity(...)` | Yes |
| `resolver.GetEntity(...)` → `world.GetEntity(...)` | Yes |

**Instructions**: Add `using TacticalSim.Core.World;`. Create `var world = ...` before each resolver. Replace entity method calls.

---

### File 9: `PhysiologyIntegrationChallenger2Tests.cs`

| Operation | Count |
|---|---|
| `new TurnResolver()` → `new TurnResolver(world)` | Many |
| `resolver.RegisterEntity(...)` → `world.AddEntity(...)` | Yes |
| `resolver.GetEntity(...)` → `world.GetEntity(...)` | Yes |
| `resolver.UnregisterEntity(...)` → `world.RemoveEntity(...)` | Yes |

**Instructions**: Add `using TacticalSim.Core.World;`. Create `var world = ...` before each resolver. Replace entity method calls.

> **IMPORTANT**: Some tests in this file create multiple resolvers (e.g. `macroResolver` and `microResolver`). Each resolver needs its **own** world instance:
> ```csharp
> var macroWorld = new TacticalWorld(WorldBounds.CreateDefault());
> var macroResolver = new TurnResolver(macroWorld);
> var microWorld = new TacticalWorld(WorldBounds.CreateDefault());
> var microResolver = new TurnResolver(microWorld);
> ```
> Then use `macroWorld.AddEntity(...)` and `microWorld.AddEntity(...)` respectively.

---

### File 10: `DependencyInjectionTests.cs`

Add a new test verifying `ITacticalWorld` resolves from the DI container:

```csharp
[Fact]
public void ITacticalWorld_ResolvesFromContainer()
{
    var services = new ServiceCollection();
    services.AddTacticalSimCore();
    var sp = services.BuildServiceProvider();

    var world = sp.GetService<ITacticalWorld>();
    Assert.NotNull(world);
}

[Fact]
public void ITacticalWorld_DefaultBounds_AreCorrect()
{
    var services = new ServiceCollection();
    services.AddTacticalSimCore();
    var sp = services.BuildServiceProvider();

    var world = sp.GetRequiredService<ITacticalWorld>();
    Assert.Equal(new Vector3(-50, 0, -50), world.Bounds.Min);
    Assert.Equal(new Vector3(50, 30, 50), world.Bounds.Max);
}
```

Also add the required using:
```csharp
using TacticalSim.Core.World;
```

If any existing DI tests resolve `ITurnResolver` via `sp.GetService<ITurnResolver>()`, those should still work because the DI registration (Task 5) wires up the `ITacticalWorld` dependency automatically.

---

### File 11: `ArchitectureTests.cs`

The existing architecture tests will automatically cover the new `TacticalSim.Core.World` namespace types because they inspect the entire `TacticalSim.Core` assembly via reflection. No changes needed unless you want to add an explicit test.

**Optional**: Add a test that explicitly verifies the `TacticalSim.Core.World` namespace exists:

```csharp
[Fact]
public void WorldNamespace_ExistsInCoreAssembly()
{
    var worldTypes = CoreAssembly.GetTypes()
        .Where(t => t.Namespace?.StartsWith("TacticalSim.Core.World") == true)
        .ToList();

    Assert.True(worldTypes.Count >= 3, 
        "Expected at least WorldBounds, ITacticalWorld, and TacticalWorld in the World namespace.");
}
```

---

## Verification

After migrating ALL files, run:

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build
dotnet test
```

**Expected**:
- `dotnet build` exits with code 0, zero errors, zero warnings
- `dotnet test` passes all tests (416+ existing + new world tests)

## Troubleshooting

### "TurnResolver does not contain a definition for RegisterEntity"
You missed replacing `resolver.RegisterEntity(...)` with `world.AddEntity(...)` somewhere. Search the file for `RegisterEntity`.

### "'TurnResolver' does not contain a parameterless constructor"  
You missed creating a `world` variable before the `new TurnResolver()` call. Search for `new TurnResolver()` (with empty parens) — they should ALL be `new TurnResolver(world)`.

### "The name 'world' does not exist in the current context"
You need to add `var world = new TacticalWorld(WorldBounds.CreateDefault());` before the `new TurnResolver(world)` line in that specific test method.

### "The type or namespace name 'TacticalWorld' could not be found"
You're missing `using TacticalSim.Core.World;` at the top of the file.

## Checklist

- [ ] `using TacticalSim.Core.World;` added to all 9 test files
- [ ] All `new TurnResolver()` calls changed to `new TurnResolver(world)`
- [ ] All `resolver.RegisterEntity(...)` changed to `world.AddEntity(...)`
- [ ] All `resolver.UnregisterEntity(...)` changed to `world.RemoveEntity(...)`
- [ ] All `resolver.GetEntity(...)` changed to `world.GetEntity(...)`
- [ ] All `resolver.GetRegisteredEntities()` changed to `world.GetEntities()`
- [ ] All `resolver.EntityRegistered` changed to `world.EntityAdded`
- [ ] All `resolver.EntityUnregistered` changed to `world.EntityRemoved`
- [ ] DI tests updated with `ITacticalWorld` resolution tests
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` passes all tests
