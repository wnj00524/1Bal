# Damage model and Godot client authority audit

Issue: [#207](https://github.com/wnj00524/1Bal/issues/207)  
Model version reviewed: `IntegratedV3`

## Finding

The Godot client does not contain a second tissue or physiological damage model. Its projectile flight loop integrates the external trajectory and detects scene walls, but a body interaction is sent once to `IProjectileInteractionService`. That core service owns voxel traversal, energy transfer, wound-track construction, lesion generation, and application to the actor's persistent `ActorMedicalState`. The client retains the returned wound track and energy ledger only for visualization and telemetry.

The reported 98% (.380 ACP) and 95% (9x19 mm) brain-function outcomes were produced by the core model, not invented by Godot. `LesionGenerator` previously divided the few joules transferred in one brain segment by a generic structure denominator, yielding minimum severities near 1–2%. `NeurologicalFunctionalResolver` then correctly projected those core severities through its cognitive-loss multiplier. The presentation faithfully exposed the result, but the lesion calibration was not plausible for a projectile track physically penetrating the brain.

## Corrected authoritative flow

```text
Godot scenario input and external flight
  -> IProjectileInteractionService (Core)
  -> WoundTrack + EnergyLedger (Core)
  -> LesionGenerator (Core)
  -> ActorMedicalState.ApplyImpact (Core)
  -> hemorrhage, neurological state, casualty state, capability (Core)
  -> ActorMedicalSnapshot / MedicalAssessor report (Core)
  -> Godot text, tint, and wound-track visualization only
```

For the reduced anatomy in `IntegratedV3`, a generated wound track that intersects `organ.brain` now receives a provisional minimum lesion severity of 0.30. That is the existing immediate-unconsciousness boundary. It prevents a penetrating brain wound from being represented as near-normal cognitive function while preserving lower-severity neurological lesions supplied by other future mechanisms. This is deterministic gameplay calibration, not a clinical mortality prediction, and is registered in the parameter-provenance inventory.

## Time progression and client refresh

`SimulationManager.AdvanceScenario` ticks the same persistent core physiology instance for five seconds. `UIManager` then calls `MedicalAssessor.AssessTrauma` again; it does not extrapolate future damage. The report now includes the authoritative model time and cumulative blood loss so a small early change is visible instead of being hidden by whole-percent rounding. Brain-lesion bleeding continues through the core hemorrhage source and can drive later systemic deterioration. Direct neurological structural severity remains persistent rather than changing merely because client time passed.

This distinction is intentional: the client must show deterioration that the model actually produces, but must not manufacture deterioration when the core snapshot is unchanged.

## Remaining limitations

- The anatomy has one undifferentiated brain structure, with no trajectory-specific eloquent regions, intracranial pressure, edema, hematoma expansion, or brain herniation.
- The 0.30 minimum is explicitly provisional and should be replaced by regional brain structures and validated lesion geometry when those mechanisms are implemented.
- External flight and scene-wall contact remain Godot scenario responsibilities. Once body contact occurs, all injury consequences remain core-owned.
- Ordinary gameplay may later obscure ground truth, but this developer medical report is currently an omniscient view of the core snapshot.
