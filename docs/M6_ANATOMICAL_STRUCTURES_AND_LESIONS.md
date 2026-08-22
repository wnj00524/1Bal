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

## Serialization and inspection

The lesion base contract uses explicit JSON polymorphism and round-trips every subtype through `DamageModelJson`. `LesionDebugInspector` returns read-only rows containing structure, lesion kind, severity, treatment state, origin impact, and subtype detail. Reference impact outputs now mark lesions available and include their serialized representations.

## Parameter provenance and limitations

| Rule | Value | Classification | Purpose |
|---|---:|---|---|
| Cavity radius | `sqrt(deposited J) * 0.00035 m` | provisional, gameplay-calibrated | deterministic first-pass structure reach |
| Minimum lesion radius | `0.0005 m` | inferred | avoids degenerate serialized geometry |
| Severity divisor | `max(20 J, calibre * 3000 J/m)` | provisional | bounded relative injury severity |
| Transection | wound diameter >= vessel calibre | inferred | distinguishes laceration and transection |

The standard map is deliberately coarse and is not a clinical predictor. Pleural volumes are represented as intersectable centreline capsules rather than exact membranes. Membrane-accurate geometry and finer solid-organ substructures remain follow-up refinements. M7, not M6, owns pressure-dependent bleeding and capability effects; legacy voxel-derived physiology remains temporarily active behind the existing migration boundary.
