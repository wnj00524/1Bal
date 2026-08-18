# Specification Mining Report: Issue #3 (Fractionated TU Turn Resolver)

**Author**: `spec_miner_survey_1` (Archetype: `teamwork_preview_spec_miner`)  
**Date**: 2026-08-18  
**Scope**: Issue #3 (Fractionated TU Turn Resolver) & Physiological Integration  
**Authoritative References**: `ORIGINAL_REQUEST.md`, `PROJECT.md`, `agents.md`, `TacticalSim.Core`, `TacticalSim.Tests`

---

## 1. Observation

### 1.1 Source Documents & Codebase Contracts
The specification was mined from the following authoritative files and lines of code:

1. **`c:\Users\jdwil\source\repos\Codex\1bal\.agents\ORIGINAL_REQUEST.md` (Lines 18-26, 43-66)**:
   - **R1 (Issue #3)**: "Create a simultaneous turn resolution system that manages a global timeline. It must be capable of scheduling concurrent actions from multiple entities and advancing their execution state based on fractionated Time Unit (TU) increments."
   - **R2 (Physiological Integration)**: "The Turn Resolver must have a mechanism to invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation as the timeline advances, ensuring bleeding and ischemia effects resolve properly over the game's duration."
   - **R3 (Architectural Decoupling)**: "All implementations must remain strictly isolated within `TacticalSim.Core` and rely on `Microsoft.Extensions.DependencyInjection` for service registration, conforming to the guidelines established in `agents.md`."
   - **Acceptance Criteria**:
     - Programmatic xUnit tests verify concurrent actions interleaved across fractionated time steps.
     - Programmatic xUnit tests verify `TickPhysiology` is successfully called on entities during turn progression.
     - Full solution (`dotnet build`) compiles with 0 errors and 0 warnings.
     - All tests (`dotnet test`) pass successfully.

2. **`c:\Users\jdwil\source\repos\Codex\1bal\agents.md` (Lines 6-36)**:
   - Strict decoupling: Keep mathematical simulation (`TacticalSim.Core`) independent of UI/rendering.
   - Dependency Injection: Use `Microsoft.Extensions.DependencyInjection`.
   - Math & Vectors: `System.Numerics.Vector3`.
   - Standard units: Time in seconds / TUs, distance in meters, mass in kg, density in $\text{kg/m}^3$, energy in Joules, bleed rates in $\text{ml/sec}$ (reported in $\text{ml/min}$).

3. **`c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Core\Simulation\ITurnResolver.cs` (Lines 1-113)**:
   - Interface definition for `ITurnResolver`:
     - Properties: `float GlobalTime { get; }`, `bool HasActiveActions { get; }`, `int ActiveActorCount { get; }`
     - Methods: `void ScheduleAction(TacticalAction action);`, `bool CancelAction(Guid actionId);`, `int CancelActorActions(Guid actorId);`, `IReadOnlyList<TacticalAction> GetActiveActions();`, `IReadOnlyList<TacticalAction> GetQueuedActions(Guid actorId);`, `TacticalAction? GetCurrentAction(Guid actorId);`, `void Tick(float dt);`, `void Reset();`
     - Events: `ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `ActionFailed`, `TimeAdvanced`.

4. **`c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Core\Simulation\TacticalAction.cs` (Lines 1-114)** & **`TacticalActionState.cs` (Lines 1-34)**:
   - State machine: `Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`.
   - Action properties: `Id`, `ActorId`, `TUCost`, `ExecutionProgress`, `State`, `StartTime`, `CompletionTime`, `FailureException`, `RemainingTU`, `NormalizedProgress`, `IsComplete`.
   - Action methods & hooks: `abstract void Execute(float dt);`, `virtual void OnStart();`, `virtual void OnComplete();`, `virtual void OnCancel();`, `virtual void OnFail(Exception ex);`.

5. **`c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Core\ActorPhysiology.cs` (Lines 101-207)**:
   - Interface `IActorPhysiology`:
     - `BodyPart RootBodyPart { get; }`
     - `float TotalBloodVolume { get; }`
     - `float ConsciousnessLevel { get; }`
     - `void TickPhysiology(float dt);`
     - `void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint);`
   - State Machine: `TacticalActorPhysiology`:
     - Hemorrhage classes: `Class1` (<15% blood loss), `Class2` (15-30%), `Class3` (30-40%), `Class4` (40-50%), `Fatal` (>50%).
     - Ischemia: Extremity tourniquet halts distal bleeding, increments `IschemiaDuration` by `dt`; turns necrotic after 7200s (2 hrs).
     - Cardiovascular decay: Blood volume deducted by `totalBleedRate * dt`.

6. **`c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Core\Entities\IEntity.cs` (Lines 1-15)** & **`TacticalEntity.cs` (Lines 1-22)**:
   - Entity contract linking `Guid Id`, `Vector3 Position`, `IActorPhysiology Physiology`, `WeaponProfile? EquippedWeapon`.

7. **`c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Core\DependencyInjection\ServiceCollectionExtensions.cs` (Lines 1-62)**:
   - `AddTacticalSimCore(this IServiceCollection services)`
   - `AddSimulationServices(this IServiceCollection services)` -> registers `ITurnResolver` $\rightarrow$ `TurnResolver` (Transient).

8. **Existing Test Suite Baseline**:
   - `dotnet test`: 232 tests passed, 0 failed, 0 warnings across `TacticalSim.Tests`.

---

## 2. Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|----------|---------|-------------|--------|---------|----------------|----------------|
| 1 | Timeline | Global Simulation Timeline | Monotonically advancing simulation clock ($T_g \ge 0$) tracking global elapsed time across all entities. | `dt > 0` (float) via `Tick(dt)` | `GlobalTime` property updated ($T_g \leftarrow T_g + dt$) | Rejects invalid `dt` | `ITurnResolver.cs`, `ORIGINAL_REQUEST.md` R1 |
| 2 | Timeline | Time Advanced Event | Strongly-typed event notification dispatched when timeline advances by $\Delta t$. | `Tick(dt)` invocation | `TimeAdvanced` event with `DeltaTime`, `PreviousGlobalTime`, `CurrentGlobalTime` | Dispatched synchronously after actor sub-steps | `TurnResolverEvents.cs`, `TurnResolver.cs:367` |
| 3 | Timeline | Timeline Reset | Clears active actions, queued actions, and resets global timeline to 0.0. | `Reset()` call | `GlobalTime = 0.0f`, `HasActiveActions = false`, queues emptied | None (idempotent) | `ITurnResolver.cs:75`, `TurnResolver.cs:371` |
| 4 | Scheduling | Concurrent Action Scheduling | Enqueues tactical actions per actor (`Guid ActorId`). If actor is idle, action becomes active; otherwise queued in FIFO order. | `TacticalAction action` via `ScheduleAction(action)` | Action registered, `ActionScheduled` event fired | Validates `action`, `ActorId`, `TUCost`, `State` | `ITurnResolver.cs:31`, `TurnResolver.cs:50` |
| 5 | Scheduling | Per-Actor FIFO Queuing | Maintains isolated FIFO action queues per entity ID so subsequent actions execute sequentially upon completion of previous action. | Multiple `TacticalAction` instances for same `ActorId` | Queued in internal dictionary of queues | Preserves strict FIFO ordering | `TurnResolver.cs:75-81`, `TurnResolverTests.cs:242` |
| 6 | Scheduling | Active & Queued State Inspection | Querying methods to inspect currently active actions, queued actions per actor, current action per actor, and actor counts. | `Guid actorId` | `GetActiveActions()`, `GetQueuedActions(actorId)`, `GetCurrentAction(actorId)`, `ActiveActorCount`, `HasActiveActions` | Returns empty list / null for unknown actors | `ITurnResolver.cs:49-63` |
| 7 | Execution | Fractionated TU Sub-Stepping | Advances active actions in discrete fractional $\Delta t$ increments, allowing arbitrary fine-grained time slicing. | `float dt` via `Tick(dt)` | `action.ExecutionProgress` incremented by $\Delta t$ (or step TU), `ActionProgressed` event fired | Throws if `dt <= 0`, `NaN`, or $\pm\infty$ | `TurnResolver.cs:224-363`, `PROJECT.md` F3 |
| 8 | Execution | Sub-Tick Carryover Interleaving | When an action completes within $\Delta t$, the remaining time $dt_{\text{remaining}} = \Delta t - \Delta t_{\text{needed}}$ immediately promotes and executes the next queued action within the SAME tick. | Chained actions with $\sum \text{TUCost} \le \Delta t$ | Consecutive actions executed and completed in single tick | Accurately calculates `StartTime` and `CompletionTime` | `TurnResolver.cs:238-350`, `TurnResolverStressTests.cs:20` |
| 9 | Execution | Multi-Actor Interleaved Concurrency | Multiple actors progress simultaneously. Actors with shorter TU costs complete earlier; remaining actors continue without blocking. | Multiple active actors, `Tick(dt)` | Independent progress per actor, deterministic actor order (`Guid` sorted) | Deterministic execution irrespective of insertion order | `TurnResolverTests.cs:176`, `PROJECT.md` F2 |
| 10 | Lifecycle | Tactical Action State Machine | Tracks action states: `Pending` $\rightarrow$ `Executing` $\rightarrow$ `Completed` / `Cancelled` / `Failed`. | State transitions driven by `ScheduleAction`, `Tick`, `CancelAction`, or exceptions | `State` property, `NormalizedProgress` ($[0, 1]$), `RemainingTU`, `IsComplete` | Enforces state transition invariants | `TacticalActionState.cs`, `TacticalAction.cs` |
| 11 | Lifecycle | Action Lifecycle Hooks | Virtual lifecycle callbacks on `TacticalAction` invoked during state transitions. | Lifecycle events | `OnStart()`, `OnComplete()`, `OnCancel()`, `OnFail(Exception)` invoked | Exceptions caught in `Execute` transition to `Failed` | `TacticalAction.cs:93-112` |
| 12 | Lifecycle | Action Cancellation (Single) | Cancels a specific action by ID (active or queued). If active, promotes next queued action. | `Guid actionId` via `CancelAction(actionId)` | Returns `bool` (true if cancelled), `ActionCancelled` fired | Returns `false` if `actionId == Guid.Empty` or not found | `ITurnResolver.cs:39`, `TurnResolver.cs:87` |
| 13 | Lifecycle | Actor Action Cancellation (Bulk) | Cancels all active and queued actions for a specified actor ID. | `Guid actorId` via `CancelActorActions(actorId)` | Returns `int` (count cancelled), `ActionCancelled` fired for each | Returns `0` if `actorId == Guid.Empty` or no actions | `ITurnResolver.cs:46`, `TurnResolver.cs:164` |
| 14 | Lifecycle | Fault Isolation & Failure State | If an action's `Execute(dt)` throws an unhandled exception, action transitions to `Failed`, stores exception, fires `ActionFailed`, and other actors continue unaffected. | Exception thrown in `Execute(dt)` | `State = Failed`, `FailureException = ex`, `ActionFailed` fired | Exception isolated; simulation loop continues | `TurnResolver.cs:288-297`, `TurnResolverEvents.cs:70` |
| 15 | Actions | Generic Tactical Action | Concrete tactical action backed by delegate callbacks for execution and lifecycle hooks. | Delegate actions (`onExecute`, `onStart`, `onComplete`, `onCancel`, `onFail`) | Invokes callbacks, records `ExecutionCount` and `TotalDeltaExecuted` | Bubble exceptions to resolver fault handler | `GenericTacticalAction.cs:8-71` |
| 16 | Actions | Movement Action | Computes 3D interpolated position over TU duration (`Vector3.Lerp(Start, Target, NormalizedProgress)`). | `StartPosition`, `TargetPosition`, `TUCost` / `MovementSpeed` | `CurrentPosition` updated on each tick | Clamped to TargetPosition on completion | `MoveTacticalAction.cs:9-51` |
| 17 | Actions | Aiming Action | Tracks target entity ID and dynamically scales aim precision bonus linearly with normalized progress. | `TargetId`, `TUCost`, `MaxAimBonus` | `CurrentAimBonus = MaxAimBonus * NormalizedProgress` | Dynamically calculated; bounded $[0, \text{MaxAimBonus}]$ | `AimTacticalAction.cs:8-34` |
| 18 | Actions | Ballistic Shoot Action | Executes weapon fire and projectile trajectory integration (RK4) upon consuming base TU cost. | `IEntity shooter`, `Vector3 targetDir`, `IEnvironmentModel env` | Projectile trajectory computed, `FinalState` recorded | Throws `InvalidOperationException` if ammo missing | `ShootTacticalAction.cs:8-66` |
| 19 | Actions | Wait / Idle Action | Consumes specified TU duration representing actor idling or delay. | `Guid actorId`, `float tuCost` | Time consumed without side-effects | Standard TU validation | `WaitTacticalAction.cs:8-24` |
| 20 | Physiology | Physiological Ticking State Machine | Updates biological trauma state machine over delta time $\Delta t$: deducts blood volume based on active bleed rates, advances ischemia, updates cardiovascular response. | `float dt` via `IActorPhysiology.TickPhysiology(dt)` | `TotalBloodVolume`, `ConsciousnessLevel`, `CurrentHemorrhageClass`, `HeartRateBpm`, `MeanArterialPressureMmhg` | Handles zero blood volume / dead state | `ActorPhysiology.cs:126-199`, `ORIGINAL_REQUEST.md` R2 |
| 21 | Physiology | Hemorrhage & Bleed Progression | Calculates active hemorrhage rates from damaged voxels in hierarchical body parts (`RootBodyPart` and children) and reduces systemic blood volume. | Damaged voxels, `dt` | Blood volume decreased by $\text{rate} \times dt$, transitions hemorrhage classes (Class 1 to Fatal) | Fatal when lost percent $> 50\%$ | `ActorPhysiology.cs:138-198` |
| 22 | Physiology | Extremity Tourniquet & Ischemia | Halts bleed distal to tourniquet on limbs (`LeftArm`, `RightArm`, `LeftLeg`, `RightLeg`), increments `IschemiaDuration` by $dt$, flags `IsNecrotic = true` if $> 7200\text{s}$. | `HasTourniquet = true`, `dt` | `GetActiveBleedRate() == 0`, `IschemiaDuration` accumulates | Necrotic after 2 hours without blood flow | `ActorPhysiology.cs:47-54, 146-158` |
| 23 | Physiology | Turn Resolver Physiological Integration Requirement | Turn Resolver mechanism to invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in simulation during timeline advancement. | Active entities with `IActorPhysiology`, `dt` | Synchronized physiological progression across global timeline | Must handle entity registration/lifecycle gracefully | `ORIGINAL_REQUEST.md` Follow-up R2, AC |
| 24 | DI | Dependency Injection Composition | Extension methods on `IServiceCollection` for registering simulation, material penetration, ballistics, and physiology services. | `IServiceCollection services` | Registers `ITurnResolver` $\rightarrow$ `TurnResolver` (Transient), drag models, environment models, penetration services | Rejects null `services` (`ArgumentNullException`) | `ServiceCollectionExtensions.cs`, `PROJECT.md` F10 |

---

## 3. Edge Cases

| # | Feature | Input | Observed Behavior |
|---|---------|-------|-------------------|
| 1 | Timeline `Tick` | `dt = 0.0f` | Throws `ArgumentException` ("Delta time (dt) must be strictly positive and finite."). |
| 2 | Timeline `Tick` | `dt = -1.0f` (negative) | Throws `ArgumentException` ("Delta time (dt) must be strictly positive and finite."). |
| 3 | Timeline `Tick` | `dt = float.NaN` | Throws `ArgumentException` ("Delta time (dt) must be strictly positive and finite."). |
| 4 | Timeline `Tick` | `dt = float.PositiveInfinity` / `NegativeInfinity` | Throws `ArgumentException` ("Delta time (dt) must be strictly positive and finite."). |
| 5 | Timeline `Tick` | `dt = 0.000001f` ($10^{-6}$ micro-step) | Global time and action execution progress advance by exact micro-step without numerical drift. |
| 6 | Action Scheduling | `action = null` | Throws `ArgumentNullException`. |
| 7 | Action Scheduling | `action.ActorId = Guid.Empty` | Throws `ArgumentException` ("ActorId cannot be empty."). |
| 8 | Action Scheduling | `action.TUCost = 0.0f`, `-5.0f`, `NaN`, `+Infinity` | Throws `ArgumentException` ("TUCost must be strictly positive and finite."). |
| 9 | Action Scheduling | `action.State = Executing` or `Completed` | Throws `InvalidOperationException` ("Cannot schedule action with state... Action state must be Pending."). |
| 10 | Action Cancellation | `CancelAction(Guid.Empty)` | Returns `false` without throwing exception or altering state. |
| 11 | Action Cancellation | `CancelAction(nonExistentGuid)` | Returns `false` without side effects. |
| 12 | Action Cancellation | Cancel active action with queued action waiting | Active action cancelled (`State = Cancelled`, `OnCancel()` called, `ActionCancelled` fired); next queued action promoted to active. |
| 13 | Action Cancellation | Cancel action in the middle of a queue | Target queued action removed and cancelled; remaining queue order preserved. |
| 14 | Actor Cancellation | `CancelActorActions(Guid.Empty)` | Returns `0`. |
| 15 | Actor Cancellation | `CancelActorActions(actorWithActiveAnd3Queued)` | Cancels active action and all 3 queued actions, fires 4 `ActionCancelled` events, returns `4`. |
| 16 | Sub-Tick Carryover | Single tick `dt = 1.0f`, 10 chained actions of `cost = 0.05f` (0.50 TU total) | All 10 actions execute and complete in FIFO order in the single tick; excess 0.50 TU remains elapsed on global clock. |
| 17 | Sub-Tick Carryover | Action exact match `cost = 0.5f`, `dt = 0.5f` | Action completes exactly at 0.5; `remainingDt` reaches zero without spurious promotions. |
| 18 | Execution Failure | Action throws `DivideByZeroException` in `Execute(dt)` | Action marked `State = Failed`, `FailureException` captured, `ActionFailed` fired, actor active action removed; subsequent ticks or other actors not interrupted. |
| 19 | Concurrency Determinism | 100 simultaneous actors with randomized Guids | Sorted deterministically by Guid so execution order is repeatable and identical across runs. |
| 20 | Resolver Reset | `Reset()` called with active and queued actions at $T_g = 45.2$ | `GlobalTime` reset to `0.0f`, all internal active dictionaries and queues cleared, `HasActiveActions` becomes `false`. |
| 21 | Physiology Zero Bleed | Actor with no trauma ticked for 100s | `TotalBloodVolume` remains at baseline (5000ml), `ConsciousnessLevel = 1.0`, `Class1` hemorrhage. |
| 22 | Physiology Massive Bleed | Total bleed rate 50 ml/s, ticked for 60s (3000ml lost = 60%) | Blood volume drops to 2000ml, transitions to `Fatal` hemorrhage class, `ConsciousnessLevel = 0`, heart rate = 0. |
| 23 | Physiology Tourniquet | Extremity limb with tourniquet ticked for 7300s | Distal bleed rate remains 0; `IschemiaDuration` exceeds 7200s, triggering `IsNecrotic = true`. |
| 24 | DI Service Resolution | Resolving `ITurnResolver` from `IServiceProvider` | Returns new instance of `TurnResolver` configured as Transient service. |

---

## 4. Architectural & Specification Matrix for Issue #3

### 4.1 Specification Matrix

| Requirement Area | Specification Item | Functional Behavior | Architectural Contract / Type | Verification Criteria |
|------------------|--------------------|---------------------|--------------------------------|-----------------------|
| **R1: Timeline** | Monotonic Clock | $T_g(t + \Delta t) = T_g(t) + \Delta t, \Delta t > 0$ | `ITurnResolver.GlobalTime`, `ITurnResolver.Tick(float dt)` | xUnit tests asserting $T_g$ increases monotonically |
| **R1: Timeline** | Time Step Invariants | $\Delta t$ must be finite, strictly positive, non-NaN | `TurnResolver.Tick(float dt)` argument checks | Throws `ArgumentException` on invalid values |
| **R1: Timeline** | Observability | Broadcast tick progression with delta, previous, and current time | `event EventHandler<TimeAdvancedEventArgs> TimeAdvanced` | Assert event subscriber receives exact timestamps |
| **R1: Scheduling** | Multi-Actor Concurrent | Support arbitrary number of actors simultaneously | `ITurnResolver.ScheduleAction(TacticalAction)` | Multi-actor concurrency tests with varying TU costs |
| **R1: Scheduling** | Per-Actor FIFO Queue | One active action per actor; additional actions enqueued | `Dictionary<Guid, Queue<TacticalAction>>` | FIFO ordering verified across multiple chained actions |
| **R1: Execution** | Fractionated Sub-Stepping | Actions advance by fractional $\Delta t$ steps | `TacticalAction.Execute(dt)` invoked with step $\Delta t$ | `ExecutionProgress` matches accumulated $\Delta t$ |
| **R1: Execution** | Carryover Interleaving | Remaining $\Delta t$ after completion carries over to next queued action | Sub-step loop in `TurnResolver.Tick` | Chained micro-actions complete in same simulation step |
| **R1: Lifecycle** | Action State Transitions | `Pending` $\rightarrow$ `Executing` $\rightarrow$ `Completed` / `Cancelled` / `Failed` | `TacticalActionState`, `TacticalAction.State` | Assert state transitions and timestamp properties |
| **R1: Lifecycle** | Action Cancellation | Immediate cancellation of active/queued actions with queue promotion | `CancelAction(Guid)`, `CancelActorActions(Guid)` | Assert cancellation events, return values, queue promotions |
| **R1: Lifecycle** | Exception Isolation | Unhandled exceptions inside action execution do not crash resolver | Try-catch in `Tick`, `ActionFailed` event | Assert other actors continue and action is marked `Failed` |
| **R2: Physiology** | Physiological Ticking | Invoke `TickPhysiology(dt)` on simulation entities as timeline advances | `IActorPhysiology.TickPhysiology(float dt)` | Verify bleed reduction, ischemia accumulation, cardiovascular response |
| **R2: Physiology** | Bleed & Hemorrhage Decay | Systemic blood volume reduced by $\text{rate} \times dt$ | `TacticalActorPhysiology.TotalBloodVolume` | Hemorrhage class transitions (Class 1 $\rightarrow$ Fatal) |
| **R2: Physiology** | Extremity Ischemia | Tourniquets halt bleed but accumulate ischemia duration up to necrosis | `BodyPart.IschemiaDuration`, `BodyPart.IsNecrotic` | `IsNecrotic == true` when $t > 7200\text{s}$ |
| **R3: Decoupling** | Microsoft DI Registration | Clean registration of all simulation services in DI container | `ServiceCollectionExtensions.AddTacticalSimCore`, `AddSimulationServices` | `IServiceProvider.GetRequiredService<ITurnResolver>()` succeeds |
| **R3: Decoupling** | Zero Warnings & Clean Hygiene | Zero compiler warnings with nullable references enabled | `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` | `dotnet build` produces 0 warnings and 0 errors |

---

## 5. Handoff Report

### 5.1 Observation
1. **Repository Layout**:
   - `TacticalSim.Core`: .NET 8.0 library containing ballistics, materials, physiology, and simulation modules.
   - `TacticalSim.Tests`: .NET 8.0 test project with 232 existing tests in xUnit, covering ballistics, material penetration, turn resolver stress/concurrency/adversarial invariants, and DI.
2. **Current Implementation State**:
   - `TurnResolver` implements `ITurnResolver` with full fractionated TU progression, per-actor FIFO queuing, sub-tick carryover interleaving, strongly typed observability events, and fault isolation.
   - `ActorPhysiology` defines `IActorPhysiology` and `TacticalActorPhysiology` with hierarchical body parts, voxels, bleed rate calculation, ischemia duration tracking, necrosis, and cardiovascular/consciousness state machines.
   - `IEntity` and `TacticalEntity` bind `Guid Id`, `Vector3 Position`, `IActorPhysiology Physiology`, and `WeaponProfile? EquippedWeapon`.
   - `ServiceCollectionExtensions` provides `AddTacticalSimCore()`, `AddMaterialPenetration()`, and `AddSimulationServices()`.
3. **Verification Command**:
   - `dotnet test --verbosity normal`: Exited with code 0 (232 passed, 0 failed, 0 warnings, build succeeded).

### 5.2 Logic Chain
1. **Timeline & Multi-Entity Scheduling**:
   - In a tactical simulation, actors declare actions of varying TU costs (e.g. Move: 12 TU, Aim: 8 TU, Shoot: 15 TU).
   - Because time advances continuously or in arbitrary discrete tick deltas $\Delta t$ (e.g., $0.1\text{ TU}$, $0.25\text{ TU}$, $1.0\text{ TU}$), multiple actors must progress simultaneously.
   - When an actor's action finishes mid-tick, the unused fraction of $\Delta t$ must immediately roll over to their next queued action (sub-tick carryover) to maintain continuous execution fidelity without stalling between ticks.
2. **Physiological Integration**:
   - As global time advances, physical and biological processes occur concurrently with tactical actions.
   - Biological trauma (active hemorrhages from perforated organs and tissue ischemia from tourniquets) is time-dependent.
   - Therefore, the turn resolution engine or simulation manager driving the turn resolver must tick `IActorPhysiology.TickPhysiology(dt)` for all active entities in the simulation, ensuring that blood volume decreases and ischemia progresses accurately over the duration of the combat encounter.
3. **Architectural Decoupling & DI**:
   - The simulation components must remain modular and testable without UI or engine coupling.
   - `Microsoft.Extensions.DependencyInjection` enables downstream consumers (console apps, Godot clients, automated test suites) to compose the simulation engine with custom configurations or standard defaults.

### 5.3 Caveats
- **Entity Management in TurnResolver**: The current `ITurnResolver` interface is focused on scheduling and advancing `TacticalAction` instances by `Guid ActorId`. To integrate `IActorPhysiology.TickPhysiology(dt)`, the architecture can either:
  1. Expand `ITurnResolver` or `TurnResolver` with entity registration methods (e.g. `RegisterEntity(IEntity entity)` / `UnregisterEntity(Guid id)` or `RegisterPhysiology(Guid actorId, IActorPhysiology physiology)`), OR
  2. Maintain a simulation coordinator / world manager that ticks both `ITurnResolver.Tick(dt)` and registered entities' `IActorPhysiology.TickPhysiology(dt)`, OR
  3. Allow actions to reference entities and tick physiology during action execution.
  The orchestrator and architectural team should select the pattern that best preserves decoupling and matches project conventions.
- **Time Unit (TU) to Seconds Conversion**: In `TacticalSim.Core`, actions consume TUs while physiology calculates ischemia in seconds (e.g., 7200 seconds for necrosis). In tactical scenarios, 1 TU often corresponds to a fixed physical duration (e.g., 0.1s or 1.0s). The specification assumes a 1:1 scale unless explicitly scaled by the simulation layer.

### 5.4 Conclusion
- The specifications for Issue #3 (Fractionated TU Turn Resolver) have been fully mined, categorized, and documented.
- The requirements encompass:
  1. Global timeline management with monotonic progression and discrete event notification.
  2. Concurrent multi-entity scheduling with per-actor FIFO queues.
  3. Fractionated TU advancement with exact sub-tick carryover interleaving.
  4. Complete action lifecycle state machine (`Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`) and failure isolation.
  5. Concrete tactical action representations (`Generic`, `Move`, `Aim`, `Shoot`, `Wait`).
  6. Physiological state machine integration (`IActorPhysiology.TickPhysiology(dt)`).
  7. Decoupled dependency injection registration via `Microsoft.Extensions.DependencyInjection`.
  8. Rigorous acceptance criteria verifiable via xUnit tests.

### 5.5 Verification Method
To independently verify this specification against the codebase:
1. **Solution Build**:
   ```pwsh
   dotnet build --configuration Debug c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.slnx
   ```
   *Expected*: Build succeeded with 0 Warning(s) and 0 Error(s).

2. **Full Test Suite Execution**:
   ```pwsh
   dotnet test --configuration Debug --verbosity normal c:\Users\jdwil\source\repos\Codex\1bal\TacticalSim.Tests\TacticalSim.Tests.csproj
   ```
   *Expected*: 232+ tests pass with 0 failures.

3. **Verify DI and Simulation Integration Tests**:
   - Inspect `TacticalSim.Tests/TurnResolverTests.cs`
   - Inspect `TacticalSim.Tests/TurnResolverStressTests.cs`
   - Inspect `TacticalSim.Tests/DependencyInjectionTests.cs`
   - Inspect `TacticalSim.Tests/E2ETacticalSimulationTests.cs`
