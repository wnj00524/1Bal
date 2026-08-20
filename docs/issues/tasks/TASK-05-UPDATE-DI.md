# Task 5 — Update Dependency Injection Registration

**Issue**: [Issue #5 — Bounded 3D World](../ISSUE-005-BOUNDED-3D-WORLD.md)  
**Dependencies**: Task 4 (TurnResolver refactored)  
**Estimated Effort**: Small  
**File to modify**: `TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`

---

## What You're Doing

Updating the dependency injection registration so that `ITacticalWorld` is available in the DI container, and `TurnResolver` receives it when resolved.

## Step-by-Step Instructions

### Step 1: Open `ServiceCollectionExtensions.cs`

File path: `TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`

### Step 2: Add using directive

Add this import at the top of the file:

```csharp
using TacticalSim.Core.World;
```

### Step 3: Update `AddSimulationServices` method

Find this method:

```csharp
public static IServiceCollection AddSimulationServices(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddTransient<ITurnResolver, TurnResolver>();

    return services;
}
```

**Replace it with**:

```csharp
public static IServiceCollection AddSimulationServices(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddSingleton<ITacticalWorld>(sp => new TacticalWorld(WorldBounds.CreateDefault()));
    services.AddTransient<ITurnResolver>(sp => new TurnResolver(sp.GetRequiredService<ITacticalWorld>()));

    return services;
}
```

**What changed:**
1. Added `ITacticalWorld` registration as a **Singleton** using the default 100m × 100m × 30m bounds.
2. Changed `ITurnResolver` registration to use a **factory** that resolves `ITacticalWorld` and passes it to the `TurnResolver` constructor.

### Step 4: Verify Core builds

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build TacticalSim.Core
```

Expected: Build succeeds with 0 errors.

## Checklist

- [ ] `using TacticalSim.Core.World;` added to imports
- [ ] `ITacticalWorld` registered as Singleton with `WorldBounds.CreateDefault()`
- [ ] `ITurnResolver` registered with factory that injects `ITacticalWorld`
- [ ] `dotnet build TacticalSim.Core` succeeds with 0 errors
