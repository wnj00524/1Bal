# Damage Model GitHub Issue Map

This document maps the implementation identifiers in [TacticalSim_Damage_Model_Roadmap.md](TacticalSim_Damage_Model_Roadmap.md) to the canonical GitHub issues in `wnj00524/1Bal` and the `1BalProj` project. The older Phase 1–6 milestones remain historical foundation work; M5–M12 are the active damage-model plan.

All roadmap issues use the roadmap’s type, priority, size, dependency, and acceptance-criteria fields. Project cards should remain in dependency order. The first implementation tranche is DM-001, DM-005, DM-002, DM-003, DM-006, DM-004, DM-101, DM-102, DM-103, and DM-201.

| ID | GitHub issue | Milestone | GitHub issue state |
|---|---|---|---|
| DM-001 | [Adopt a typed-unit strategy](https://github.com/wnj00524/1Bal/issues/108) | M5: Damage-model foundations | Closed |
| DM-002 | [Add a projectile energy ledger](https://github.com/wnj00524/1Bal/issues/109) | M5: Damage-model foundations | Closed |
| DM-003 | [Introduce canonical wound-track contracts](https://github.com/wnj00524/1Bal/issues/110) | M5: Damage-model foundations | Closed |
| DM-004 | [Create a single core projectile-interaction service](https://github.com/wnj00524/1Bal/issues/111) | M5: Damage-model foundations | Closed |
| DM-005 | [Add deterministic random-source contracts](https://github.com/wnj00524/1Bal/issues/112) | M5: Damage-model foundations | Closed |
| DM-006 | [Build a reference impact scenario harness](https://github.com/wnj00524/1Bal/issues/113) | M5: Damage-model foundations | Closed |
| DM-101 | [Define anatomical structure contracts](https://github.com/wnj00524/1Bal/issues/120) | M6: Anatomical structures and persistent lesions | Closed |
| DM-102 | [Define persistent lesion hierarchy](https://github.com/wnj00524/1Bal/issues/121) | M6: Anatomical structures and persistent lesions | Closed |
| DM-103 | [Add a first-pass major-vessel map](https://github.com/wnj00524/1Bal/issues/122) | M6: Anatomical structures and persistent lesions | Closed |
| DM-104 | [Add clinically meaningful bone segments and fracture lesions](https://github.com/wnj00524/1Bal/issues/123) | M6: Anatomical structures and persistent lesions | Closed |
| DM-105 | [Add spinal cord and major peripheral nerve structures](https://github.com/wnj00524/1Bal/issues/124) | M6: Anatomical structures and persistent lesions | Open |
| DM-106 | [Convert voxels into an implementation detail of spatial lookup](https://github.com/wnj00524/1Bal/issues/125) | M6: Anatomical structures and persistent lesions | Open |
| DM-107 | [Add lesion serialization and debug inspection](https://github.com/wnj00524/1Bal/issues/126) | M6: Anatomical structures and persistent lesions | Open |
| DM-201 | [Implement bleeding sources from vessel and tissue lesions](https://github.com/wnj00524/1Bal/issues/127) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-202 | [Add blood destinations and compartment conservation](https://github.com/wnj00524/1Bal/issues/128) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-203 | [Add simplified hemostasis, compression, and rebleeding](https://github.com/wnj00524/1Bal/issues/129) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-204 | [Replace hemorrhage classes with a reduced cardiovascular model](https://github.com/wnj00524/1Bal/issues/130) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-205 | [Separate oxygen saturation from oxygen delivery](https://github.com/wnj00524/1Bal/issues/131) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-206 | [Define explicit incapacitation, unconsciousness, and death states](https://github.com/wnj00524/1Bal/issues/132) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-207 | [Add physiology-to-capability resolver](https://github.com/wnj00524/1Bal/issues/133) | M7: Hemorrhage, circulation, and tactical capability | Closed |
| DM-301 | [Add bilateral pleural compartments](https://github.com/wnj00524/1Bal/issues/134) | M8: Thoracic injury model | Closed |
| DM-302 | [Implement simple, open, and tension pneumothorax](https://github.com/wnj00524/1Bal/issues/135) | M8: Thoracic injury model | Closed |
| DM-303 | [Implement hemothorax and pericardial tamponade](https://github.com/wnj00524/1Bal/issues/136) | M8: Thoracic injury model | Closed |
| DM-304 | [Implement thoracic treatment interactions](https://github.com/wnj00524/1Bal/issues/137) | M8: Thoracic injury model | Closed |
| DM-305 | [Add thoracic reference scenarios](https://github.com/wnj00524/1Bal/issues/138) | M8: Thoracic injury model | Closed |
| DM-401 | [Define treatment-action contracts](https://github.com/wnj00524/1Bal/issues/114) | M9: Timed interventions and resources | Closed |
| DM-402 | [Implement tourniquet application quality](https://github.com/wnj00524/1Bal/issues/115) | M9: Timed interventions and resources | Closed |
| DM-403 | [Implement direct pressure and wound packing](https://github.com/wnj00524/1Bal/issues/116) | M9: Timed interventions and resources | Closed |
| DM-404 | [Implement treatment equipment inventory](https://github.com/wnj00524/1Bal/issues/117) | M9: Timed interventions and resources | Closed |
| DM-405 | [Add treatment interruption and reassessment](https://github.com/wnj00524/1Bal/issues/118) | M9: Timed interventions and resources | Closed |
| DM-406 | [Add a developer quick-treatment console](https://github.com/wnj00524/1Bal/issues/119) | M9: Timed interventions and resources | Closed |
| DM-501 | [Connect capability state to tactical-action costs](https://github.com/wnj00524/1Bal/issues/140) | M10: Isometric tactical integration | Closed |
| DM-502 | [Add casualty drag and carry actions](https://github.com/wnj00524/1Bal/issues/141) | M10: Isometric tactical integration | Closed |
| DM-503 | [Add casualty behavior states for AI](https://github.com/wnj00524/1Bal/issues/142) | M10: Isometric tactical integration | Closed |
| DM-504 | [Add tactical rescue exposure and opportunity cost](https://github.com/wnj00524/1Bal/issues/143) | M10: Isometric tactical integration | Closed |
| DM-505 | [Create isometric casualty-status overlays](https://github.com/wnj00524/1Bal/issues/144) | M10: Isometric tactical integration | Closed |
| DM-506 | [Add casualty-aware scenario objectives and scoring](https://github.com/wnj00524/1Bal/issues/145) | M10: Isometric tactical integration | Closed |
| DM-507 | [Surface authoritative lesion damage in the Godot medical report](https://github.com/wnj00524/1Bal/issues/203) | M10: Isometric tactical integration | Open |
| DM-601 | [Add configurable casualty profiles](https://github.com/wnj00524/1Bal/issues/146) | M11: Casualty variation and bounded uncertainty | Closed |
| DM-602 | [Add bounded, seeded physiological uncertainty](https://github.com/wnj00524/1Bal/issues/147) | M11: Casualty variation and bounded uncertainty | Closed |
| DM-603 | [Add terminal projectile behavior profiles](https://github.com/wnj00524/1Bal/issues/154) | M11: Casualty variation and bounded uncertainty | Closed |
| DM-604 | [Add armor and clothing interaction hooks](https://github.com/wnj00524/1Bal/issues/155) | M11: Casualty variation and bounded uncertainty | Closed |
| DM-605 | [Build a batch cohort runner](https://github.com/wnj00524/1Bal/issues/156) | M11: Casualty variation and bounded uncertainty | Closed |
| DM-701 | [Create a parameter provenance registry](https://github.com/wnj00524/1Bal/issues/157) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-702 | [Create a reference injury suite](https://github.com/wnj00524/1Bal/issues/158) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-703 | [Add calibration and sensitivity tooling](https://github.com/wnj00524/1Bal/issues/159) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-704 | [Add damage-model performance benchmarks](https://github.com/wnj00524/1Bal/issues/160) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-705 | [Add model-versioned save and replay support](https://github.com/wnj00524/1Bal/issues/161) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-706 | [Publish damage-model developer documentation](https://github.com/wnj00524/1Bal/issues/162) | M12: Validation, balancing, performance, and release hardening | Closed |
| DM-801 | [Make projectile lesion application idempotent and simulation-timestamped](https://github.com/wnj00524/1Bal/issues/187) | M6: Anatomical structures and persistent lesions | Open |
| DM-802 | [Integrate lesion-driven neurological and casualty state in Godot](https://github.com/wnj00524/1Bal/issues/205) | M7: Hemorrhage, circulation, and tactical capability | Closed |

## Completion convention

GitHub issue state records workflow only; it does **not** establish integrated roadmap acceptance. A component is *locally complete* when its contract, mechanism, and direct tests exist. It is *integrated accepted* only after composition, tactical consumption, observability, persistence/replay, integrated validation, and release evidence applicable to that item are green. Closed issues that have not met those criteria remain remediation work and must not be reported as roadmap-complete. This table was reconciled against GitHub on 2026-08-23; DM-105, DM-106, and DM-107 remain open.

## Board conventions

- `Backlog` means the dependency chain has not been started.
- `Ready` means the issue is bounded and can be assigned without another design decision.
- `In progress` means the issue is the active repository change.
- `In review`, `Done`, and `Blocked` must be kept synchronized with the issue and pull request.
- Keep the project’s Priority and Size fields aligned with the values in each issue body.
