# Scope: Milestone 3 — Dependency Injection & Zero-Warning Hygiene

## Objective
Implement Requirement R3 (Dependency Injection registration) via `Microsoft.Extensions.DependencyInjection`, fix existing compiler warning CS8618 in `TacticalSim.Core/Physiology/ActorPhysiology.cs`, and create comprehensive DI unit tests in `TacticalSim.Tests/DependencyInjectionTests.cs`.

## Exclusive Write Ownership
The worker(s) in this milestone own and may modify ONLY:
- `TacticalSim.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `TacticalSim.Core/Physiology/ActorPhysiology.cs` (fix line 24 CS8618 warning by making `Parent` nullable: `public BodyPart? Parent { get; set; }`)
- `TacticalSim.Tests/DependencyInjectionTests.cs`

## Key Requirements
1. **Zero Compiler Warnings**: Fix warning CS8618 in `ActorPhysiology.cs` so `dotnet build` succeeds with 0 errors and 0 warnings.
2. **DI Registration Extension Methods**:
   - `public static IServiceCollection AddTacticalSimCore(this IServiceCollection services)`
     - Registers:
       - `IMaterialRegistry` -> `MaterialRegistry` (Singleton)
       - `IMaterialPenetrationSystem` -> `MaterialPenetrationSystem` (Transient)
       - `ITurnResolver` -> `TurnResolver` (Transient)
       - `IDragModel` -> `StandardDragCurve` (Singleton, default cd = 0.3f)
       - `IEnvironmentModel` -> `ICAOStandardAtmosphere` (Singleton, default origin Vector3.Zero, gravity Vector3(0, -9.80665f, 0))
   - `public static IServiceCollection AddMaterialPenetration(this IServiceCollection services)`
     - Registers `IMaterialRegistry` and `IMaterialPenetrationSystem`.
   - `public static IServiceCollection AddSimulationServices(this IServiceCollection services)`
     - Registers `ITurnResolver`.
3. **DI Unit Tests (`TacticalSim.Tests/DependencyInjectionTests.cs`)**:
   - Verify `AddTacticalSimCore()` registers and resolves all required services from an `IServiceProvider`.
   - Verify services can be instantiated and used end-to-end through DI resolution.
   - Verify modular registration methods `AddMaterialPenetration()` and `AddSimulationServices()`.
   - Verify zero compiler warnings across the entire solution.

## References
- `c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\.agents\explorer_survey_1\handoff.md`
