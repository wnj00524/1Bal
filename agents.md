# Agent Instructions: TacticalSim

## Overview
This repository contains the foundational architecture for a high-fidelity, turn-based tactical simulator (`TacticalSim`). The environment is strictly deterministic and written in C# (.NET 10.0). It focuses on external ballistics, localized physiological trauma, and a decoupled architecture between the computational logic and the presentation layer.

## Architecture Guidelines
- **Strict Decoupling**: Keep the mathematical simulation (`TacticalSim.Core`) entirely independent of the UI/rendering engine.
- **Dependency Injection**: Utilize `Microsoft.Extensions.DependencyInjection` to link solvers to the tactical grid's turn resolution system.
- **Math & Vectors**: Use `System.Numerics.Vector3` for $R^3$ vector math to ensure optimal performance.
- **Testing**: Maintain test coverage in `TacticalSim.Tests` using xUnit.

## GitHub Project & Tracking
Agents working in this repository MUST track their progress and align tasks with the GitHub Project "1BalProj".

- **Milestones**: Ensure PRs and issues are assigned to the correct milestone:
  - Phase 1: Core Ballistics
  - Phase 2: Physiological State Machine
  - Phase 3: Turn Resolution System
- **Labels**: Apply relevant labels (`area: ballistics`, `area: physiology`, `area: simulation`, `type: architecture`) to issues and PRs.
- **Task Tracking**: 
  - Before starting any work, you MUST check if the proposed work fits under an existing issue on the repository.
  - If the work does NOT fit under an existing issue, you MUST use the GitHub CLI to create a new issue detailing the task, and add it to the `1BalProj` project.
  - Before making any code changes, you MUST move the appropriate issue to the "In Progress" status on the `1BalProj` project using the GitHub CLI (e.g., `gh project item-edit`).
- **Project Updates**: When implementing new features, always verify if issues need to be transitioned across columns in the 1BalProj project when completed.

## Standard Units & Physics
To ensure mathematical consistency across the simulation, all calculations MUST adhere to the following units unless explicitly stated otherwise:
- **Distance/Length**: Meters (m). Example: 1cm = `0.01f`.
- **Mass**: Kilograms (kg).
- **Time**: Seconds (s) internally.
- **Volume**: Cubic meters ($m^3$) internally. Medical UI reports may convert to cubic centimeters (cc).
- **Density**: kg/$m^3$.
- **Energy**: Joules (J).
- **Pressure/Shear Strength**: Megapascals (MPa).
- **Hemorrhage/Bleed Rates**: Milliliters per second (ml/sec) internally, reported in ml/min.
