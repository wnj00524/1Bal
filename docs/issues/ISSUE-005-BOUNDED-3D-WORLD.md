# Issue #5 — Implement Bounded 3D World

## Status: Ready for Implementation

## Summary

Add a bounded 3D simulation world to `TacticalSim.Core` large enough to hold a UK detached house and surrounding tactical area. Refactor entity management out of `TurnResolver` into the new `TacticalWorld`, making it the single spatial authority for the simulation.

## Background

Currently, entities in the simulation have a `Vector3 Position` property but exist in unbounded space. There is no world container, no boundary enforcement, and no central spatial authority. Cover surfaces (`CoverPolygon`) also exist independently with no container. Entity registration is currently handled by `TurnResolver`, mixing temporal (action scheduling) and spatial (entity management) concerns.

This issue introduces:
1. A **bounded 3D world** with defined extents
2. A **`TacticalWorld`** class as the single spatial authority
3. A **clean separation** of entity management (world) from action scheduling (turn resolver)

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Coordinate system | Y-up, metres | Consistent with existing `ICAOStandardAtmosphere` which treats `position.Y` as altitude |
| Boundary shape | AABB (axis-aligned bounding box) | Simple, efficient, natural fit for rectangular plots |
| Default dimensions | 100m × 100m × 30m | Fits house + garden + street + tactical approaches; 30m handles 3-storey + roof |
| Origin | Centre XZ, ground at Y=0 | Bounds: `(-50, 0, -50)` to `(50, 30, 50)`. Ground at Y=0 is intuitive |
| Boundary enforcement | Entities clamped to bounds | Prevents entities leaving simulation space |
| Entity ownership | Moved from `TurnResolver` to `TacticalWorld` | Clean separation of spatial and temporal concerns |
| Static geometry | `CoverPolygon` collections in `TacticalWorld` | Reuses existing cover/material system |
| Interface | `ITacticalWorld` | Follows project pattern (`ITurnResolver`, `IEntity`, etc.) |
| DI registration | Singleton `ITacticalWorld`, Transient `ITurnResolver` | One world per simulation, resolvers are lightweight |
| File location | `TacticalSim.Core/World/` | Follows existing feature-based directory pattern |

## Task Breakdown

This issue is broken into **7 tasks** that must be completed **in order**. Each task has its own detailed document in `docs/issues/tasks/`.

| # | Task | GitHub Issue | File | Dependencies | Estimated Effort |
|---|------|--------------|------|-------------|------------------|
| 1 | Create `WorldBounds` value type | [#36](https://github.com/wnj00524/1Bal/issues/36) | [TASK-01-WORLD-BOUNDS.md](tasks/TASK-01-WORLD-BOUNDS.md) | None | Small |
| 2 | Create `ITacticalWorld` interface | [#37](https://github.com/wnj00524/1Bal/issues/37) | [TASK-02-ITACTICAL-WORLD.md](tasks/TASK-02-ITACTICAL-WORLD.md) | Task 1 | Small |
| 3 | Create `TacticalWorld` implementation | [#38](https://github.com/wnj00524/1Bal/issues/38) | [TASK-03-TACTICAL-WORLD.md](tasks/TASK-03-TACTICAL-WORLD.md) | Tasks 1, 2 | Medium |
| 4 | Refactor `ITurnResolver` and `TurnResolver` | [#39](https://github.com/wnj00524/1Bal/issues/39) | [TASK-04-REFACTOR-TURN-RESOLVER.md](tasks/TASK-04-REFACTOR-TURN-RESOLVER.md) | Task 3 | Medium |
| 5 | Update DI registration | [#40](https://github.com/wnj00524/1Bal/issues/40) | [TASK-05-UPDATE-DI.md](tasks/TASK-05-UPDATE-DI.md) | Task 4 | Small |
| 6 | Create `WorldTests.cs` | [#41](https://github.com/wnj00524/1Bal/issues/41) | [TASK-06-WORLD-TESTS.md](tasks/TASK-06-WORLD-TESTS.md) | Tasks 1, 2, 3 | Medium |
| 7 | Migrate all existing tests | [#42](https://github.com/wnj00524/1Bal/issues/42) | [TASK-07-MIGRATE-TESTS.md](tasks/TASK-07-MIGRATE-TESTS.md) | Tasks 4, 5 | Large (mechanical) |

## Acceptance Criteria

- [ ] `dotnet build` compiles with zero errors and zero warnings
- [ ] `dotnet test` passes all existing tests (416+) plus new world tests
- [ ] `TacticalSim.Core` has zero references to UI/Godot/presentation libraries (architecture tests pass)
- [ ] `ITurnResolver` no longer contains entity management methods
- [ ] All entity registration goes through `ITacticalWorld`
- [ ] World bounds default to `(-50, 0, -50)` to `(50, 30, 50)`
- [ ] Entity positions are clamped to world bounds on add and move

## Verification

```bash
cd c:\Users\Shadow\source\repos\1bal
dotnet build
dotnet test
```

Both commands must exit with code 0.
