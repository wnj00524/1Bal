# Architectural Analysis & Investigation Report: Simultaneous Turn Resolution & Fractionated TU Simulation

**Author:** explorer_survey_2 (Teamwork Explorer)  
**Date:** 2026-08-18  
**Scope:** `TacticalSim.Core.Simulation`, `TacticalSim.Core.Entities`, `TacticalSim.Core.Physiology`, `TacticalSim.Core.DependencyInjection`

---

## 1. Observation

### 1.1 Solution & Project Structure
The solution `TacticalSim.slnx` contains two primary projects targeting `.NET 8.0`:
1. `TacticalSim.Core/TacticalSim.Core.csproj`: Core simulation library with dependency injection (`Microsoft.Extensions.DependencyInjection` v10.0.11).
2. `TacticalSim.Tests/TacticalSim.Tests.csproj`: xUnit test suite (`xunit` v2.5.3, `Microsoft.NET.Test.Sdk` v17.8.0, `coverlet.collector` v6.0.0).

All 232 existing tests currently compile with zero warnings and pass (`dotnet test` duration: ~280ms).

---

### 1.2 Existing Action Representations & Command Structures
Direct observation of `TacticalSim.Core/Simulation/TacticalAction.cs` and `TacticalSim.Core/Simulation/TacticalActionState.cs`:

- **Action Lifecycle States (`TacticalActionState`)**:
  - `Pending` (0): Registered and enqueued, awaiting execution.
  - `Executing` (1): Actively executing in the resolver loop.
  - `Completed` (2): Fully consumed its required Time Units (`ExecutionProgress >= TUCost`).
  - `Cancelled` (3): Aborted prior to completion via explicit cancellation or actor purge.
  - `Failed` (4): Threw an unhandled exception during execution; isolated from other actors.

- **`TacticalAction` Base Class (`TacticalAction.cs:11-113`)**:
  ```csharp
  public abstract class TacticalAction
  {
      public Guid Id { get; set; } = Guid.NewGuid();
      public Guid ActorId { get; set; }
      public float TUCost { get; set; }
      public float ExecutionProgress { get; set; }
      public TacticalActionState State { get; internal set; } = TacticalActionState.Pending;
      public float StartTime { get; internal set; }
      public float? CompletionTime { get; internal set; }
      public Exception? FailureException { get; internal set; }
      public float RemainingTU => MathF.Max(0f, TUCost - ExecutionProgress);
      public float NormalizedProgress => TUCost > 0f ? Math.Clamp(ExecutionProgress / TUCost, 0f, 1f) : 1f;
      public bool IsComplete => State == TacticalActionState.Completed || ExecutionProgress >= TUCost;

      public abstract void Execute(float dt);
      public virtual void OnStart() { }
      public virtual void OnComplete() { }
      public virtual void OnCancel() { }
      public virtual void OnFail(Exception ex) { }
  }
  ```

- **Concrete Tactical Actions (`TacticalSim.Core/Simulation/Actions/`)**:
  - `GenericTacticalAction`: Delegate-driven action with `Action<float>? OnExecuteCallback`, `Action? OnStartCallback`, `Action? OnCompleteCallback`, `Action? OnCancelCallback`, and `Action<Exception>? OnFailCallback`.
  - `MoveTacticalAction`: Spatial translation across 3D coordinates (`Vector3 StartPosition`, `TargetPosition`). During `Execute(dt)`, updates `CurrentPosition = Vector3.Lerp(StartPosition, TargetPosition, NormalizedProgress)`.
  - `AimTacticalAction`: Continuous precision accumulation targeting `Guid TargetId` (`CurrentAimBonus = MaxAimBonus * NormalizedProgress`).
  - `WaitTacticalAction`: Idle waiting for a specified TU duration with an empty `Execute(dt)` body.
  - `ShootTacticalAction`: Ballistic firing simulation when TU duration completes (`BallisticSolver.StepRK4` integration).
    *Observation Note*: In `ShootTacticalAction.cs:32`, line 32 contains `ExecutionProgress += dt;` and line 62 contains `State = TacticalActionState.Completed;`. Because `TurnResolver.Tick` *already* increments `ExecutionProgress` before invoking `Execute(dt)`, `ShootTacticalAction` contains a redundant duplicate increment.

---

### 1.3 Turn Resolver Architecture & Event Loop
Direct observation of `TacticalSim.Core/Simulation/TurnResolver.cs` and `ITurnResolver.cs`:

- **Internal Storage**:
  - `_globalTime`: `float` simulation timeline clock.
  - `_activeActions`: `Dictionary<Guid, TacticalAction>` mapping each `ActorId` to its currently executing action.
  - `_actorQueues`: `Dictionary<Guid, Queue<TacticalAction>>` mapping each `ActorId` to a FIFO queue of subsequent actions.

- **Scheduling & Queueing (`ScheduleAction`, lines 50-84)**:
  - Validates `action != null`, `action.ActorId != Guid.Empty`, `action.TUCost > 0f` (finite, positive), and `action.State == TacticalActionState.Pending`.
  - If `_activeActions` does not contain `ActorId`, action is immediately placed in `_activeActions`.
  - If `_activeActions` already has an action for `ActorId`, action is pushed into `_actorQueues[ActorId]`.
  - Fires `ActionScheduled?.Invoke(this, new ActionEventArgs(action, _globalTime))`.

- **Cancellation (`CancelAction`, `CancelActorActions`, lines 87-194)**:
  - If an active action is cancelled, it transitions to `TacticalActionState.Cancelled`, calls `action.OnCancel()`, fires `ActionCancelled`, and immediately promotes the next queued action for that actor from `_actorQueues` into `_activeActions`.

- **Tick Loop & Sub-Stepping (`Tick(float dt)`, lines 224-368)**:
  ```csharp
  // 1. Snapshot active actor IDs sorted deterministically
  var actorIds = _activeActions.Keys.OrderBy(id => id).ToList();

  // 2. Iterate each actor independently with sub-tick carryover
  foreach (var actorId in actorIds)
  {
      float remainingDt = dt;
      while (remainingDt > Epsilon)
      {
          // Retrieve or promote action
          // If Pending -> transition to Executing, set StartTime, fire ActionStarted
          // Check if neededTU <= remainingDt + Epsilon:
          //   - If completing: set ExecutionProgress = TUCost, State = Completed,
          //     call Execute(stepDt), OnComplete(), fire ActionProgressed & ActionCompleted,
          //     remainingDt -= stepDt, remove from active actions -> loop promotes next queued action!
          //   - If partial: ExecutionProgress += remainingDt, call Execute(remainingDt),
          //     fire ActionProgressed, remainingDt = 0f -> break
      }
  }

  // 3. Advance global timeline clock
  _globalTime += dt;
  TimeAdvanced?.Invoke(this, new TimeAdvancedEventArgs(dt, prevTime, _globalTime));
  ```

- **Observability Events (`TurnResolverEvents.cs:1-128`)**:
  - `ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `ActionFailed`, `TimeAdvanced`.

---

### 1.4 Entities & Physiology State Machine
Direct observation of `TacticalSim.Core/Entities/` and `TacticalSim.Core/ActorPhysiology.cs`:

- **`IEntity` / `TacticalEntity` (`TacticalSim.Core/Entities/IEntity.cs`)**:
  ```csharp
  public interface IEntity
  {
      Guid Id { get; }
      Vector3 Position { get; set; }
      IActorPhysiology Physiology { get; }
      WeaponProfile? EquippedWeapon { get; set; }
  }
  ```
- **`IActorPhysiology` & `TacticalActorPhysiology` (`TacticalSim.Core/ActorPhysiology.cs:103-205`)**:
  - Exposes `BodyPart RootBodyPart`, `float TotalBloodVolume` (5000 mL baseline), `float ConsciousnessLevel` (0.0 to 1.0), `HemorrhageClass CurrentHemorrhageClass`, `HeartRateBpm`, `MeanArterialPressureMmhg`.
  - Exposes `void TickPhysiology(float dt)`:
    1. Aggregates bleed rates from all body parts: `TotalBloodVolume -= totalBleedRate * dt`.
    2. Updates tourniquet ischemia duration: `part.IschemiaDuration += dt` (flags necrosis if $> 7200\text{ s}$).
    3. Updates cardiovascular state & consciousness level based on percentage blood loss:
       - $< 15\%$ loss $\to$ Class 1, consciousness = 1.0
       - $15\% - 30\%$ loss $\to$ Class 2, consciousness = 0.9
       - $30\% - 40\%$ loss $\to$ Class 3, consciousness = 0.6
       - $40\% - 50\%$ loss $\to$ Class 4, consciousness = 0.2
       - $> 50\%$ loss $\to$ Fatal, consciousness = 0.0
- **Current Observation**: `TurnResolver` currently operates solely on `Guid ActorId` and `TacticalAction`. It does not currently register `IEntity` or invoke `IActorPhysiology.TickPhysiology(dt)` during `Tick(dt)`.

---

## 2. Logic Chain

### 2.1 Analysis of Simultaneous Fractionated TU Turn Resolution Patterns

| Pattern | Mechanism | Strengths | Weaknesses | Fit for TacticalSim |
|---|---|---|---|---|
| **A. Per-Actor Sliced Sub-Stepping (Current Implementation)** | Outer loop over actors (sorted by `ActorId`); inner `while (remainingDt > 0)` exhausts actor's time budget $\Delta t$, promoting queued actions and executing fractional carryover. | • Extremely fast $O(N \cdot K)$ where $N$ = active actors, $K$ = actions per tick.<br>• Exact sub-tick timestamps.<br>• Zero temporal drift across ticks.<br>• Perfect deterministic reproducibility. | • Actor A processes its entire $[t, t+\Delta t]$ time budget before Actor B begins its $[t, t+\Delta t]$ budget within the tick. If Actor A's action at $t+0.2$ impacts Actor B, Actor B's state during that same tick was not yet updated at $t+0.2$ unless resolved via global event slices. | **Current Production Pattern** (solid baseline, handles concurrent multi-actor queues and carryover). |
| **B. Global Discrete Event Min-Heap (Priority Queue)** | Future action completion events ($T_{\text{completion}} = T_{\text{start}} + \text{TUCost}$) placed in a global min-heap sorted by $(T_{\text{time}}, \text{Priority}, \text{ActorId})$. Resolver pops earliest event, jumps $T_{\text{global}} \to T_{\text{event}}$, and triggers completion. | • Strict global temporal ordering across all actors.<br>• Clean event-driven scheduling. | • Clunky for continuous-time processes (e.g. continuous motion interpolation, physiology bleed rates $d(\text{Blood})/dt$, sensor queries) which require continuous sampling rather than jumping between sparse discrete events. | **Poor fit for continuous ballistics and continuous physiology integration.** |
| **C. Synchronized Micro-Slice Stepping (Enhanced Hybrid)** | Fixed macro-tick `Tick(dt)` divided into dynamic micro-slices $\delta t = \min_{a}(\text{RemainingTU}_a, \text{remainingDt})$. All actors and continuous systems (physiology) advance in synchronized $\delta t$ slices. | • Combines continuous time integration with strict cross-actor temporal interleaving.<br>• Immediate reaction/opportunity fire and damage response at the exact micro-slice where impact occurs. | • Higher overhead when actors have heavily disparate, non-aligned fractionated costs (multiple micro-iterations per tick). | **Optimal for future interactive tactical reaction loops.** |

---

### 2.2 Deterministic Concurrency & Interleaving Architecture

To achieve 100% deterministic simulation across platforms, threads, and runs:
1. **Canonical Actor Ordering**:
   - `TurnResolver.cs:232` sorts actors deterministically: `_activeActions.Keys.OrderBy(id => id).ToList()`.
   - For domain-specific initiative (e.g., Agility or Reaction stats), the sort key can be generalized to `(InitiativePriority, ActorId)` so that higher initiative actors are processed first, with `ActorId` breaking any ties.
2. **Sub-Tick Carryover Mechanics**:
   - If Actor 1 has Action A (cost: 0.3 TU) and Action B (cost: 0.5 TU), and `Tick(1.0f)` is called:
     - Sub-step 1: Action A executes $0.3$ TU, completes at $T_{\text{global}} + 0.3$.
     - Sub-step 2: Remaining time $\Delta t_{\text{rem}} = 0.7$ TU. Action B is dequeued, starts at $T_{\text{global}} + 0.3$, consumes $0.5$ TU, and completes at $T_{\text{global}} + 0.8$.
     - Sub-step 3: Remaining time $\Delta t_{\text{rem}} = 0.2$ TU. No further queued actions; actor becomes idle.
     - Final state: Both actions completed in a single tick; zero lost time; exact completion timestamps recorded.
3. **Fault Isolation**:
   - If an action throws during `Execute(dt)`, `TurnResolver` catches the exception, transitions that action to `TacticalActionState.Failed`, fires `ActionFailed`, and purges that actor's active action without terminating or corrupting other actors' executions.

---

### 2.3 Entity Registration & Physiological Integration Architecture

Per `ORIGINAL_REQUEST.md` (R2 - Physiological Integration):
*"The Turn Resolver must have a mechanism to invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation as the timeline advances, ensuring bleeding and ischemia effects resolve properly over the game's duration."*

To satisfy this requirement with strict architectural decoupling:
1. **Entity Management in `ITurnResolver`**:
   - `ITurnResolver` should provide registration/unregistration for entities:
     ```csharp
     void RegisterEntity(IEntity entity);
     bool UnregisterEntity(Guid entityId);
     IReadOnlyCollection<IEntity> GetRegisteredEntities();
     ```
2. **Physiology Ticking in `Tick(float dt)`**:
   - During each `Tick(dt)` invocation:
     ```csharp
     // Advance physiology for all registered entities
     foreach (var entity in _registeredEntities.Values.OrderBy(e => e.Id))
     {
         entity.Physiology.TickPhysiology(dt);

         // Automatic Invalidation on Incapacitation:
         // If entity consciousness reaches 0 (dead or unconscious), abort ongoing actions
         if (entity.Physiology.ConsciousnessLevel <= 0f)
         {
             CancelActorActions(entity.Id);
         }
     }
     ```
3. **Decoupled DI Composition**:
   - `AddSimulationServices` in `TacticalSim.Core.DependencyInjection.ServiceCollectionExtensions` registers `ITurnResolver` $\to$ `TurnResolver` as `Transient` or `Scoped`.
   - Factory or DI resolution provides clean instantiation without hardcoded singleton dependencies.

---

## 3. Caveats

1. **`ShootTacticalAction` Redundant Progress Increment**:
   - In `ShootTacticalAction.cs:32`, `ExecutionProgress += dt;` is executed inside `Execute(dt)`. Because `TurnResolver.Tick` already modifies `ExecutionProgress` before calling `Execute`, concrete action implementations should not directly modify `ExecutionProgress` unless executing outside `TurnResolver`.
2. **Action Interruption Granularity**:
   - In the current per-actor sliced model, if Actor A shoots Actor B at $t = 0.3$ TU, Actor B's action in that same tick may have already completed if Actor B's ID was sorted before Actor A. If sub-tick real-time reactivity (e.g. bullet flight interrupting mid-movement) is required, the solver will need micro-slice synchronization or delayed event commitment.
3. **Physiology Performance at Scale**:
   - Voxel-level trauma (`AnatomicalDummyBuilder` generates ~15,000 voxels per torso dummy) is processed only on impact (`ProcessImpact`). `TickPhysiology(dt)` only aggregates tree-level bleed rates and ischemia timers across ~7 `BodyPart` nodes, running in under $1\,\mu\text{s}$ per entity.

---

## 4. Conclusion & Architectural Recommendations

### 4.1 Recommended Interface Enhancements for `ITurnResolver`
```csharp
namespace TacticalSim.Core.Simulation
{
    public interface ITurnResolver
    {
        // Timeline & State
        float GlobalTime { get; }
        bool HasActiveActions { get; }
        int ActiveActorCount { get; }

        // Entity Registration (R2 Physiological Integration)
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

        // Timeline Progression
        void Tick(float dt);
        void Reset();

        // Lifecycle & Observability Events
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
}
```

### 4.2 Proposed Implementation Blueprint for `TurnResolver.cs`
1. Maintain `Dictionary<Guid, IEntity> _registeredEntities = new();`.
2. In `RegisterEntity(IEntity entity)`: validate non-null, store by `entity.Id`.
3. In `Tick(float dt)`:
   - Advance action execution queues for active actors with deterministic sub-tick carryover.
   - Advance physiology for all registered entities via `entity.Physiology.TickPhysiology(dt)`.
   - If `entity.Physiology.ConsciousnessLevel <= 0f`, automatically invoke `CancelActorActions(entity.Id)` to cleanly abort actions for incapacitated or deceased entities.
   - Advance `_globalTime += dt` and fire `TimeAdvanced`.
4. In `Reset()`: clear `_activeActions`, `_actorQueues`, `_registeredEntities`, and reset `_globalTime = 0f`.

---

## 5. Verification Method

### 5.1 Verification Commands
1. **Compilation Check (Zero Warnings/Errors)**:
   ```pwsh
   dotnet build TacticalSim.slnx --configuration Debug
   ```
   *Expected*: `Build succeeded. 0 Warning(s), 0 Error(s).`

2. **Test Suite Execution**:
   ```pwsh
   dotnet test TacticalSim.slnx --verbosity normal
   ```
   *Expected*: All 232 existing tests pass.

### 5.2 Specific xUnit Verification Scenarios for Issue #3 & Physiological Integration
When implementing the recommended changes, verify with the following dedicated test cases:
1. `TurnResolver_RegistersAndTracksEntities`: Verifies `RegisterEntity`, `UnregisterEntity`, and `GetRegisteredEntities`.
2. `TurnResolver_Tick_InvokesTickPhysiology_OnAllRegisteredEntities`: Registers entities with active bleed rates, advances timeline with `resolver.Tick(1.0f)`, and verifies `TotalBloodVolume` decreases by exactly `totalBleedRate * 1.0f`.
3. `TurnResolver_IncapacitatedEntity_AutomaticallyCancelsActiveAndQueuedActions`: Inflicts lethal/incapacitating trauma (reducing consciousness to 0.0), calls `resolver.Tick(0.5f)`, and asserts that active and queued actions transition to `TacticalActionState.Cancelled`.
4. `TurnResolver_ConcurrentMultiActorCarryover_MaintainsExactTimestamps`: Verifies multiple actors with fractional action chains (e.g., $0.35 + 0.65$ TU) resolve concurrently within a $1.0$ TU tick with exact `CompletionTime` values and no temporal drift.
