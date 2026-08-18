## 2026-08-17T21:37:26Z
<USER_REQUEST>
You are the Worker for Milestone 3: Dependency Injection & Zero-Warning Hygiene.

Working directory: c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m3_1
Project root: c:\Users\jdwil\source\repos\Codex\1bal

## Mandatory Integrity Warning:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Exclusive Write Ownership:
You own and may modify ONLY these 3 files:
1. TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs
2. TacticalSim.Core/Physiology/ActorPhysiology.cs
3. TacticalSim.Tests/DependencyInjectionTests.cs

Do NOT modify any other files in the project.

## Reference Files to Read First:
- c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md
- c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md
- c:\Users\jdwil\source\repos\Codex\1bal\.agents\sub_orch_m3\SCOPE.md

## Objectives & Detailed Tasks:
1. Fix CS8618 Warning:
   - In `TacticalSim.Core/Physiology/ActorPhysiology.cs` around line 24, fix the CS8618 warning on `Parent` property by making it nullable: `public BodyPart? Parent { get; set; }`.
   - Verify that building the project emits 0 compiler warnings.

2. Implement DI Extension Methods in `TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`:
   - Namespace: `TacticalSim.Core.DependencyInjection` (and/or `Microsoft.Extensions.DependencyInjection` per idiomatic .NET conventions, using `Microsoft.Extensions.DependencyInjection`).
   - Implement extension methods:
     - `public static IServiceCollection AddTacticalSimCore(this IServiceCollection services)`
       Registers:
       - `IMaterialRegistry` -> `MaterialRegistry` as Singleton
       - `IMaterialPenetrationSystem` -> `MaterialPenetrationSystem` as Transient
       - `ITurnResolver` -> `TurnResolver` as Transient
       - `IDragModel` -> `StandardDragCurve` as Singleton (default constructor with default cd = 0.3f or instance)
       - `IEnvironmentModel` -> `ICAOStandardAtmosphere` as Singleton (default origin Vector3.Zero, gravity Vector3(0, -9.80665f, 0) or default constructor/factory)
     - `public static IServiceCollection AddMaterialPenetration(this IServiceCollection services)`
       Registers:
       - `IMaterialRegistry` -> `MaterialRegistry` (Singleton)
       - `IMaterialPenetrationSystem` -> `MaterialPenetrationSystem` (Transient)
     - `public static IServiceCollection AddSimulationServices(this IServiceCollection services)`
       Registers:
       - `ITurnResolver` -> `TurnResolver` (Transient)

3. Implement DI Unit Tests in `TacticalSim.Tests/DependencyInjectionTests.cs`:
   - Test `AddTacticalSimCore()` registers and successfully resolves all 5 interfaces (`IMaterialRegistry`, `IMaterialPenetrationSystem`, `ITurnResolver`, `IDragModel`, `IEnvironmentModel`).
   - Test lifetime semantics (Singleton vs Transient).
   - Test modular registration (`AddMaterialPenetration()`, `AddSimulationServices()`).
   - Test end-to-end usage of resolved services (e.g. scheduling action on resolved `ITurnResolver`, calculating penetration with resolved `IMaterialPenetrationSystem` using resolved `IMaterialRegistry`, evaluating drag and atmosphere).

4. Verification:
   - Run `dotnet build` to ensure 0 errors and 0 warnings.
   - Run `dotnet test` to ensure all tests across the entire solution pass.
   - Document build command outputs and test execution outputs in your handoff report.

5. Output:
   - Write a complete `handoff.md` and `progress.md` in `c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m3_1\`.
   - Send a message to parent orchestrator with your completion status and key results.

</USER_REQUEST>
