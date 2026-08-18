# E2E Test Infra: TacticalSim

## Test Philosophy
- Opaque-box, requirement-driven derived strictly from `ORIGINAL_REQUEST.md`.
- Comprehensive multi-tier verification:
  - **Tier 1 - Feature Coverage**: Direct validation of each feature in isolation (>=5 per feature).
  - **Tier 2 - Boundary & Corner Cases**: Limits, extreme densities, zero/fractional steps, ultra-high velocities, grazing angles (>=5 per feature).
  - **Tier 3 - Cross-Feature Combinations**: Pairwise interactions (e.g. concurrent turn resolution scheduling ballistic shots through layered materials).
  - **Tier 4 - Real-World Application Scenarios**: Multi-actor combat firefight through complex cover environments (Wood, Concrete, Steel) over discrete fractionated time steps.

## Feature Inventory Coverage Matrix
| # | Feature | Source | Tier 1 | Tier 2 | Tier 3 | Tier 4 |
|---|---------|--------|:------:|:------:|:------:|:------:|
| F1 | Global Simulation Timeline | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F2 | Concurrent Multi-Entity Scheduling | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F3 | Fractionated TU Advancement & Sub-Stepping | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F4 | Tactical Action Lifecycle State Machine | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F5 | Turn Resolver Observability Events | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F6 | Environmental Cover Material Properties | Issue #4 / R2 | 5 | 5 | ✓ | ✓ |
| F7 | Material Registry | Issue #4 / R2 | 5 | 5 | ✓ | ✓ |
| F8 | Terminal Ballistics Penetration Physics | Issue #4 / R2 | 5 | 5 | ✓ | ✓ |
| F9 | Penetration Outcome Classification | Issue #4 / R2 | 5 | 5 | ✓ | ✓ |
| F10 | Dependency Injection Service Registration | R3 | 5 | 5 | ✓ | ✓ |
| F11 | Zero-Warning Codebase Hygiene | AC | 1 | 1 | ✓ | ✓ |

## Test Architecture
- **Test Framework**: xUnit (`net8.0`)
- **Test Runner Command**: `dotnet test --configuration Debug --verbosity normal`
- **Pass / Fail Semantics**: Zero test failures, zero build errors, zero build warnings.
- **Directory Layout**:
  - `TacticalSim.Tests/TurnResolverTests.cs` (Unit & lifecycle tests for Issue #3)
  - `TacticalSim.Tests/MaterialPenetrationTests.cs` (Unit & ballistics physics tests for Issue #4)
  - `TacticalSim.Tests/DependencyInjectionTests.cs` (DI container resolution tests for R3)
  - `TacticalSim.Tests/E2ETacticalSimulationTests.cs` (Tier 3 & Tier 4 integration & scenario tests)

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Multi-Actor Breach & Clear Firefight | F1, F2, F3, F4, F5, F8, F9, F10 | High |
| 2 | Heavy Weapon Material Penetration Through Layered Barricade (Wood + Concrete + Steel) | F6, F7, F8, F9, F10 | High |
| 3 | Concurrent Snipers Shooting Through Glass & Wall with Fractionated Reaction Interleaving | F1, F2, F3, F4, F8, F9 | High |
| 4 | Suppressive Fire Sequence with Action Interruption & Cancellation | F1, F2, F3, F4, F5 | Medium |
| 5 | Calibrated Velocity Loss & Kinetic Energy Decay Curve Across Variable Calibers | F6, F7, F8, F9 | Medium |
