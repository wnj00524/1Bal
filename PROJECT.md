# Project: TacticalSim - Issue #3 Fractionated TU Turn Resolver & Physiological Integration

## Architecture
TacticalSim is a high-fidelity decoupled tactical simulation engine targeting .NET 8.0 with `System.Numerics.Vector3` mathematics and `Microsoft.Extensions.DependencyInjection` service composition.

### Core Architecture Components:
1. **`TacticalSim.Core.Simulation`**:
   - `ITurnResolver` / `TurnResolver`: Simultaneous timeline manager managing monotonic global clock ($T_g$), concurrent multi-actor action scheduling, per-actor FIFO queues, sub-tick fractionated TU carryover interleaving, fault isolation, and simulation lifecycle events.
   - Entity & Physiology Registration: `RegisterEntity(IEntity)`, `UnregisterEntity(Guid)`, `GetRegisteredEntities()`, `GetEntity(Guid)` to track active entities.
   - Simultaneous Physiological Progression: During `TurnResolver.Tick(float dt)`, advances `IActorPhysiology.TickPhysiology(dt)` for all active registered entities, updating hemorrhage blood loss, tourniquet ischemia, and cardiovascular state.
   - Automatic Incapacitation Cancellation: Automatically cancels active and queued actions if an actor's `ConsciousnessLevel <= 0f` (fatal trauma / acute decompensation).
   - Concrete Actions: `GenericTacticalAction`, `MoveTacticalAction`, `AimTacticalAction`, `ShootTacticalAction`, `WaitTacticalAction`.
2. **`TacticalSim.Core.Entities`**:
   - `IEntity` / `TacticalEntity`: Contracts and classes binding entity identity (`Guid Id`), spatial coordinates (`Vector3 Position`), biological state (`IActorPhysiology Physiology`), and optional weaponry.
3. **`TacticalSim.Core.DependencyInjection`**:
   - `ServiceCollectionExtensions`: Registers `ITurnResolver` -> `TurnResolver` (Transient), ballistics models, material penetration systems, and core dependencies.
4. **`TacticalSim.Tests`**:
   - Comprehensive xUnit test suite (392 tests) validating timeline invariants, concurrent multi-actor interleaving, sub-tick carryover, failure isolation, physiological bleeding/ischemia integration, stress, and dependency injection.

---

## Feature Inventory

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Global Simulation Timeline | Monotonically advancing simulation clock ($T_g \ge 0$) tracking global elapsed time across all entities. | M1 | `ORIGINAL_REQUEST.md` R1, `ITurnResolver.cs` |
| 2 | Timeline Observability Events | Strongly-typed event notifications (`TimeAdvanced`, `ActionScheduled`, `ActionStarted`, etc.) dispatched on state changes. | M1 | `TurnResolverEvents.cs` |
| 3 | Timeline & Resolver Reset | Clears active actions, queued actions, registered entities, and resets global timeline to 0.0. | M1 | `ITurnResolver.cs` |
| 4 | Concurrent Multi-Actor Scheduling | Enqueues tactical actions per actor (`Guid ActorId`). If idle, activates immediately; otherwise enqueues in FIFO order. | M1 | `ORIGINAL_REQUEST.md` R1 |
| 5 | Per-Actor FIFO Queuing | Maintains isolated FIFO action queues per entity ID for sequential multi-step tactical plans. | M1 | `ITurnResolver.cs` |
| 6 | Active & Queued State Inspection | Inspection methods for active actions, queued actions, current action per actor, and actor counts. | M1 | `ITurnResolver.cs` |
| 7 | Fractionated TU Sub-Stepping | Advances active actions in discrete fractional $\Delta t$ increments, allowing arbitrary fine-grained time slicing. | M1 | `ORIGINAL_REQUEST.md` R1 |
| 8 | Sub-Tick Carryover Interleaving | When an action completes within $\Delta t$, unused remainder immediately promotes and executes next queued action in the SAME tick. | M1 | `ORIGINAL_REQUEST.md` R1 |
| 9 | Multi-Actor Concurrency & Determinism | Multiple actors progress simultaneously with canonical Guid/ID deterministic ordering. | M1 | `TurnResolver.cs` |
| 10 | Action Lifecycle State Machine | Full state tracking: `Pending` -> `Executing` -> `Completed` / `Cancelled` / `Failed`. | M1 | `TacticalActionState.cs` |
| 11 | Action Cancellation (Single & Bulk) | Immediate cancellation of active/queued actions with automatic queue promotion. | M1 | `ITurnResolver.cs` |
| 12 | Fault Isolation & Failure State | Isolated exception handling; failing actions do not crash the simulation loop or corrupt other actors. | M1 | `TurnResolver.cs` |
| 13 | Entity Registration in TurnResolver | Methods to register, unregister, and query active simulation entities (`IEntity`) in `ITurnResolver`. | M1 | `ORIGINAL_REQUEST.md` R2 |
| 14 | Physiological Ticking Integration | `TurnResolver.Tick(dt)` invokes `IActorPhysiology.TickPhysiology(dt)` on all active registered entities as timeline advances. | M1 | `ORIGINAL_REQUEST.md` R2 |
| 15 | Incapacitation Handling | Automatic action cancellation if an entity loses consciousness (`ConsciousnessLevel <= 0`) due to fatal trauma/hemorrhage. | M1 | `ORIGINAL_REQUEST.md` R2, Architectural Survey |
| 16 | Concrete Actions Suite | `GenericTacticalAction`, `MoveTacticalAction`, `AimTacticalAction`, `ShootTacticalAction` (clean progress math), `WaitTacticalAction`. | M1 | `TacticalSim.Core/Simulation/Actions/` |
| 17 | Dependency Injection Registration | Service collection extension methods (`AddTacticalSimCore`, `AddSimulationServices`) registering `ITurnResolver`. | M1 | `ORIGINAL_REQUEST.md` R3, `agents.md` |
| 18 | E2E Tier 1 Tests (Feature Coverage) | Direct happy-path unit tests for all resolver features and physiological ticking. | M2 | `ORIGINAL_REQUEST.md` AC |
| 19 | E2E Tier 2 Tests (Boundary & Corner Cases) | Extreme delta times, micro-steps, exact matches, empty registrations, rapid churn, zero-bleed, massive bleed, 7200s ischemia. | M2 | `ORIGINAL_REQUEST.md` AC |
| 20 | E2E Tier 3 Tests (Cross-Feature Combinations) | Concurrent multi-actor action chains running alongside active trauma progression, tourniquet ischemia, and mid-tick failure isolation. | M2 | `ORIGINAL_REQUEST.md` AC |
| 21 | E2E Tier 4 Tests (Real-World Scenarios) | Multi-actor tactical combat scenarios with movement, aiming, shooting, injury, tourniquet application, and turn progression. | M2 | `ORIGINAL_REQUEST.md` AC |
| 22 | Solution Clean Build & 100% Tests Pass | Zero warnings, zero errors (`dotnet build`), 100% test pass rate (`dotnet test`). | M3 | `ORIGINAL_REQUEST.md` AC |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Core Turn Resolver & Physiology Integration | Enhance `ITurnResolver` and `TurnResolver` with entity management, physiological ticking, clean action math, and DI registration. | none | DONE |
| M2 | Comprehensive E2E Testing Suite | Develop Tiers 1-4 comprehensive xUnit test suite for turn resolution, multi-actor interleaving, and physiological integration; publish `TEST_READY.md`. | M1 | DONE |
| M3 | Verification, Hardening & Acceptance Gate | Run full test suite, execute adversarial challenger verification, forensic integrity audit, and ensure 0 warnings/0 errors. | M1, M2 | DONE |

---

## Interface Contracts

### `ITurnResolver`
```csharp
namespace TacticalSim.Core.Simulation;

public interface ITurnResolver
{
    // Timeline & State
    float GlobalTime { get; }
    bool HasActiveActions { get; }
    int ActiveActorCount { get; }

    // Entity Management (Physiological Integration R2)
    void RegisterEntity(IEntity entity);
    bool UnregisterEntity(Guid entityId);
    IReadOnlyCollection<IEntity> GetRegisteredEntities();
    IEntity? GetEntity(Guid entityId);

    // Action Management
    void ScheduleAction(TacticalAction action);
    bool CancelAction(Guid actionId);
    int CancelActorActions(Guid actorId);
    IReadOnlyList<TacticalAction> GetActiveActions();
    IReadOnlyList<TacticalAction> GetQueuedActions(Guid actorId);
    TacticalAction? GetCurrentAction(Guid actorId);

    // Progression
    void Tick(float dt);
    void Reset();

    // Events
    event EventHandler<ActionEventArgs>? ActionScheduled;
    event EventHandler<ActionEventArgs>? ActionStarted;
    event EventHandler<ActionProgressEventArgs>? ActionProgressed;
    event EventHandler<ActionEventArgs>? ActionCompleted;
    event EventHandler<ActionEventArgs>? ActionCancelled;
    event EventHandler<ActionFailedEventArgs>? ActionFailed;
    event EventHandler<TimeAdvancedEventArgs>? TimeAdvanced;
    event EventHandler<EntityEventArgs>? EntityRegistered;
    event EventHandler<EntityEventArgs>? EntityUnregistered;
}
```

---

## Code Layout

- `TacticalSim.Core/`
  - `Simulation/`
    - `ITurnResolver.cs` (Interface contract)
    - `TurnResolver.cs` (Turn resolution & physiological ticking engine)
    - `TacticalAction.cs` (Base action class & state)
    - `TacticalActionState.cs` (State enum)
    - `TurnResolverEvents.cs` (Event argument definitions)
    - `Actions/` (`GenericTacticalAction.cs`, `MoveTacticalAction.cs`, `AimTacticalAction.cs`, `ShootTacticalAction.cs`, `WaitTacticalAction.cs`)
  - `Entities/`
    - `IEntity.cs` (Entity contract)
    - `TacticalEntity.cs` (Entity implementation)
  - `ActorPhysiology.cs` (`IActorPhysiology`, `TacticalActorPhysiology`, `BodyPart`)
  - `DependencyInjection/`
    - `ServiceCollectionExtensions.cs` (DI registrations)
- `TacticalSim.Tests/`
  - `TurnResolverTests.cs` (Unit tests for scheduling, cancellation, timeline)
  - `TurnResolverPhysiologyTests.cs` (Dedicated physiological integration tests)
  - `TurnResolverE2ETieredTests.cs` (101 comprehensive Tier 1-4 tests)
  - `TurnResolverEmpiricalChallengerTests.cs` (Challenger 1 stress tests)
  - `PhysiologyIntegrationChallenger2Tests.cs` (Challenger 2 trauma/ischemia tests)
  - `TurnResolverStressTests.cs` (Concurrency, stress, and sub-tick interleaving tests)
  - `E2ETacticalSimulationTests.cs` (End-to-end multi-entity tactical scenarios)
  - `DependencyInjectionTests.cs` (DI container resolution tests)
