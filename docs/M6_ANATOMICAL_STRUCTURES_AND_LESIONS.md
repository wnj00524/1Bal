# M6 anatomical structures and persistent lesions

M6 replaces voxel destruction as the public injury ontology. Voxels remain an internal collision and tissue-energy index, while the authoritative injury state is exposed through versioned anatomical structures and a persistent lesion repository.

## Contracts and data flow

`IAnatomicalStructureCatalog` provides stable named objects in body-local metres and deterministic segment queries without a renderer. `StandardAnatomy` supplies the first-pass major arterial and venous map, clinically meaningful bone segments, spinal cord, major limb nerves, airway, pleura, and pericardium. Each definition records calibre, pressure regime, functional role, region, and laterality where relevant.

The authoritative flow is now:

```text
ordered voxel traversal -> canonical wound track -> structure query
                        -> typed lesions -> actor lesion repository
```

`LesionGenerator` derives geometry and bounded severity from each energy-depositing wound segment. Vessel injuries distinguish partial laceration from complete transection based on wound aperture versus vessel calibre. Bone lesions carry stability and weight-bearing state. Nerve lesions carry grade, laterality, and spinal level. Other typed lesions cover parenchyma, airway, pleura, cardiac boundaries, brain/spinal injury, and open soft tissue.

Lesion IDs are deterministic from impact ID, generation order, and structure ID. Creation timestamps use the replay-stable Unix epoch until the simulation timeline is threaded into impact commands. Repeated impacts append to the same actor repository; treatment replaces immutable lesion records and never rebuilds anatomy or scans voxels.

## Musculoskeletal function and capability bridge

DM-104 adds a bounded bridge from structural injury to the existing actor capability outputs:

```text
fracture lesion -> fracture functional consequence
                -> musculoskeletal functional state
                -> actor mobility, weapon handling, and CanStand
```

`FractureLesion.FunctionalConsequence` is derived from `Stability`; it is not a second independently mutable or serialized classification. The first-pass mapping is:

| Fracture stability | Functional consequence | Capacity factor |
|---|---|---:|
| `Stable` | `LimitedUse` | `0.75` |
| `Displaced` | `SevereRestriction` | `0.40` |
| `Unstable` | `StructuralFunctionLost` | `0.00` |

`MusculoskeletalFunctionalState` publishes `StandingCapacity`, `MovementCapacity`, `UpperLimbCapacity`, and `CanStand`; its healthy baseline is `1.0`, `1.0`, `1.0`, and `true`. The pure resolver starts from that baseline and reads the persistent fracture repository against the canonical anatomy catalog. A fracture whose structure has the weight-bearing role applies its capacity factor to both standing and movement capacity; the persisted `WeightBearing` flag is used only as a compatibility fallback when the structure ID is absent from an older or custom catalog. A lower-limb motor role affects movement without implying weight-bearing support. A fracture whose structure has the upper-limb motor role applies its factor to upper-limb capacity, which bounds weapon handling. Fractures without those roles remain persistent and inspectable but do not acquire an unrelated motor penalty in this bridge.

Multiple applicable fractures aggregate deterministically by taking the minimum capacity for each affected output. This is a worst-effect rule: lesion order cannot change the result, and repeated injuries do not average, add, or multiply their penalties. It is an intentional first-pass limitation rather than a claim that combined fractures have no interaction.

Resolution is linear in the actor's fracture count and uses the catalog's constant-time structure-ID lookup; it does not scan anatomy voxels. No benchmark is required for this bounded M-sized change, but M12 performance work should measure lesion growth in long-running casualty scenarios.

During migration, motor-state updates retain the voxel-derived mobility and weapon-handling calculations. `TacticalActorPhysiology` combines voxel mobility with lesion-derived standing and movement capacity, and voxel weapon handling with lesion-derived upper-limb capacity, using `min`; it publishes the final combined `MusculoskeletalFunctionalState` alongside the existing `MobilityLevel` and `WeaponHandlingLevel` outputs. `CanStand` is false when final standing capacity is zero. The fracture state refreshes immediately after the authoritative interaction service persists new lesions and is recomputed on physiology ticks, so impact telemetry and subsequently created actions see the same constraint. Partially constrained entity-bound movement reduces speed and increases traversal time; new movement actions are rejected at zero mobility. Dynamic posture, crawling, and replanning already-running actions remain deferred. Consequently, the new bridge cannot improve a worse legacy result or erase pre-existing voxel damage. `FoundationsV2` impacts generate the persistent fractures consumed by this bridge; explicit `LegacyV1` comparison impacts continue to generate no lesions and therefore remain voxel-only. The model-version boundary must remain explicit until migration comparison criteria permit legacy removal.

Adding `CanStand` to capability telemetry advances the reference result and comparison output schemas to `reference-impact-result-v2` and `reference-impact-comparison-v2`. Scenario inputs and comparison keys are unchanged; fixed-shape v1 output consumers must migrate explicitly.

This bridge closes the fracture-specific functional requirement in DM-104. It does not implement the full DM-207 physiology-to-capability resolver: posture beyond standing, aiming, firing, reloading, communication, self-aid, causal attribution, and generalized physiological incapacity remain deferred to that issue.

## Serialization and inspection

The lesion base contract uses explicit JSON polymorphism and round-trips every subtype through `DamageModelJson`. `LesionDebugInspector` returns read-only rows containing structure, lesion kind, severity, treatment state, origin impact, and subtype detail. Reference impact outputs now mark lesions available and include their serialized representations.

## Parameter provenance and limitations

| Rule | Value | Classification | Purpose |
|---|---:|---|---|
| Cavity radius | `sqrt(deposited J) * 0.00035 m` | provisional, gameplay-calibrated | deterministic first-pass structure reach |
| Minimum lesion radius | `0.0005 m` | inferred | avoids degenerate serialized geometry |
| Severity divisor | `max(20 J, calibre * 3000 J/m)` | provisional | bounded relative injury severity |
| Transection | wound diameter >= vessel calibre | inferred | distinguishes laceration and transection |
| Displaced-fracture threshold | severity `> 0.30` | provisional; not clinically validated | strict boundary: `0.30` remains stable |
| Unstable-fracture threshold | severity `> 0.65` | provisional; not clinically validated | strict boundary: `0.65` remains displaced |
| Limited-use capacity | `0.75` | provisional gameplay mapping; not clinically validated | stable-fracture standing, movement, or upper-limb capacity |
| Severe-restriction capacity | `0.40` | provisional gameplay mapping; not clinically validated | displaced-fracture standing, movement, or upper-limb capacity |
| Structural-function-lost capacity | `0.00` | provisional gameplay mapping; not clinically validated | unstable-fracture capacity; prevents standing when weight-bearing |
| Multiple-fracture aggregation | minimum capacity (worst effect) | provisional deterministic rule; not clinically validated | stable and order-independent, but does not model additive or synergistic injury |

The standard map is deliberately coarse and is not a clinical predictor. Pleural volumes are represented as intersectable centreline capsules rather than exact membranes. Membrane-accurate geometry and finer solid-organ substructures remain follow-up refinements. M7 owns pressure-dependent bleeding and the general physiology-to-capability model; DM-104 supplies only the bounded musculoskeletal bridge described above. Legacy voxel-derived physiology remains temporarily active behind the existing migration boundary.
