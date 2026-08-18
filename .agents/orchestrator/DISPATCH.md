# Dispatch Log

## 2026-08-18T12:35:52Z
Implement Issue #3: Fractionated TU Turn Resolver in TacticalSim.Core.
- Simultaneous turn resolution system managing a global timeline.
- Schedule concurrent actions from multiple entities, advance execution state based on fractionated TU increments.
- Physiological Integration: Invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation as timeline advances.
- Architectural Decoupling: Strict isolation in `TacticalSim.Core`, DI registration via `Microsoft.Extensions.DependencyInjection`, adhering to `agents.md`.
- Acceptance Criteria: Comprehensive programmatic xUnit tests in `TacticalSim.Tests`, clean build (`dotnet build`), all tests pass (`dotnet test`).
