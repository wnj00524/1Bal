# Scope: Milestone 1 — Fractionated TU Turn Resolver

## Objective
Implement Issue #3 (Fractionated TU Turn Resolver) within `TacticalSim.Core.Simulation` and comprehensive unit tests in `TacticalSim.Tests/TurnResolverTests.cs`.

## Exclusive Write Ownership
The worker(s) in this milestone own and may modify ONLY:
- `TacticalSim.Core/Simulation/TacticalActionState.cs`
- `TacticalSim.Core/Simulation/TacticalAction.cs`
- `TacticalSim.Core/Simulation/ITurnResolver.cs`
- `TacticalSim.Core/Simulation/TurnResolver.cs`
- `TacticalSim.Core/Simulation/TurnResolverEvents.cs`
- `TacticalSim.Core/Simulation/Actions/GenericTacticalAction.cs`
- `TacticalSim.Core/Simulation/Actions/MoveTacticalAction.cs`
- `TacticalSim.Core/Simulation/Actions/AimTacticalAction.cs`
- `TacticalSim.Core/Simulation/Actions/WaitTacticalAction.cs`
- `TacticalSim.Tests/TurnResolverTests.cs`

## Key Requirements
1. Global timeline tracking `GlobalTime` starting at 0.0, strictly monotonic.
2. Concurrent multi-entity scheduling (`ScheduleAction(TacticalAction)`), supporting multiple distinct actors (`Guid ActorId`).
3. Per-actor FIFO queuing: If an actor already has an active action, new actions are queued.
4. Fractionated TU advancement (`Tick(float dt)`): Execute active actions by fractional $\Delta t$. If an action completes with leftover sub-tick time, carry over leftover $\Delta t$ into the actor's next queued action.
5. Full action lifecycle state machine (`Pending`, `Executing`, `Completed`, `Cancelled`, `Failed`).
6. Cancellation support: `CancelAction(Guid actionId)` and `CancelActorActions(Guid actorId)`.
7. Observability events: `ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `ActionFailed`, `TimeAdvanced`.
8. Deterministic sequential execution order (e.g. ordered by `ActorId`).
9. Precision safeguards: Epsilon tolerance on float comparisons, clamped `ExecutionProgress` at `TUCost`.
10. Robust argument validation (`dt > 0`, `TUCost > 0`, non-empty `ActorId`, null checks).
11. Programmatic xUnit tests covering single actor, multi-actor concurrent execution, sub-tick carryover interleaving, cancellation, events, and failure isolation.

## References
- `c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\.agents\explorer_survey_2\handoff.md`
