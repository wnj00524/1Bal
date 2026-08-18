# E2E Test Infra: TacticalSim - Turn Resolver & Physiological Integration

## Test Philosophy
- Opaque-box, requirement-driven derived strictly from `ORIGINAL_REQUEST.md` and `PROJECT.md`.
- Comprehensive 4-Tier verification:
  - **Tier 1 - Feature Coverage**: Direct validation of all core simulation features in isolation (>=5 test cases per feature).
  - **Tier 2 - Boundary & Corner Cases**: Limits, extreme delta times, micro-steps ($10^{-6}\text{ s}$), exact-match TU costs, carryover queue exhaustion, zero-bleed vitals baseline, hyper-massive fatal hemorrhage, 7200s tourniquet ischemia necrosis threshold, entity registration churn (>=5 test cases per feature).
  - **Tier 3 - Cross-Feature Combinations**: Pairwise multi-system interactions (e.g. concurrent multi-actor action execution alongside dynamic physiological hemorrhage, limb tourniquet application during movement, mid-tick lethal trauma cancellation, action failure isolation).
  - **Tier 4 - Real-World Application Scenarios**: Multi-entity combat scenarios (squad bounding maneuvers, ambush crossfires with ballistic cover penetration and tourniquet treatment, casualty extraction under active suppression, counter-sniper engagements with decompensation, CQB room clearing).

---

## Feature Inventory Coverage Matrix

| # | Feature | Source | Tier 1 | Tier 2 | Tier 3 | Tier 4 |
|---|---------|--------|:------:|:------:|:------:|:------:|
| F1 | Global Simulation Timeline | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F2 | Concurrent Multi-Actor Scheduling | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F3 | Fractionated TU Sub-Stepping | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F4 | Sub-Tick Carryover Interleaving | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F5 | Action Lifecycle State Machine | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F6 | Action Cancellation (Single & Bulk) | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F7 | Fault Isolation & Failure State | Issue #3 / R1 | 5 | 5 | ✓ | ✓ |
| F8 | Entity Management in TurnResolver | Issue #3 / R2 | 5 | 5 | ✓ | ✓ |
| F9 | Physiological Ticking Integration | Issue #3 / R2 | 5 | 5 | ✓ | ✓ |
| F10 | Dependency Injection Registration | R3 | 5 | 5 | ✓ | ✓ |

---

## Test Architecture
- **Test Framework**: xUnit (`net8.0`)
- **Test Runner Command**: `dotnet test TacticalSim.slnx --verbosity normal`
- **Pass / Fail Semantics**: 0 test failures, 0 compiler warnings, 0 compiler errors.
- **Directory Layout**:
  - `TacticalSim.Tests/TurnResolverE2ETieredTests.cs` (Comprehensive 4-Tier Test Suite)
  - `TacticalSim.Tests/TurnResolverTests.cs` (Timeline & Action Lifecycle Unit Tests)
  - `TacticalSim.Tests/TurnResolverPhysiologyTests.cs` (Dedicated Physiological Integration Tests)
  - `TacticalSim.Tests/TurnResolverStressTests.cs` (Stress & Concurrency Tests)
  - `TacticalSim.Tests/TurnResolverAdversarialTests.cs` (Adversarial Invariant & Fuzzing Tests)
  - `TacticalSim.Tests/E2ETacticalSimulationTests.cs` (End-to-End Ballistic & Simulation Tests)
  - `TacticalSim.Tests/DependencyInjectionTests.cs` (DI Container Tests)

---

## Real-World Application Scenarios (Tier 4)

| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Squad Bounding Maneuver with Concurrent Movement & Suppressive Aim | F1, F2, F3, F4, F5 | High |
| 2 | Ambush Crossfire with Simultaneous Ballistics, Cover Penetration, Trauma, and Tourniquet | F1, F2, F3, F5, F6, F8, F9 | High |
| 3 | Multi-Phase Combat Encounter with Bleeding Casualty Extraction | F1, F2, F3, F5, F6, F8, F9 | High |
| 4 | Counter-Sniper Urban Engagement with Layered Cover and Decompensation | F1, F2, F3, F5, F6, F8, F9 | High |
| 5 | CQB Room Clearing with Staggered Breach, Ballistics, and Trauma Management | F1, F2, F3, F5, F8, F9 | High |
