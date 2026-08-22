# Agent Instructions: TacticalSim

## Authoritative damage-model plan

The implementation roadmap for the damage model is [docs/TacticalSim_Damage_Model_Roadmap.md](docs/TacticalSim_Damage_Model_Roadmap.md). Its GitHub issue mapping is maintained in [docs/DAMAGE_MODEL_ISSUE_MAP.md](docs/DAMAGE_MODEL_ISSUE_MAP.md), and the work is tracked in the private GitHub project `1BalProj`.

The roadmap supersedes ad-hoc requests to make the model "more realistic." Work must be expressed as a bounded issue in the dependency chain M5–M12:

- M5: one authoritative, typed-unit, deterministic projectile-to-injury pipeline;
- M6: explicit anatomical structures and persistent lesions;
- M7: hemorrhage, circulation, and tactical capability;
- M8: thoracic injury mechanisms;
- M9: timed treatment actions and resources;
- M10: isometric tactical integration;
- M11: casualty variation and bounded seeded uncertainty;
- M12: validation, calibration, performance, persistence, and developer documentation.

The completed Phase 1–6 milestones remain historical foundation work. New damage-model work belongs to M5–M12 unless an issue explicitly documents why it is a foundation regression or migration task.

## Overview
This repository contains the foundational architecture for a high-fidelity, turn-based tactical simulator (`TacticalSim`). The environment is strictly deterministic and written in C# (.NET 10.0). It focuses on external ballistics, localized physiological trauma, and a decoupled architecture between the computational logic and the presentation layer.

## Architecture Guidelines
- **Strict Decoupling**: Keep the mathematical simulation (`TacticalSim.Core`) entirely independent of the UI/rendering engine.
- **Dependency Injection**: Utilize `Microsoft.Extensions.DependencyInjection` to link solvers to the tactical grid's turn resolution system.
- **Math & Vectors**: Use `System.Numerics.Vector3` for $R^3$ vector math to ensure optimal performance.
- **Testing**: Maintain test coverage in `TacticalSim.Tests` using xUnit.
- **Authoritative damage model**: Projectile interaction, wound generation, lesions, physiology, treatment effects, and capability state belong in `TacticalSim.Core`. Godot consumes core results and must not maintain a competing injury or medical pipeline.
- **Layered consequences**: Keep injury, physiology, and gameplay capability as separate contracts. Do not map a destroyed voxel directly to a final tactical penalty.
- **Deterministic replay**: Any stochastic behavior must use a seeded, recorded random stream. Model version, scenario, actor profiles, seed, and ordered actions must be sufficient to reproduce a simulation.
- **Typed quantities and conservation**: Use explicit unit wrappers or documented conversion boundaries for energy, distance, mass, time, volume, pressure, flow, and rates. Add conservation checks for projectile energy, blood destinations, consumables, and action time.
- **Feature-flag migration**: New damage behavior must be introduced behind a model version or feature flag until comparison and migration acceptance criteria are complete. Do not remove legacy behavior early.
- **No unexplained constants**: Mark each non-trivial parameter as sourced, calibrated, inferred, provisional, or gameplay tuning.

## GitHub Project & Tracking
Agents working in this repository MUST track their progress and align tasks with the GitHub Project "1BalProj".

- **Milestones**: Preserve the completed foundation milestones (Phase 1–6). Assign new damage-model issues to the roadmap milestones M5–M12 in `docs/DAMAGE_MODEL_ISSUE_MAP.md`.
- **Labels**: Apply the roadmap taxonomy: one or more `type:*` labels, one `priority:P0`–`priority:P3` label, and workflow/risk labels where applicable (`model-change`, `needs-tests`, `needs-design`, `needs-data`, `human-review-required`, `debug-tooling`, `legacy-removal`, or `save-compatibility`). Existing `area:*` labels remain valid for historical issues.
- **Task Tracking**: 
  - Before starting any work, you MUST check if the proposed work fits under an existing issue on the repository.
  - If the work does NOT fit under an existing issue, you MUST use the GitHub CLI to create a new issue detailing the task, and add it to the `1BalProj` project.
  - Before making code or documentation changes, you MUST move the appropriate issue to the "In progress" status on the `1BalProj` project using the GitHub CLI (for example, `gh project item-edit --project-id ...`).
  - Keep issue status, milestone, priority, size, labels, dependencies, and acceptance criteria synchronized with the implementation.
- **Project Updates**: When implementing new features, always verify if issues need to be transitioned across columns in the 1BalProj project when completed.

## Damage-model implementation workflow

1. Read the roadmap section, issue, dependencies, current implementation, and relevant tests.
2. State the bounded implementation plan in the task log.
3. Make the smallest architecture-consistent change in the authoritative core layer.
4. Add deterministic tests for normal, boundary, failure/interruption, replay, and regression behavior where relevant.
5. Run targeted tests, then the full build and test suite.
6. Update debug telemetry, developer documentation, and parameter provenance when hidden state or model behavior changes.
7. Report the issue, changed files, behavior before/after, tests, build result, performance effect, parameters, limitations, and follow-up issues.

Do not begin thoracic detail, timed treatment, broad AI integration, or casualty variation before the single injury pipeline and lesion foundations are in place. The first implementation tranche is DM-001, DM-005, DM-002, DM-003, DM-006, DM-004, DM-101, DM-102, DM-103, and DM-201.

## Simulation and presentation boundary

The Godot client may render projectile visuals, casualty overlays, and omniscient developer views, but the authoritative damage event must come from the core interaction service. Gameplay presentation may simplify or obscure information later; debug telemetry may remain ground-truth and omniscient during development.

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
