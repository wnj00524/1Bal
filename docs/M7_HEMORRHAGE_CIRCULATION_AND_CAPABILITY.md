# M7: Hemorrhage, circulation, and tactical capability

M7 introduces a deterministic, reduced-order pipeline from persistent M6 lesions to tactical capability:

```text
lesion -> bleeding source -> destination ledger -> cardiovascular state
       -> oxygen delivery -> casualty state -> tactical capability
```

`HemorrhagePhysiologyModel` is the authoritative M7 progression contract. Legacy voxel-derived bleeding remains available only for migration comparisons in `TacticalActorPhysiology`; new damage-model callers should create sources through `BleedingSourceFactory` and advance this model.

## Contracts and invariants

- Each lesion owns at most one independently controllable `BleedingSource`.
- Arterial, venous, pulmonary, and parenchymal sources use distinct pressure regimes. Flow falls with source pressure.
- Every millilitre removed from circulation is credited to exactly one of eight destination compartments. `ConservationErrorMilliliters` exposes reconciliation drift.
- Compression, packing, and tourniquets are rejected for non-compressible sources. Definitive control is explicit and source-local.
- Small sources can form a stable clot; high-flow or major-aperture sources cannot spontaneously seal. Movement stress deterministically disrupts stable clots.
- The cardiovascular model separately exposes volume, heart rate, stroke-volume proxy, cardiac-output proxy, vascular-resistance proxy, MAP, and perfusion.
- Arterial saturation is distinct from red-cell mass and systemic/cerebral oxygen delivery. Normal saturation therefore does not imply adequate delivery.
- `Dead` is latched. `Incapacitated` and `Unconscious` are separate states and can otherwise improve if their inputs improve.
- Capability output covers movement, posture, aiming, firing, reloading, communication, and self-aid without inspecting voxels.

The integrator uses fixed 0.1-second internal steps so caller timestep size does not materially change a trajectory. Source ordering is canonical by lesion ID, and the model contains no unrecorded randomness.

## Parameter provenance

| Parameter or rule | Value | Classification | Purpose |
|---|---:|---|---|
| Internal integration step | 0.1 s | inferred numerical bound | stable, timestep-independent integration |
| Normal MAP | 93 mmHg | sourced physiological reference approximation | pressure normalization |
| Venous source pressure | 12 mmHg | sourced physiological approximation | distinguish venous flow |
| Pulmonary pressure multiplier | 0.25 systemic MAP | inferred reduction | first-pass pulmonary regime |
| Flow coefficients | 0.006–0.018 | provisional calibration | tactically distinct lesion trajectories; not clinically validated |
| Minor-source clot time | 300 s untreated | provisional gameplay tuning | bounded spontaneous hemostasis |
| Major aperture threshold | 5 mm | provisional calibration | prevents unrealistic major-vessel self-resolution |
| Incapacity delivery threshold | 0.55 | provisional gameplay mapping | degraded action availability before collapse |
| Unconscious cerebral threshold | 0.25 | provisional gameplay mapping | reversible loss of consciousness |
| Death threshold | cerebral delivery below 0.18 for 30 s | provisional gameplay mapping | stable terminal transition |

## Limitations

This model is mechanistically plausible game simulation, not a clinical predictor. Compartment-specific pressure effects belong to M8. Timed treatment quality and resources belong to M9. Human calibration and reference-case validation remain M12 work.
