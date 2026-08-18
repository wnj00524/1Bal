# Original User Request

## Initial Request — 2026-08-17T21:21:07Z

# Teamwork Project Prompt — Draft

> Status: Ready for launch — awaiting user approval
> Goal: Craft prompt → get user approval → delegate to teamwork_preview
> Requested team: Full team

Implement the architectural systems for Issue #3 (Fractionated TU Turn Resolver) and Issue #4 (Material Penetration System) within the existing C# `TacticalSim.Core` project, ensuring strict adherence to the project's decoupled design.

Working directory: c:\Users\jdwil\source\repos\Codex\1bal
Integrity mode: development

## Requirements

### R1. Implement Fractionated TU Turn Resolver (Issue #3)
Create a simultaneous turn resolution system that manages a global timeline. It must be capable of scheduling concurrent actions from multiple entities and advancing their execution state based on fractionated Time Unit (TU) increments.

### R2. Implement Material Penetration System (Issue #4)
Develop a terminal ballistics system to handle environmental cover materials (e.g., Wood, Concrete, Steel). It must calculate a projectile's velocity loss and kinetic energy transfer when intersecting a material, based on the material's density and the penetration thickness.

### R3. Architectural Decoupling
All implementations must remain strictly isolated within `TacticalSim.Core` and rely on `Microsoft.Extensions.DependencyInjection` for service registration, conforming to the guidelines established in `agents.md`.

## Acceptance Criteria

### Functional Verification (xUnit)
- [ ] Programmatic xUnit tests in `TacticalSim.Tests` successfully verify that multiple concurrent actions are interleaved and resolved correctly by the Turn Resolver across fractionated time steps.
- [ ] Programmatic xUnit tests successfully verify that a projectile loses velocity proportionally to a target material's density and thickness, and exits the material with the mathematically correct reduced kinetic energy.
- [ ] The full solution (`dotnet build`) compiles without errors or warnings.
- [ ] All tests (`dotnet test`) pass successfully.
