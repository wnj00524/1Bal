# Task 4 — Refactor `ITurnResolver` and `TurnResolver`

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Task 3 (TacticalWorld must exist)  
**Estimated Effort**: Medium  
**Files to modify**: `TacticalSim.Core/Simulation/ITurnResolver.cs`, `TacticalSim.Core/Simulation/TurnResolver.cs`

---

## What You're Doing

Removing entity management from the turn resolver. Entity storage is now the responsibility of `TacticalWorld`. The `TurnResolver` will receive an `ITacticalWorld` dependency and query it for entities during `Tick()`.

> **⚠️ WARNING**: This is a breaking API change. After this task, the code will NOT compile until Task 5 (DI) and Task 7 (test migration) are also completed. That's expected.

## Step-by-Step Instructions

### Step 1: Modify `ITurnResolver.cs`

Open `TacticalSim.Core/Simulation/ITurnResolver.cs`.

**Remove these 6 members** from the interface:

```csharp
// DELETE these lines:
void RegisterEntity(IEntity entity);
bool UnregisterEntity(Guid entityId);
IReadOnlyCollection<IEntity> GetRegisteredEntities();
IEntity? GetEntity(Guid entityId);
event EventHandler<EntityEventArgs>? EntityRegistered;
event EventHandler<EntityEventArgs>? EntityUnregistered;
```

**Also remove** the XML doc comments above each of those members.

**Also remove** the `using TacticalSim.Core.Entities;` import if it becomes unused (it will — check for any remaining references first). The `using System.Collections.Generic;` import may also become unused — remove it if so.

The remaining interface should look like this:

```csharp
using System;

namespace TacticalSim.Core.Simulation
{
    public interface ITurnResolver
    {
        float GlobalTime { get; }
        bool HasActiveActions { get; }
        int ActiveActorCount { get; }

        void ScheduleAction(TacticalAction action);
        bool CancelAction(Guid actionId);
        int CancelActorActions(Guid actorId);
        IReadOnlyList<TacticalAction> GetActiveActions();
        IReadOnlyList<TacticalAction> GetQueuedActions(Guid actorId);
        TacticalAction? GetCurrentAction(Guid actorId);

        void Tick(float dt);
        void Reset();

        event EventHandler<ActionEventArgs>? ActionScheduled;
        event EventHandler<ActionEventArgs>? ActionStarted;
        event EventHandler<ActionProgressEventArgs>? ActionProgressed;
        event EventHandler<ActionEventArgs>? ActionCompleted;
        event EventHandler<ActionEventArgs>? ActionCancelled;
        event EventHandler<ActionFailedEventArgs>? ActionFailed;
        event EventHandler<TimeAdvancedEventArgs>? TimeAdvanced;
    }
}
```

Note: You will still need `using System.Collections.Generic;` for `IReadOnlyList<>`.

### Step 2: Modify `TurnResolver.cs`

Open `TacticalSim.Core/Simulation/TurnResolver.cs`.

#### 2a. Add `using` for the world namespace

Add this to the top of the file:

```csharp
using TacticalSim.Core.World;
```

#### 2b. Add constructor with `ITacticalWorld` dependency

Replace the field declarations at the top of the class. 

**Remove** these lines:
```csharp
private readonly Dictionary<Guid, IEntity> _registeredEntities = new();
```

**Add** this field and constructor:
```csharp
private readonly ITacticalWorld _world;

public TurnResolver(ITacticalWorld world)
{
    _world = world ?? throw new ArgumentNullException(nameof(world));
}
```

#### 2c. Remove entity management methods

**Delete** the following methods entirely (including their XML doc comments):
- `RegisterEntity(IEntity entity)`
- `UnregisterEntity(Guid entityId)`  
- `GetRegisteredEntities()`
- `GetEntity(Guid entityId)`

**Delete** these event declarations:
```csharp
public event EventHandler<EntityEventArgs>? EntityRegistered;
public event EventHandler<EntityEventArgs>? EntityUnregistered;
```

#### 2d. Update the `Tick()` method

In the `Tick(float dt)` method, find this block (around line 282-292):

```csharp
// Advance physiology for all registered entities in deterministic order (by Id)
var entities = _registeredEntities.Values.OrderBy(e => e.Id).ToList();
foreach (var entity in entities)
{
    entity.Physiology?.TickPhysiology(dt);

    if (entity.Physiology != null && entity.Physiology.ConsciousnessLevel <= 0f)
    {
        CancelActorActions(entity.Id);
    }
}
```

**Replace it with**:

```csharp
// Advance physiology for all registered entities in deterministic order (by Id)
var entities = _world.GetEntities().OrderBy(e => e.Id).ToList();
foreach (var entity in entities)
{
    entity.Physiology?.TickPhysiology(dt);

    if (entity.Physiology != null && entity.Physiology.ConsciousnessLevel <= 0f)
    {
        CancelActorActions(entity.Id);
    }
}
```

The only change is `_registeredEntities.Values` → `_world.GetEntities()`.

#### 2e. Update the `Reset()` method

Find the `Reset()` method:

```csharp
public void Reset()
{
    _globalTime = 0.0f;
    _activeActions.Clear();
    _actorQueues.Clear();
    _registeredEntities.Clear();
}
```

**Remove** the `_registeredEntities.Clear();` line. The final method should be:

```csharp
public void Reset()
{
    _globalTime = 0.0f;
    _activeActions.Clear();
    _actorQueues.Clear();
}
```

#### 2f. Clean up unused imports

The `using TacticalSim.Core.Entities;` import at the top of the file may now be unused. Check and remove if so.

### Step 3: Verify the Core project builds

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build TacticalSim.Core
```

Expected: Build succeeds with 0 errors. (The test project WILL fail at this point — that's expected and will be fixed in Task 7.)

## Checklist

- [ ] `ITurnResolver` no longer contains `RegisterEntity`, `UnregisterEntity`, `GetEntity`, `GetRegisteredEntities`, `EntityRegistered`, `EntityUnregistered`
- [ ] `TurnResolver` has a constructor accepting `ITacticalWorld`
- [ ] `TurnResolver` no longer has `_registeredEntities` dictionary
- [ ] `TurnResolver.Tick()` queries `_world.GetEntities()` for physiology ticking
- [ ] `TurnResolver.Reset()` no longer clears entities
- [ ] `dotnet build TacticalSim.Core` succeeds with 0 errors
