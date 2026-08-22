# ADR-001: Typed units for the damage model

- Status: Accepted for DM-001
- Date: 2026-08-22
- Scope: `TacticalSim.Core` model quantities used by projectile, material, injury, physiology, and treatment systems

## Decision

Use small, maintained-in-repository readonly value types rather than adding a third-party units library. The initial types are `Distance`, `Area`, `Volume`, `Mass`, `Time`, `Energy`, `Pressure`, and `FlowRate` in `TacticalSim.Core.Units`.

Each type stores one canonical simulation unit and exposes named accessors:

| Quantity | Canonical unit | Display/serialization examples |
| --- | --- | --- |
| Distance | m | m |
| Area | m² | m² |
| Volume | m³ | cm³/cc |
| Mass | kg | g |
| Density | kg/m³ | kg/m³ |
| Time | s | ms |
| Energy | J | J |
| Pressure | Pa | MPa |
| FlowRate | m³/s | ml/s or ml/min |

Conversions are named factory methods or boundary helpers. In particular, MPa input must use `Pressure.FromMegapascals`; a raw numeric pressure is never implicitly treated as either Pa or MPa. The core does not use implicit numeric conversions, and unlike quantities have no arithmetic operators between them.

## Migration policy

Existing public scalar members remain temporarily available for save/UI compatibility. Typed accessors sit beside those members and are used by the highest-risk projectile and material calculations first. New core contracts should expose typed quantities directly. Legacy scalar values are converted only at explicit serialization, UI, or compatibility boundaries.

This bounded migration avoids changing the established Godot and test construction APIs while making unit intent visible to new code. Later milestones can replace legacy members once save compatibility and model-version gates are in place.

## Rejected alternatives

- A third-party units package would add a dependency and a serialization policy before the model contracts have stabilized.
- Bare documented floats do not prevent mixing mass, energy, time, or pressure, and cannot enforce the MPa-to-Pa conversion.
