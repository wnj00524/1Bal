# Handoff Report: Fractionated TU Turn Resolver (Issue #3)

## 1. Observation

### 1.1 Project & Solution Baseline
- **Build Status**: Solution builds cleanly with .NET 8.0 SDK.
- **Test Status**: `dotnet test` executes 2 tests passing in `TacticalSim.Tests/BallisticSolverTests.cs` (0 failed, 2 passed).
- **Core Dependencies**: `TacticalSim.Core.csproj` includes `Microsoft.Extensions.DependencyInjection` (v10.0.11), target framework `net8.0`, with `<Nullable>enable</Nullable>`.
- **Existing Scaffolding**: `TacticalSim.Core/TurnResolution.cs` currently contains initial stub definitions:
```csharp
namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Represents an action that consumes Time Units (TUs) within the simulation.
    /// </summary>
    public abstract class TacticalAction
    {
        public Guid ActorId { get; set; }
        public float TUCost { get; set; }
        public float ExecutionProgress { get; set; }
        
        /// <summary>
        /// Advances the action execution by fractionated timesteps.
        /// </summary>
        public abstract void Execute(float dt);
        public bool IsComplete => ExecutionProgress >= TUCost;
    }

    /// <summary>
    /// Interface for managing the Simultaneous Turn Resolution system.
    /// </summary>
    public interface ITurnResolver
    {
        /// <summary>
        /// Current global time in the simulation.
        /// </summary>
        float GlobalTime { get; }

        /// <summary>
        /// Schedules an action for an actor.
        /// </summary>
        void ScheduleAction(TacticalAction action);

        /// <summary>
        /// Advances the simulation by a fractionated timestep, executing all concurrent actions.
        /// </summary>
        void Tick(float dt);
    }
}
```

### 1.2 User Requirements (from `ORIGINAL_REQUEST.md`)
- **R1**: Create a simultaneous turn resolution system that manages a global timeline. It must be capable of scheduling concurrent actions from multiple entities and advancing their execution state based on fractionated Time Unit (TU) increments.
- **R3**: Isolated within `TacticalSim.Core`, registered via `Microsoft.Extensions.DependencyInjection`, conforming to `agents.md`.
- **Acceptance Criteria**: Programmatic xUnit tests in `TacticalSim.Tests` verifying multiple concurrent actions interleaved and resolved across fractionated time steps; clean compile; all tests pass.

---

## 2. Logic Chain

### 2.1 Domain Model Analysis for Simultaneous Turn Resolution

#### 2.1.1 Global Timeline & Fractionated Time Units (TU)
- **Time Unit (TU)**: Standardized simulation time quantity representing duration. All actions define a `TUCost > 0`.
- **Global Time ($T_g$)**: Monotonically advancing simulation clock ($T_g \ge 0$).
- **Fractionated Timestep ($\Delta t$)**: Discrete delta time by which the simulation timeline advances per `Tick(dt)`.
- **Time Discretization & Floating-Point Stability**:
  - Because `dt` and `TUCost` are floating-point values, standard rounding drift can occur (e.g. $0.1 + 0.1 + 0.1 \ne 0.3$).
  - An epsilon tolerance ($\epsilon = 1 \times 10^{-5}\text{f}$) must be used for completion comparison: `ExecutionProgress + epsilon >= TUCost`.
  - On action completion, `ExecutionProgress` is clamped exactly to `TUCost` to prevent arithmetic overshoot.

#### 2.1.2 Concurrency & Multi-Entity Scheduling
- **Entity Model**: Each action is associated with an actor identified by `Guid ActorId` (and unique `Guid Id` for the action).
- **Per-Actor Queuing vs Multi-Actor Concurrency**:
  - Each individual actor maintains an ordered FIFO queue of pending actions.
  - Across different actors ($A_1, A_2, \dots, A_N$), active actions execute **concurrently** (simultaneously in parallel timeline progress).
  - When `ScheduleAction(action)` is called:
    - If the actor has no active action, the action becomes the actor's currently active action (transitioning to `Pending` / ready to start on next tick).
    - If the actor already has an active action, the action is enqueued in that actor's pending action queue.
- **Action Cancellation & Interruption**:
  - Actions can be cancelled individually via `CancelAction(Guid actionId)` or per actor via `CancelActorActions(Guid actorId)`.
  - When an active action is cancelled, the next queued action for that actor (if any) is promoted to active status.

#### 2.1.3 Sub-Tick Interleaving & Fractional Carryover
When `Tick(float dt)` is called with a step $\Delta t$:
1. For each actor with an active action, let $\text{remainingTU} = \text{action.TUCost} - \text{action.ExecutionProgress}$.
2. If $\text{remainingTU} \ge \Delta t$:
   - The action executes for the full step: `action.Execute(dt)`.
   - `action.ExecutionProgress += dt`.
3. If $\text{remainingTU} < \Delta t$:
   - The action executes for the remaining duration: `action.Execute(remainingTU)`.
   - `action.ExecutionProgress = action.TUCost`.
   - The action transitions to `Completed` and fires completion hooks/events.
   - Let $\Delta t_{\text{carryover}} = \Delta t - \text{remainingTU}$.
   - If the actor has a queued action in its queue, the next action is dequeued, started at $T_g + \text{remainingTU}$, and immediately executed for $\Delta t_{\text{carryover}}$ within the same tick (or recursively sub-stepped).
4. All active actions across all actors are updated consistently, advancing the global timeline by $\Delta t$.

#### 2.1.4 Action Lifecycle State Machine
```
   [ ScheduleAction ]
          │
          ▼
   ┌──────────────┐
   │   Pending    │ ──(Cancel)──> ┌─────────────┐
   └──────────────┘               │  Cancelled  │
          │ (Tick start)          └─────────────┘
          ▼                              ▲
   ┌──────────────┐                      │
   │  Executing   │ ──(Cancel/Interrupt)─┘
   └──────────────┘
     │          │
     │          └──(Uncaught Exception)──> ┌─────────────┐
     ▼ (Progress >= TUCost)                │   Failed    │
   ┌──────────────┐                        └─────────────┘
   │  Completed   │
   └──────────────┘
```

States (`TacticalActionState`):
- `Pending`: Registered with the resolver, awaiting initial tick or queued behind another action.
- `Executing`: Active action currently progressing on the timeline.
- `Completed`: Action reached `ExecutionProgress >= TUCost`.
- `Cancelled`: Action was aborted before completion.
- `Failed`: Action threw an exception during execution.

#### 2.1.5 Strict Determinism & Reproducibility
- To guarantee determinism across runs, platforms, and thread scheduling:
  - Turn resolution is single-threaded and sequentially evaluates active actors in a stable sort order (e.g. sorted by `ActorId`, then `Action.Id`).
  - No unordered collection iteration (such as standard `Dictionary.Values` / `HashSet`) is allowed to dictate execution order.
  - Given an initial state and identical scheduled actions/tick deltas, state and events are bit-for-bit reproducible.

#### 2.1.6 Event Emission & Decoupled Observability
Events enable external systems (ballistics triggers, physiological updates, UI animations, logging) to react cleanly without tight coupling:
- `ActionScheduled(TacticalAction action, float globalTime)`
- `ActionStarted(TacticalAction action, float globalTime)`
- `ActionProgressed(TacticalAction action, float dt, float executionProgress, float globalTime)`
- `ActionCompleted(TacticalAction action, float globalTime)`
- `ActionCancelled(TacticalAction action, float globalTime)`
- `TimeAdvanced(float previousTime, float newTime, float dt)`

#### 2.1.7 Robust Error Handling & Input Validation
- **Validation**:
  - `dt <= 0`, `float.IsNaN(dt)`, `float.IsInfinity(dt)` $\to$ `ArgumentException` / `ArgumentOutOfRangeException`.
  - `action == null` $\to$ `ArgumentNullException`.
  - `action.TUCost <= 0` $\to$ `ArgumentException("TUCost must be strictly positive.")`.
  - `action.ActorId == Guid.Empty` $\to$ `ArgumentException("ActorId cannot be empty.")`.
- **Fault Isolation**:
  - If a specific `TacticalAction.Execute(dt)` throws, the turn resolver catches the exception, transitions the action to `TacticalActionState.Failed`, records `Exception`, fires an `ActionFailed` event, and continues ticking remaining concurrent actors without corrupting the global timeline.

---

### 2.2 Detailed Class and Interface Design

```
TacticalSim.Core/
└── Simulation/
    ├── TacticalActionState.cs    // Enum: Pending, Executing, Completed, Cancelled, Failed
    ├── TacticalAction.cs         // Base abstract class with full lifecycle & progress tracking
    ├── ITurnResolver.cs          // Interface with timeline, scheduling, cancellation, queries, events
    ├── TurnResolver.cs           // Concrete deterministic simultaneous turn resolver implementation
    ├── TurnResolverEvents.cs     // EventArgs for all turn resolver lifecycle events
    └── Actions/                  // Concrete standard tactical actions for simulation & testing
        ├── GenericTacticalAction.cs // Delegate-backed action
        ├── MoveTacticalAction.cs    // Position interpolation over TUs
        ├── AimTacticalAction.cs     // Precision ramp-up over TUs
        └── WaitTacticalAction.cs    // Idle delay action
```

#### Proposed `TacticalAction.cs` Signature:
```csharp
namespace TacticalSim.Core.Simulation
{
    public enum TacticalActionState
    {
        Pending,
        Executing,
        Completed,
        Cancelled,
        Failed
    }

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

        /// <summary>
        /// Advances the action execution by fractionated timesteps.
        /// </summary>
        public abstract void Execute(float dt);

        public virtual void OnStart() { }
        public virtual void OnComplete() { }
        public virtual void OnCancel() { }
        public virtual void OnFail(Exception ex) { }
    }
}
```

#### Proposed `ITurnResolver.cs` Signature:
```csharp
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

#### Proposed DI Registration (`TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`):
```csharp
namespace TacticalSim.Core.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTacticalSimulation(this IServiceCollection services)
        {
            services.AddSingleton<ITurnResolver, TurnResolver>();
            return services;
        }
    }
}
```

---

### 2.3 Proposed Test Coverage Matrix (`TacticalSim.Tests/TurnResolverTests.cs`)

1. **Deterministic Timeline Advancement**:
   - Verify `GlobalTime` starts at 0.0 and increments accurately across multiple fractionated `Tick(dt)` steps.
   - Reject invalid `dt` ($\le 0$, `NaN`, `Infinity`).
2. **Single Actor Action Execution**:
   - Schedule action with $TU = 1.0$, tick $0.5 \to$ progress = $0.5$, state = `Executing`.
   - Tick $0.5 \to$ progress = $1.0$, state = `Completed`, `IsComplete = true`.
3. **Multi-Actor Simultaneous Execution**:
   - Actor 1 ($TU = 1.0$), Actor 2 ($TU = 1.0$), Actor 3 ($TU = 2.0$).
   - Tick $1.0 \to$ Actor 1 and 2 complete simultaneously; Actor 3 is at 50% progress ($1.0 / 2.0$).
4. **Fractionated Sub-Tick Interleaving**:
   - Actor A has Action A1 ($TU = 0.3$) and queued Action A2 ($TU = 0.5$).
   - Actor B has Action B1 ($TU = 1.0$).
   - Tick $0.5 \to$ Action A1 completes at +0.3 TU; Action A2 starts and consumes remaining 0.2 TU (progress = 0.2/0.5). Action B1 reaches 0.5/1.0.
5. **Action Queueing & FIFO Ordering**:
   - Schedule multiple actions for the same actor; verify sequential execution as prior actions complete.
6. **Action Cancellation**:
   - Cancel currently executing action $\to$ transitions to `Cancelled`, dequeues next pending action.
   - Cancel all actions for an actor $\to$ clears active and queued actions for that actor.
7. **Event Emission Verification**:
   - Verify all events (`ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `TimeAdvanced`) fire with exact timestamps and arguments in correct temporal sequence.
8. **Fault Tolerance / Exception Handling**:
   - Action throwing exception inside `Execute` transitions to `Failed`; resolver continues executing other concurrent actors.
9. **DI Integration Verification**:
   - Build `ServiceCollection`, register `AddTacticalSimulation()`, resolve `ITurnResolver` via `ServiceProvider`, execute simulation ticks.

---

## 3. Caveats
- `TacticalSim.Core/TurnResolution.cs` currently has stub definitions that should be evolved in-place to preserve backwards compatibility while adding the full lifecycle and event infrastructure.
- In multi-actor actions that interact with spatial geometry (e.g. moving actors crossing paths), spatial collision is handled by higher-level movement validation; the Turn Resolver is strictly responsible for temporal scheduling, state transitions, and fractionated TU advancement.

---

## 4. Conclusion
The requirements for Issue #3 (Fractionated TU Turn Resolver) are fully mapped. The system requires:
1. `TurnResolver` implementing `ITurnResolver` with deterministic multi-entity scheduling, per-actor queues, and fractionated sub-tick interleaving.
2. `TacticalAction` lifecycle state machine (`Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`) with clamped floating-point progress and precision safeguards.
3. Event-driven decoupled observability (`ActionEventArgs`, `TimeAdvancedEventArgs`).
4. DI service registration via `Microsoft.Extensions.DependencyInjection`.
5. Comprehensive xUnit test suite validating all concurrency, interleaving, cancellation, and determinism properties.

---

## 5. Verification Method

To verify the survey findings and ensure the repository baseline is valid:
1. **Run Current Test Suite**:
   ```bash
   dotnet test
   ```
   *Expected*: 2 tests pass in `TacticalSim.Tests`.
2. **Inspect Scaffolding**:
   Check `TacticalSim.Core/TurnResolution.cs` to confirm existing stubs and compatibility requirements.
3. **Verify DI Package Reference**:
   Check `TacticalSim.Core/TacticalSim.Core.csproj` to confirm `Microsoft.Extensions.DependencyInjection` is present.
