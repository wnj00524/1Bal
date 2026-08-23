# M8 Thoracic Injury Model

M8 adds an authoritative, deterministic `ThoracicInjuryModel` beside the M7 hemorrhage model. Each casualty owns one model and its associated `HemorrhagePhysiologyModel`. The left and right `PleuralCompartment` instances independently expose pleural gas, conserved pleural blood, pressure, lung compression, seal state, needle state, and tension state.

## Mechanisms and integration

Persistent pleural or pulmonary lesions can be admitted through `AddPleuralLesion`; laterality comes from the named anatomical structure rather than impact coordinates. Explicit `ThoracicLesion` inputs support reference scenarios and record lung leak, open-wound conductance, one-way-valve behavior, and pulmonary functional loss. A lung leak begins as a simple pneumothorax and becomes tension only after configurable pleural pressure crosses the tension threshold.

The thoracic tick uses fixed 0.1-second internal steps. Pleural and pericardial blood remain owned by the M7 `BloodCompartmentLedger`, preserving the circulation-to-destination conservation invariant. Gas and blood both compress the affected lung, while only gas creates pleural pressure. Tension adds a circulatory penalty; pericardial volume creates an independent tamponade modifier. The resulting ventilation and cardiac modifiers are assigned to `HemorrhagePhysiologyModel` before its tick, so oxygen delivery, circulation, casualty state, and the existing capability resolver receive thoracic consequences.

## Quick treatment surface

`ApplyChestSeal` targets one side with an explicit effective, vented, partial, blocked, or detached state. Open-wound exchange responds to that state but sealing does not remove accumulated gas. `NeedleDecompress` also targets one side and reports successful, partial, ineffective, or wrong-side placement. An indwelling needle vents at a configurable rate; a continuing leak can overwhelm it and recur. Decompression removes neither pleural blood nor pericardial blood.

These methods are the M8 developer quick-treatment surface, not timed tactical actions. Treatment duration, inventory consumption, interruption, and operator skill remain M9 responsibilities.

## Parameter provenance and limitations

All defaults in `ThoracicModelParameters` are **provisional gameplay calibration**, not clinical constants: pleural compliance, compression volume, tension threshold, wound conductance, needle flow, and tamponade volume thresholds. The model deliberately uses reduced mechanics and is intended for mechanistically plausible tactical consequences, not diagnosis, medical prediction, or treatment instruction. M12 remains responsible for human review and calibration.

The reference tests cover unilateral simple pneumothorax, open chest wound, tension pneumothorax, massive hemothorax, pulmonary injury without tension, tamponade, incorrect intervention, and successful/failed decompression, plus the explicit seal states.
