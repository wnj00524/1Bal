# Exploration & Codebase Survey Report: TacticalSim Architecture & Physiology Integration

## 1. Observation

### 1.1 Requirements & Directives
- **Authoritative Request File**: `ORIGINAL_REQUEST.md` (lines 35-66)
  - **Issue #3 / R1 (Fractionated TU Turn Resolver)**: Simultaneous turn resolution system managing a global timeline, scheduling concurrent actions across multiple entities, and advancing execution state in fractionated Time Unit (TU) increments.
  - **Issue #3 Follow-up / R2 (Physiological Integration)**:
    > "The Turn Resolver must have a mechanism to invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation as the timeline advances, ensuring bleeding and ischemia effects resolve properly over the game's duration."
  - **R3 (Architectural Decoupling)**: Strict isolation within `TacticalSim.Core`, utilizing `Microsoft.Extensions.DependencyInjection` conforming to `agents.md`.
  - **Acceptance Criteria**: xUnit test verification in `TacticalSim.Tests` for concurrent action interleaving, `TickPhysiology` invocation on entities during turn progression, zero compiler warnings/errors, and 100% test pass rate.

### 1.2 Entity and Actor Models
- **`IEntity` interface** (`TacticalSim.Core/Entities/IEntity.cs`, lines 7-13):
  ```csharp
  public interface IEntity
  {
      Guid Id { get; }
      Vector3 Position { get; set; }
      IActorPhysiology Physiology { get; }
      WeaponProfile? EquippedWeapon { get; set; }
  }
  ```
- **`TacticalEntity` class** (`TacticalSim.Core/Entities/TacticalEntity.cs`, lines 7-20):
  - Implements `IEntity`.
  - Constructor: `public TacticalEntity(Vector3 position, IActorPhysiology physiology)` initializes `Id = Guid.NewGuid()`, sets `Position`, `Physiology`, and optional `EquippedWeapon`.
- **`WeaponProfile` & `AmmunitionProfile` records** (`TacticalSim.Core/Entities/`):
  - `WeaponProfile.cs` (lines 3-8): `Name`, `LoadedAmmunition`, `BaseTUCostToFire` (defaults to `15f`).
  - `AmmunitionProfile.cs` (lines 5-11): `Name`, `MuzzleVelocity`, `Ballistics` (`BallisticProfile`).

### 1.3 Physiology Interface and State Machine
- **`IActorPhysiology` interface** (`TacticalSim.Core/ActorPhysiology.cs`, lines 103-111):
  ```csharp
  public interface IActorPhysiology
  {
      BodyPart RootBodyPart { get; }
      float TotalBloodVolume { get; }
      float ConsciousnessLevel { get; } // 0.0 to 1.0
      
      void TickPhysiology(float dt);
      void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint);
  }
  ```
- **`TacticalActorPhysiology` class** (`TacticalSim.Core/ActorPhysiology.cs`, lines 113-205):
  - Baseline state: `_baselineBloodVolume = 5000f` ml, `TotalBloodVolume = 5000f` ml, `HeartRateBpm = 80f`, `MeanArterialPressureMmhg = 93f`, `ConsciousnessLevel = 1.0f`, `CurrentHemorrhageClass = HemorrhageClass.Class1`.
  - **`TickPhysiology(float dt)` implementation** (lines 126-136):
    ```csharp
    public void TickPhysiology(float dt)
    {
        float totalBleedRate = CalculateBleedRate(RootBodyPart);
        if (totalBleedRate > 0)
        {
            TotalBloodVolume -= totalBleedRate * dt;
        }

        TickIschemia(RootBodyPart, dt);
        UpdateCardiovascularState();
    }
    ```
  - **Hemorrhage Calculation** (lines 138-144): Traverses `BodyPart` hierarchy recursively via `part.GetActiveBleedRate()`. In `BodyPart.cs` (lines 47-54): If `HasTourniquet && IsExtremity(Type)` returns `0f`, else returns `ArterialBleedRate + VenousBleedRate`.
  - **Tourniquet Ischemia Progression** (lines 146-158): Increments `part.IschemiaDuration += dt` when `HasTourniquet == true`. If `IschemiaDuration > 7200f` (2 hours), marks `part.IsNecrotic = true`.
  - **Cardiovascular & Consciousness State Machine** (`UpdateCardiovascularState`, lines 160-199):
    - `lostPercent < 0.15f` (Class 1): HR 80–100 bpm, MAP 93 mmHg, Consciousness 1.0.
    - `0.15f <= lostPercent < 0.30f` (Class 2): HR 100–120 bpm, MAP drops to 80 mmHg, Consciousness 0.9.
    - `0.30f <= lostPercent < 0.40f` (Class 3): HR 120–140 bpm, MAP drops to 60 mmHg, Consciousness 0.6.
    - `0.40f <= lostPercent < 0.50f` (Class 4): HR drops to 100 bpm (decompensation), MAP drops to 30 mmHg, Consciousness 0.2.
    - `lostPercent >= 0.50f` (Fatal): HR 0 bpm, MAP 0 mmHg, Consciousness 0.0 (Death).
- **`BodyPart`, `PhysiologicalVoxel`, `TissueRegistry`, `AnatomicalDummyBuilder`**:
  - `BodyPart` (`ActorPhysiology.cs`, lines 30-98): Tree structure of extremities and organs (`Thorax`, `Head`, `LeftArm`, `RightArm`, `LeftLeg`, `RightLeg`), contains `List<PhysiologicalVoxel> Voxels`.
  - `PhysiologicalVoxel` (`PhysiologicalVoxel.cs`, lines 24-204): 3D cubic voxels with `TissueProperties` and `OrganType`, processes penetration drag work $\Delta E_k$, temporary cavity expansion, and permanent cavity tissue destruction.
  - `AnatomicalDummyBuilder` (`AnatomicalDummyBuilder.cs`, lines 6-118): Generates dummy with thoracic organs (Bone, Heart, Lungs, Liver, Stomach, Muscle) via Signed Distance Field math.

### 1.4 Time, Time Units (TU), and Delta Time Representation
- **Standard Physics Units** (`agents.md`, lines 26-36):
  - Distance: Meters (m).
  - Mass: Kilograms (kg).
  - Time: Seconds (s) internally.
  - Hemorrhage / Bleed Rates: Milliliters per second (ml/s) internally, reported in ml/min.
- **Simulation Time Units (TU)**:
  - In `TacticalAction.cs` (lines 26-66): `TUCost`, `ExecutionProgress`, and `RemainingTU` are `float` values.
  - In `ITurnResolver.cs` and `TurnResolver.cs` (lines 15, 224-368): `GlobalTime` and `Tick(float dt)` operate in fractional floating-point units where $1 \text{ TU} = 1.0 \text{ s}$.
  - In `MoveTacticalAction.cs`: `MovementSpeed` is in meters/TU; `TUCost = Distance / MovementSpeed`.
  - In `AimTacticalAction.cs`: Bonus scales linearly with normalized execution progress ($P_{norm} = \text{ExecutionProgress} / \text{TUCost}$).
  - In `BallisticSolver.cs`: RK4 integration sub-steps use seconds `dt` (e.g. `0.01f` s or `0.00001f` s in Godot voxel collision).

### 1.5 Current Turn Resolver Implementation & Registration Gap
- **`ITurnResolver` / `TurnResolver`** (`TacticalSim.Core/Simulation/`):
  - Current members: `GlobalTime`, `HasActiveActions`, `ActiveActorCount`, `ScheduleAction(TacticalAction)`, `CancelAction(Guid)`, `CancelActorActions(Guid)`, `GetActiveActions()`, `GetQueuedActions(Guid)`, `GetCurrentAction(Guid)`, `Tick(float dt)`, `Reset()`, and event hooks (`ActionScheduled`, `ActionStarted`, `ActionProgressed`, `ActionCompleted`, `ActionCancelled`, `ActionFailed`, `TimeAdvanced`).
  - **Key Architectural Gap Observed**: `TurnResolver` currently manages internal dictionaries only for actions indexed by `Guid ActorId` (`Dictionary<Guid, TacticalAction> _activeActions`, `Dictionary<Guid, Queue<TacticalAction>> _actorQueues`).
  - **There is currently NO mechanism to register `IEntity` / `IActorPhysiology` instances with `ITurnResolver` / `TurnResolver`, and `TurnResolver.Tick(float dt)` does NOT invoke `IActorPhysiology.TickPhysiology(dt)`.**

### 1.6 Dependency Injection Registration
- **`ServiceCollectionExtensions`** (`TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`, lines 13-60):
  - `AddTacticalSimCore(this IServiceCollection services)`: Chained registration of materials, simulation, and singleton ballistics models (`IDragModel`, `IEnvironmentModel`).
  - `AddMaterialPenetration(this IServiceCollection services)`: `IMaterialRegistry` (Singleton), `IMaterialPenetrationSystem` (Transient).
  - `AddSimulationServices(this IServiceCollection services)`: `ITurnResolver` (Transient `TurnResolver`).
- **DI Tests** (`TacticalSim.Tests/DependencyInjectionTests.cs`): Verifies service resolutions, singleton/transient lifetimes, modular chaining, and container isolation.

### 1.7 Current Test Suite Status
- **Test execution command**: `dotnet test`
- **Result**: `Passed! - Failed: 0, Passed: 232, Skipped: 0, Total: 232, Duration: 212 ms`
- Zero compiler warnings, zero compiler errors.

---

## 2. Logic Chain

1. **Premise 1 (Follow-up Requirement R2)**:
   The updated prompt and `ORIGINAL_REQUEST.md` (lines 53-55) require that the turn resolver advance `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation as the timeline advances.
2. **Premise 2 (Physiology Subsystem Capability)**:
   `TacticalActorPhysiology.TickPhysiology(float dt)` is fully implemented in `TacticalSim.Core/ActorPhysiology.cs` and computes blood volume loss from active hemorrhages, tourniquet ischemia necrosis durations, and cardiovascular state changes over time delta `dt`.
3. **Premise 3 (Current Resolver Limitation)**:
   Inspection of `TacticalSim.Core/Simulation/TurnResolver.cs` reveals that `TurnResolver` only maintains `_activeActions` and `_actorQueues` keyed by `Guid ActorId`. It does not hold references to `IEntity` or `IActorPhysiology`, nor does `TurnResolver.Tick(float dt)` iterate through any registered entities or physiologies.
4. **Premise 4 (Time Scale Consistency)**:
   In `agents.md` (lines 27-30), simulation time is standard seconds ($s$). In `ActorPhysiology.cs`, hemorrhage rates are in ml/s and ischemia duration is in seconds. In `TurnResolver.cs`, `dt` represents elapsed fractionated TUs (1 TU = 1 s). Therefore, passing `dt` from `TurnResolver.Tick(dt)` directly into `IActorPhysiology.TickPhysiology(dt)` maintains 100% physical and mathematical consistency without unit conversion factors.
5. **Premise 5 (Decoupled Architectural Extension)**:
   In accordance with `agents.md` guidelines, extending `ITurnResolver` / `TurnResolver` to support entity/physiology tracking (e.g. `RegisterEntity(IEntity entity)`, `UnregisterEntity(Guid entityId)`, `IReadOnlyCollection<IEntity> RegisteredEntities`, and/or `RegisterPhysiology(Guid actorId, IActorPhysiology physiology)`) will enable the resolver to invoke `TickPhysiology(dt)` during each simulation tick step while keeping `TacticalSim.Core` decoupled and cleanly registered in `AddSimulationServices`.

---

## 3. Caveats

- **No Caveats on Codebase Exploration**: The entire codebase, interfaces, tests, DI extension methods, and client scaffolding were inspected and fully mapped.
- **Assumptions on Entity Registration API**: While `ORIGINAL_REQUEST.md` mandates that the resolver invoke `IActorPhysiology.TickPhysiology(dt)` on all active entities in the simulation, the exact method signatures for entity registration on `ITurnResolver` (e.g. `RegisterEntity(IEntity entity)` vs `RegisterActor(IEntity entity)` vs `RegisterPhysiology(Guid actorId, IActorPhysiology physiology)`) are open to design by the team during specification and implementation phases.

---

## 4. Conclusion

1. **Current System Maturity**:
   - The fractionated TU action execution state machine, sub-tick carryover, concurrent multi-actor scheduling, fault isolation, observability events, material penetration physics, and DI containers are fully operational with 232 passing xUnit tests.
   - `IActorPhysiology` and `TacticalActorPhysiology` are fully implemented with accurate hemorrhage and ischemia modeling.
2. **Key Actionable Integration Item**:
   - `ITurnResolver` and `TurnResolver` must be enhanced with entity/physiology registration capabilities and an iteration loop inside `TurnResolver.Tick(float dt)` that invokes `physiology.TickPhysiology(dt)` on all active registered entities/physiologies as global time advances.
   - Comprehensive unit and integration tests in `TacticalSim.Tests` should be added to verify entity registration, unregistration, time advancement, active bleeding blood volume depletion over turns, and tourniquet ischemia progression during turn resolver progression.

---

## 5. Verification Method

### 5.1 Independent Code Verification
- Inspect `TacticalSim.Core/Entities/IEntity.cs` to verify the `IActorPhysiology Physiology { get; }` contract.
- Inspect `TacticalSim.Core/ActorPhysiology.cs` (lines 103-159) to confirm `TickPhysiology(float dt)` mechanics.
- Inspect `TacticalSim.Core/Simulation/TurnResolver.cs` (lines 224-368) to verify that `Tick(float dt)` currently only steps active actions and does not yet invoke `TickPhysiology`.
- Inspect `TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs` to verify DI bindings.

### 5.2 Test Execution
Run the full test suite using PowerShell/dotnet CLI:
```pwsh
dotnet test --configuration Debug --verbosity normal
```
Verify that all 232 existing tests pass with 0 errors and 0 warnings.
