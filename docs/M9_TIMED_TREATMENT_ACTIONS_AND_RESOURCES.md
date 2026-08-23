# M9 — Timed treatment actions and resources

M9 routes medical interventions through `TurnResolver` as `TreatmentAction` instances. A treatment identifies its provider, casualty and lesion/region, duration, equipment, hands, posture, quality, interruption policy, result, and reassessment requirement. Physiology remains independently advanced by the simulation while treatment occupies the provider.

## Behavior and provenance

- Actor or team `TreatmentInventory` loadouts are finite and reserve equipment only when an action begins. Completion consumes reserved items; cancellation or failure releases them. `IgnoreInventory` is an explicit scenario/debug option.
- Tourniquets require a lesion and limb placement zone. Ineffective quality does not control bleeding, partial quality reports a partial result, and a configured second device upgrades partial control. The current M9 abstraction applies the existing M7 tourniquet flow multiplier; explicit limb perfusion continues to use the legacy actor-physiology ischemia model.
- Packing and pressure are restricted to accessible, compressible external/local-soft-tissue sources. Packing is region-agnostic. Internal, pleural, pericardial, and abdominal bleeding cannot be controlled by these actions.
- Cancellation records interruption reason and removes sustained direct pressure. Completed actions create a deterministic reassessment due time. Reassessment observations and all applied treatments are traceable.
- Developer quick treatment requires an explicit runtime enable flag and records a debug-marked trace entry. It is an orchestration API, not a second physiology pipeline.

Treatment durations (8 seconds for a tourniquet, 10 seconds for packing/pressure, and 30 seconds to reassessment) and quality thresholds are **provisional gameplay tuning**, not clinical guidance. The model is intended for deterministic tactical simulation and is not a clinical predictor or first-aid training tool.

## Known limitations

Treatment TU and seconds are currently equivalent at the existing resolver boundary. Equipment attachment/removal is represented by consumption and model state rather than a separate physical item entity. Suppression, movement, and provider incapacitation are exposed as deterministic interruption reasons; M10 systems will invoke them from tactical state.
