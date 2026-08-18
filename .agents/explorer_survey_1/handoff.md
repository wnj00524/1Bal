# Codebase Survey and Technical Architecture Report

## 1. Observation

### 1.1 Project Structure and Build Configuration
The solution is located at `c:\Users\jdwil\source\repos\Codex\1bal` using the new XML solution format `TacticalSim.slnx`:
```xml
<Solution>
  <Project Path="TacticalSim.Core/TacticalSim.Core.csproj" />
  <Project Path="TacticalSim.Tests/TacticalSim.Tests.csproj" />
</Solution>
```

- **Target Framework**: `.NET 8.0` (`net8.0`) across all projects.
- **Language Settings**: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **TacticalSim.Core Dependencies**:
  - `Microsoft.Extensions.DependencyInjection` (Version 10.0.11)
- **TacticalSim.Tests Dependencies**:
  - `xunit` (Version 2.5.3)
  - `xunit.runner.visualstudio` (Version 2.5.3)
  - `Microsoft.NET.Test.Sdk` (Version 17.8.0)
  - `coverlet.collector` (Version 6.0.0)
  - Project reference to `TacticalSim.Core`

### 1.2 Existing Source Inventory

#### TacticalSim.Core:
1. `ActorPhysiology.cs` (`namespace TacticalSim.Core.Physiology`):
   - Defines `BodyPartType` enum (`Head`, `Thorax`, `Abdomen`, `LeftArm`, `RightArm`, `LeftLeg`, `RightLeg`).
   - Defines `BodyPart` class (lines 21-52). Line 24 contains property `public BodyPart Parent { get; set; }` which triggers compiler warning CS8618.
   - Defines `IActorPhysiology` interface (lines 58-74) with `TickPhysiology(float dt)` and `ProcessImpact(...)`.
2. `BallisticSolver.cs` (`namespace TacticalSim.Core.Ballistics`):
   - Defines `ProjectileState` struct (`Position`, `Velocity`, `Time`) using `System.Numerics.Vector3`.
   - Defines `BallisticProfile` struct (`Mass`, `CrossSectionalArea`, `IDragModel`).
   - Defines static class `BallisticSolver` with RK4 integrator `StepRK4(in ProjectileState state, in BallisticProfile profile, IEnvironmentModel environment, float dt)`. Calculates aerodynamic drag $F_d = \frac{1}{2} \rho v^2 C_d A$ and gravity.
3. `DragModels.cs` (`namespace TacticalSim.Core.Ballistics`):
   - Defines `IDragModel` interface (`float GetDragCoefficient(float mach)`).
   - Implements `StandardDragCurve` with transonic drag rise between Mach 0.8 and 1.2.
4. `Environment.cs` (`namespace TacticalSim.Core.Ballistics`):
   - Defines `EnvironmentState` struct (`WindVelocity`, `Gravity`, `AirDensity`, `SpeedOfSound`).
   - Defines `IEnvironmentModel` interface (`EnvironmentState GetConditionsAt(Vector3 position)`).
   - Implements `ICAOStandardAtmosphere` using barometric and adiabatic lapse equations.
5. `PhysiologicalVoxel.cs` (`namespace TacticalSim.Core.Physiology`):
   - Defines `TissueProperties` struct (`Density`, `Elasticity`, `ShearStrength`).
   - Defines `CavitationEvent` struct (`Origin`, `Radius`, `Energy`).
   - Defines `PhysiologicalVoxel` class with AABB slab ray intersection (`CalculateIntersectionDistance`), energy dissipation ($\Delta E = F_d \cdot d$), exit velocity calculation ($v = \sqrt{2 E_{rem} / m}$), and cavitation volume tracking.
6. `TissueRegistry.cs` (`namespace TacticalSim.Core.Physiology`):
   - Static registry providing `Muscle`, `Bone`, `Lung`, `Liver`, and `Brain` presets.
7. `TurnResolution.cs` (`namespace TacticalSim.Core.Simulation`):
   - Defines `TacticalAction` abstract class (lines 9-20) with `Guid ActorId`, `float TUCost`, `float ExecutionProgress`, `abstract void Execute(float dt)`, and `bool IsComplete => ExecutionProgress >= TUCost`.
   - Defines `ITurnResolver` interface (lines 25-41) with `float GlobalTime { get; }`, `void ScheduleAction(TacticalAction action);`, `void Tick(float dt);`.
   - **Note**: There is currently NO implementation of `ITurnResolver`.

#### TacticalSim.Tests:
1. `BallisticSolverTests.cs` (`namespace TacticalSim.Tests`):
   - Tests `VacuumTrajectory_FollowsParabolicKinematics` and `AtmosphericTrajectory_ExhibitsNonLinearDrag`.

### 1.3 Baseline Build and Test Execution
Execution of `dotnet test --verbosity normal`:
- Tests: 2 Passed, 0 Failed. Total time: ~1.9s.
- Compiler Warnings: 1 warning:
  `ActorPhysiology.cs(24,25): warning CS8618: Non-nullable property 'Parent' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the property as nullable.`

---

## 2. Logic Chain

### 2.1 Requirements Analysis from ORIGINAL_REQUEST.md and agents.md
1. **Issue #3: Fractionated TU Turn Resolver**:
   - Must implement a simultaneous turn resolution system that manages a global timeline (`GlobalTime`).
   - Must schedule concurrent actions from multiple entities (`ScheduleAction(TacticalAction)`).
   - Must advance their execution states concurrently in fractionated Time Unit (`TU`) increments (`Tick(float dt)`).
   - Must handle action lifecycle (advancement until `IsComplete`, removal/completion notification, multi-actor interleaving).

2. **Issue #4: Material Penetration System**:
   - Must implement environmental cover material penetration (terminal ballistics).
   - Materials required: at least Wood, Concrete, Steel (with defined densities $\rho$, resistance / thickness metrics).
   - Mathematical model: Calculate projectile velocity loss and kinetic energy transfer during intersection based on material density $\rho$, cross-sectional area $A$, drag coefficient / resistance factor, and penetration thickness $d$:
     - Initial kinetic energy: $E_0 = \frac{1}{2} m v_0^2$
     - Drag / penetration resistance force: $F_{\text{drag}} = \frac{1}{2} \rho_{\text{material}} v^2 C_d A$ (or material resistance work)
     - Energy lost through thickness $d$: $\Delta E = F_{\text{drag}} \cdot d$
     - Exit energy: $E_{\text{exit}} = \max(0, E_0 - \Delta E)$
     - Exit velocity: $v_{\text{exit}} = \sqrt{\frac{2 E_{\text{exit}}}{m}}$
     - Energy transfer to material: $E_{\text{transferred}} = E_0 - E_{\text{exit}}$
     - If $E_0 \le \Delta E$, the projectile is stopped / captured inside the material ($v_{\text{exit}} = 0$).
   - Integration: Can be designed as a dedicated terminal ballistics solver/service (e.g. `IMaterialPenetrationSolver` / `MaterialPenetrationSolver` or `MaterialProperties` + `MaterialRegistry`) and/or material cover voxel/volume representation.

3. **Architectural Decoupling and Dependency Injection (R3)**:
   - Must remain strictly decoupled inside `TacticalSim.Core`.
   - Must provide DI service registration using `Microsoft.Extensions.DependencyInjection` (e.g., `TacticalSimServiceExtensions.AddTacticalSimCore(...)`) to register turn resolver, ballistic solvers, drag models, environment models, and penetration solvers.

4. **Zero Compiler Warnings Requirement**:
   - ORIGINAL_REQUEST.md Acceptance Criteria: "The full solution (`dotnet build`) compiles without errors or warnings."
   - Fix required: `BodyPart.Parent` in `ActorPhysiology.cs:24` should be made nullable (`BodyPart? Parent`).

---

## 3. Caveats

- No UI or presentation engine exists or should be created; all simulation logic is isolated in `TacticalSim.Core`.
- Existing `PhysiologicalVoxel` addresses soft tissue cavitation for biological actors (Phase 2), whereas Issue #4 addresses environmental cover materials (Wood, Concrete, Steel) (Phase 1/Core Ballistics). The terminal ballistics physics for materials should be clean, modular, and reusable.
- The `TacticalAction` abstract class defines `public abstract void Execute(float dt);`. Concrete actions for test scenarios (e.g., `MoveAction`, `AimAction`, `FireAction`, or mock action) need to increment `ExecutionProgress += dt` (or appropriate fractionated TU delta).

---

## 4. Conclusion & Architectural Blueprint

### 4.1 Recommended Design for Issue #3 (Turn Resolver)
- **Class**: `TurnResolver : ITurnResolver` in `TacticalSim.Core.Simulation` (or `TacticalSim.Core.Simulation.TurnResolver`).
- **State**:
  - `private float _globalTime;`
  - `private readonly List<TacticalAction> _activeActions;` (thread-safe or deterministic collection)
- **Methods**:
  - `GlobalTime => _globalTime;`
  - `ScheduleAction(TacticalAction action)`: validates and adds to active actions.
  - `Tick(float dt)`:
    1. Advances `_globalTime += dt`.
    2. Iterates over active actions snapshot and invokes `action.Execute(dt)`.
    3. Prunes completed actions (`action.IsComplete`).
    4. Optional events/callbacks: `ActionCompleted`, `TurnTicked` for decoupled observability.

### 4.2 Recommended Design for Issue #4 (Material Penetration System)
- **Namespace**: `TacticalSim.Core.Ballistics` (or `TacticalSim.Core.Ballistics.Materials`)
- **Data Structures**:
  - `MaterialProperties`:
    - `string Name`
    - `float Density` ($\text{kg/m}^3$) — e.g., Wood $\approx 600\text{ kg/m}^3$, Concrete $\approx 2400\text{ kg/m}^3$, Steel $\approx 7850\text{ kg/m}^3$.
    - `float ResistanceFactor` or specific drag coefficient $C_d$.
  - `MaterialRegistry`:
    - Static or injectable registry offering standard presets: `Wood`, `Concrete`, `Steel`, `BallisticGlass`, `Kevlar`.
  - `PenetrationResult`:
    - `bool Penetrated`
    - `float ExitSpeed` ($v_{\text{exit}}$)
    - `Vector3 ExitVelocity`
    - `float EnergyLost` ($\Delta E$)
    - `float RemainingEnergy` ($E_{\text{exit}}$)
    - `float PenetrationDepth` (actual depth traversed or stopped depth)
- **Interface & Implementation**:
  - `IMaterialPenetrationSolver`:
    - `PenetrationResult CalculatePenetration(in ProjectileState projectile, in BallisticProfile profile, MaterialProperties material, float thicknessMeters)`
  - `MaterialPenetrationSolver : IMaterialPenetrationSolver`:
    - Implements hydrodynamic/ballistic resistance calculation based on density $\rho$, thickness $d$, mass $m$, and area $A$.

### 4.3 Dependency Injection Setup
- **Class**: `ServiceCollectionExtensions` (in `TacticalSim.Core`)
  - `public static IServiceCollection AddTacticalSimCore(this IServiceCollection services)`
  - Registers:
    - `ITurnResolver` $\rightarrow$ `TurnResolver` (Transient or Singleton / Scoped)
    - `IMaterialPenetrationSolver` $\rightarrow$ `MaterialPenetrationSolver` (Singleton / Transient)
    - `IDragModel` $\rightarrow$ `StandardDragCurve`
    - `IEnvironmentModel` $\rightarrow$ `ICAOStandardAtmosphere` (with standard defaults)

### 4.4 Test Coverage Blueprint in TacticalSim.Tests
1. `TurnResolverTests.cs`:
   - `ScheduleAction_IncreasesActiveActionCount`
   - `Tick_AdvancesGlobalTime_ByFractionatedStep`
   - `Tick_ExecutesMultipleConcurrentActions_Interleaved`
   - `Tick_CompletesActions_WhenTUCostReached`
   - `SimultaneousActions_WithDifferentTUCosts_CompleteAtExpectedGlobalTimes`
2. `MaterialPenetrationTests.cs`:
   - `Wood_AllowsHighVelocityPenetration_WithCalculatedVelocityLoss`
   - `Concrete_CausesHigherVelocityLoss_ThanWood`
   - `ThickSteel_CompletelyStopsProjectile_ZeroExitVelocity`
   - `KineticEnergyTransfer_MatchesTheoreticalDelta`
   - `PenetrationLoss_ProportionalToDensityAndThickness`
3. `DependencyInjectionTests.cs`:
   - `AddTacticalSimCore_ResolvesAllRequiredServices`

---

## 5. Verification Method

To verify the codebase and future implementations independently:

1. **Build Solution (with zero warnings/errors)**:
   ```pwsh
   dotnet build --configuration Debug
   ```
   *Expected*: Build succeeded with 0 Warning(s) and 0 Error(s).

2. **Run All xUnit Tests**:
   ```pwsh
   dotnet test --verbosity normal
   ```
   *Expected*: All tests pass (including existing ballistic tests and new turn resolver + material penetration tests).

3. **Verify DI Container Resolution**:
   Execute programmatic xUnit tests creating a `ServiceCollection`, invoking `AddTacticalSimCore()`, building the `ServiceProvider`, and resolving `ITurnResolver` and `IMaterialPenetrationSolver`.
