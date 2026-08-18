## 2026-08-17T21:24:25Z
You are the Worker for Milestone 1: Fractionated TU Turn Resolver in TacticalSim.
Working directory: c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m1_1
Project root: c:\Users\jdwil\source\repos\Codex\1bal

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Context & Reference Documents:
- Read c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md
- Read c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md
- Read c:\Users\jdwil\source\repos\Codex\1bal\.agents\sub_orch_m1\SCOPE.md

Exclusive Write Ownership:
You own and may ONLY create/modify the following files:
- TacticalSim.Core/Simulation/TacticalActionState.cs
- TacticalSim.Core/Simulation/TacticalAction.cs
- TacticalSim.Core/Simulation/ITurnResolver.cs
- TacticalSim.Core/Simulation/TurnResolver.cs
- TacticalSim.Core/Simulation/TurnResolverEvents.cs
- TacticalSim.Core/Simulation/Actions/GenericTacticalAction.cs
- TacticalSim.Core/Simulation/Actions/MoveTacticalAction.cs
- TacticalSim.Core/Simulation/Actions/AimTacticalAction.cs
- TacticalSim.Core/Simulation/Actions/WaitTacticalAction.cs
- TacticalSim.Tests/TurnResolverTests.cs

DO NOT touch any other source or test files.

Requirements:
1. Implement TacticalSim.Core/Simulation/TacticalActionState.cs:
   Enum: Pending, Executing, Completed, Cancelled, Failed
2. Implement TacticalSim.Core/Simulation/TacticalAction.cs:
   Abstract class matching interface contract in PROJECT.md:
   - Guid Id { get; set; } = Guid.NewGuid()
   - Guid ActorId { get; set; }
   - float TUCost { get; set; }
   - float ExecutionProgress { get; set; }
   - TacticalActionState State { get; internal set; } = TacticalActionState.Pending
   - abstract void Execute(float dt);
   - bool IsComplete => State == TacticalActionState.Completed || ExecutionProgress >= TUCost;
   - Validation & precision handling.
3. Implement TacticalSim.Core/Simulation/TurnResolverEvents.cs:
   Strongly typed event args:
   - ActionEventArgs (Action)
   - ActionProgressEventArgs (Action, DeltaTime, CurrentProgress, TotalCost)
   - ActionFailedEventArgs (Action, Exception or ErrorMessage)
   - TimeAdvancedEventArgs (DeltaTime, CurrentGlobalTime)
4. Implement TacticalSim.Core/Simulation/ITurnResolver.cs:
   Interface matching PROJECT.md.
5. Implement TacticalSim.Core/Simulation/TurnResolver.cs:
   - Full implementation of ITurnResolver.
   - GlobalTime starting at 0.0f, strictly monotonic.
   - Per-actor FIFO action queues.
   - Concurrent execution across multiple actors. Deterministic processing order (e.g. ActorId ordering).
   - Fractionated TU advancement in Tick(float dt):
     * Validates dt > 0.
     * Iterates through active actors. For each actor's current action, advances by dt.
     * If an action completes with leftover sub-tick time (dtRemaining = dt - (TUCost - currentProgress)), sets State = Completed, fires ActionCompleted, then immediately transitions to the next queued action for that actor (State = Executing, fires ActionStarted) and advances it with dtRemaining. This sub-tick carryover continues if multiple short actions finish within a single Tick(dt).
     * Fires ActionProgressed during active execution.
     * GlobalTime advances by dt after all actor processing is complete, firing TimeAdvanced.
   - CancelAction(Guid actionId) and CancelActorActions(Guid actorId) marking state Cancelled and firing ActionCancelled.
   - Exception isolation: if an action throws during Execute(dt), mark State = Failed, fire ActionFailed, remove/transition appropriately without crashing the whole resolver.
   - Reset() resets GlobalTime = 0, clears all queues and active actions.
6. Implement Concrete Actions in TacticalSim.Core/Simulation/Actions/:
   - GenericTacticalAction.cs: with Action<float>? callback or similar custom execution logic.
   - MoveTacticalAction.cs: movement action with destination vector / distance / speed.
   - AimTacticalAction.cs: aiming action with target ID / aim bonus progression.
   - WaitTacticalAction.cs: simple wait/delay action.
7. Implement Comprehensive Unit Tests in TacticalSim.Tests/TurnResolverTests.cs:
   - High coverage xUnit tests covering all features: single actor lifecycle, multi-actor concurrent execution, fractionated sub-stepping with multi-action carryover within one tick, cancellation, event propagation for all 7 event types, exception/failure isolation, reset, zero/negative dt validation, boundary conditions, float precision tolerance.
8. Build & Verification:
   - Run `dotnet build` and ensure 0 errors and 0 warnings.
   - Run `dotnet test` and ensure all tests pass (100% pass rate).
9. Output:
   - Write your progress and detailed handoff report to `c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m1_1\handoff.md`.
   - Send completion message to parent when done.
