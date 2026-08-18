# Project: TacticalSim Systems (Issues #3 & #4)

## Architecture
TacticalSim is a modular, high-fidelity tactical combat and ballistics simulation library written in C# (.NET 8.0).
The architecture separates core mathematical simulation models from external application/presentation layers:
1. **External & Terminal Ballistics (`TacticalSim.Core.Ballistics`, `TacticalSim.Core.Materials`)**:
   - Numerical trajectory integration (`BallisticSolver` RK4).
   - Atmospheric environment modeling (`IEnvironmentModel`, `ICAOStandardAtmosphere`).
   - Aerodynamic drag coefficient curves (`IDragModel`, `StandardDragCurve`).
   - Environmental cover material registry and terminal ballistics penetration calculations (`IMaterialRegistry`, `IMaterialPenetrationSystem`, `MaterialPenetrationSystem`).
2. **Physiological Trauma (`TacticalSim.Core.Physiology`)**:
   - Anatomical voxel representations and cavitation energy dissipation.
3. **Simultaneous Turn Resolution (`TacticalSim.Core.Simulation`)**:
   - Global timeline management (`GlobalTime`).
   - Concurrent action scheduling, execution lifecycle state machine (`Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`), fractionated TU sub-stepping, and carryover interleaving.
   - Decoupled observability via strongly typed event args.
4. **Architectural Decoupling & DI (`TacticalSim.Core.DependencyInjection`)**:
   - Decoupled service collection extension methods using `Microsoft.Extensions.DependencyInjection`.
5. **Testing Architecture (`TacticalSim.Tests`)**:
   - Multi-tier xUnit test suites for unit, combinatorial, end-to-end, and adversarial stress testing (232 tests total).

## Feature Inventory
| # | Feature | Description | Milestone | Status | Source |
|---|---------|-------------|-----------|:------:|--------|
| F1 | Global Simulation Timeline | Monotonically advancing simulation clock ($T_g \ge 0$) tracking global elapsed time | M1 | DONE | Survey / Issue #3 |
| F2 | Concurrent Multi-Entity Scheduling | Schedule and manage concurrent tactical actions across multiple unique actors (`Guid ActorId`) | M1 | DONE | Survey / Issue #3 |
| F3 | Fractionated TU Advancement & Sub-Stepping | Advance active actions in discrete fractional $\Delta t$ increments with sub-tick carryover into queued actions | M1 | DONE | Survey / Issue #3 |
| F4 | Tactical Action Lifecycle State Machine | State tracking (`Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`) with progress clamping and validation | M1 | DONE | Survey / Issue #3 |
| F5 | Turn Resolver Observability Events | Strongly-typed event hooks (`ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `ActionFailed`, `TimeAdvanced`) | M1 | DONE | Survey / Issue #3 |
| F6 | Environmental Cover Material Properties | Physical properties (`MaterialProperties`: density $\rho$, resistance factor $C_{res}$, ricochet threshold, yield energy) for Wood, Concrete, Steel, Glass, Drywall, Sand, Kevlar | M2 | DONE | Survey / Issue #4 |
| F7 | Material Registry | Registry service (`IMaterialRegistry`, `MaterialRegistry`) for querying and registering standard and custom cover materials | M2 | DONE | Survey / Issue #4 |
| F8 | Terminal Ballistics Penetration Physics | Compute effective thickness $T_{eff} = T_0 / \cos\theta$, drag work-energy loss $\Delta E_k = \min(F_d \cdot T_{eff}, E_{k0})$, exit velocity $v_{exit} = \sqrt{2 E_{rem} / m}$, and energy conservation | M2 | DONE | Survey / Issue #4 |
| F9 | Penetration Outcome Classification | Classification of terminal results (`Perforated`, `Stopped`, `Ricochet`, `Miss`) with exit velocity vector and updated `ProjectileState` | M2 | DONE | Survey / Issue #4 |
| F10 | Dependency Injection Service Registration | Extension methods (`AddTacticalSimCore`, `AddMaterialPenetration`, `AddSimulationServices`) registering all core simulation interfaces in `IServiceCollection` | M3 | DONE | Survey / R3 |
| F11 | Zero-Warning Codebase Hygiene | Resolve existing CS8618 warning on `BodyPart.Parent` in `ActorPhysiology.cs` and ensure zero build warnings | M3 | DONE | Survey / AC |
| F12 | Comprehensive E2E Test Suite | 4-tier requirement-driven test suite verifying concurrent turn interleaving, material penetration kinematics, and DI composition | Final | DONE | Survey / AC |

## Milestones
| # | Name | Scope | Dependencies | Status | Sub-Orchestrator Conv ID |
|---|------|-------|-------------|--------|--------------------------|
| M1 | Fractionated TU Turn Resolver | Implement `TurnResolver`, full `TacticalAction` lifecycle, action events, and unit tests in `TacticalSim.Core.Simulation` | none | DONE | 48dd6a7e-df75-497b-9b1c-3059e2e35976 |
| M2 | Material Penetration System | Implement `MaterialProperties`, `IMaterialRegistry`, `MaterialRegistry`, `IMaterialPenetrationSystem`, `MaterialPenetrationSystem`, and terminal ballistics tests | none | DONE | 70367ce3-513b-459b-8b98-3f3f494db93f |
| M3 | Dependency Injection & Zero-Warning Hygiene | Implement `ServiceCollectionExtensions` in `TacticalSim.Core.DependencyInjection`, fix CS8618 warning in `ActorPhysiology.cs`, DI tests | M1, M2 | DONE | 1c3a5603-34eb-40e4-8e97-2833154d24fa |
| E2E | E2E Testing Track | Multi-tier opaque-box test suite (Tiers 1-4) in `TacticalSim.Tests/E2ETacticalSimulationTests.cs` and `TEST_READY.md` | none | DONE | a76f9822-b64c-4a4e-a6f9-292e2fc2264e |
| Final | E2E Test Suite Pass & Adversarial Hardening | Verify 100% pass rate on E2E test suite (Tiers 1-4) and execute Tier 5 white-box adversarial stress tests | M1, M2, M3, E2E | DONE | 95c884b3-341e-40ee-8ef3-8fae93c1ade1 |

## Interface Contracts

### Simulation: `ITurnResolver` ↔ `TacticalAction`
- **`TacticalAction`**:
  ```csharp
  public abstract class TacticalAction {
      public Guid Id { get; set; }
      public Guid ActorId { get; set; }
      public float TUCost { get; set; }
      public float ExecutionProgress { get; set; }
      public TacticalActionState State { get; internal set; }
      public abstract void Execute(float dt);
      public bool IsComplete => State == TacticalActionState.Completed || ExecutionProgress >= TUCost;
  }
  ```
- **`ITurnResolver`**:
  ```csharp
  public interface ITurnResolver {
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
  ```

### Ballistics: `IMaterialPenetrationSystem` ↔ `IMaterialRegistry`
- **`MaterialProperties`**:
  ```csharp
  public struct MaterialProperties {
      public string Name { get; set; }
      public MaterialType Type { get; set; }
      public float Density { get; set; }               // kg/m^3
      public float ResistanceCoefficient { get; set; }  // multiplier
      public float RicochetAngleThreshold { get; set; } // radians
      public float YieldEnergyThreshold { get; set; }   // Joules
  }
  ```
- **`IMaterialRegistry`**:
  ```csharp
  public interface IMaterialRegistry {
      MaterialProperties GetMaterial(MaterialType type);
      MaterialProperties GetMaterial(string name);
      bool TryGetMaterial(string name, out MaterialProperties material);
      void RegisterMaterial(MaterialProperties material);
  }
  ```
- **`IMaterialPenetrationSystem`**:
  ```csharp
  public interface IMaterialPenetrationSystem {
      PenetrationResult CalculatePenetration(
          in ProjectileState projectile,
          in BallisticProfile profile,
          in MaterialProperties material,
          float nominalThickness,
          Vector3 surfaceNormal);
      PenetrationResult CalculatePenetration(
          in ProjectileState projectile,
          in BallisticProfile profile,
          in MaterialProperties material,
          Vector3 entryPoint,
          Vector3 exitPoint,
          Vector3 surfaceNormal);
  }
  ```

### DI Registration: `ServiceCollectionExtensions`
- `IServiceCollection AddTacticalSimCore(this IServiceCollection services)`
- `IServiceCollection AddMaterialPenetration(this IServiceCollection services)`
- `IServiceCollection AddSimulationServices(this IServiceCollection services)`

## Code Layout
```
TacticalSim.Core/
├── Ballistics/
│   ├── BallisticSolver.cs
│   ├── DragModels.cs
│   └── Environment.cs
├── Materials/
│   ├── MaterialType.cs
│   ├── MaterialProperties.cs
│   ├── IMaterialRegistry.cs
│   ├── MaterialRegistry.cs
│   ├── PenetrationOutcome.cs
│   ├── PenetrationResult.cs
│   ├── IMaterialPenetrationSystem.cs
│   └── MaterialPenetrationSystem.cs
├── Physiology/
│   ├── ActorPhysiology.cs
│   ├── PhysiologicalVoxel.cs
│   └── TissueRegistry.cs
├── Simulation/
│   ├── TacticalActionState.cs
│   ├── TacticalAction.cs
│   ├── ITurnResolver.cs
│   ├── TurnResolver.cs
│   ├── TurnResolverEvents.cs
│   └── Actions/
│       ├── GenericTacticalAction.cs
│       ├── MoveTacticalAction.cs
│       ├── AimTacticalAction.cs
│       └── WaitTacticalAction.cs
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs

TacticalSim.Tests/
├── BallisticSolverTests.cs
├── TurnResolverTests.cs
├── TurnResolverStressTests.cs
├── TurnResolverChallenger2Tests.cs
├── TurnResolverAdversarialTests.cs
├── MaterialPenetrationTests.cs
├── MaterialPenetrationAdversarialTests.cs
├── MaterialPenetrationStressTests.cs
├── DependencyInjectionTests.cs
├── DependencyInjectionStressTests.cs
├── E2ETacticalSimulationTests.cs
└── FinalAdversarialChallenger2Tests.cs
```
