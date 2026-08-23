# M12 validation and release hardening

M12 turns the deterministic damage pipeline into a versioned development surface. It does **not** certify clinical accuracy. TacticalSim produces broad, mechanistically plausible gameplay outcomes; it must not be used for diagnosis, prognosis, equipment certification, or medical training.

## Architecture and data flow

```text
versioned scenario + profile + root seed + ordered actions
  -> projectile interaction -> wound track -> persistent versioned lesions
  -> hemorrhage/thoracic/neurological state -> capability -> tactical actions
  -> reference observations / calibration report / benchmark report
  -> versioned save or deterministic replay envelope
```

Core remains authoritative and presentation-independent. SI units are used at calculation boundaries: metre, kilogram, second, joule, pascal; medical displays alone convert blood volume and flow to ml and ml/min. Randomness comes only from recorded named streams derived from a root seed.

## Parameter provenance

`ParameterProvenanceRegistry` is the machine-readable registry contract. Every newly introduced outcome-affecting constant must be registered with a stable ID, component, value and unit, exactly one classification, source, version, owner, and affected tests. The allowed classifications are externally sourced, empirically calibrated, inferred, provisional, and gameplay tuning.

The existing parameter inventories are the initial human-readable registry:

| Component | Canonical inventory | Owner | Version |
|---|---|---|---|
| projectile and numerical guards | `M5_DAMAGE_MODEL_FOUNDATIONS.md` | damage-model | M5 v2 |
| anatomy, lesion, fracture, nerve | `M6_ANATOMICAL_STRUCTURES_AND_LESIONS.md` | damage-model | anatomy-m6-v1 |
| hemorrhage and capability | `M7_HEMORRHAGE_CIRCULATION_AND_CAPABILITY.md` | physiology | M7 v1 |
| thoracic mechanics | `M8_THORACIC_INJURY_MODEL.md` | physiology | M8 v1 |
| treatment time, quality, and resources | `M9_TIMED_TREATMENT_ACTIONS_AND_RESOURCES.md` | treatment | M9 v1 |
| tactical mappings | `M10_ISOMETRIC_TACTICAL_INTEGRATION.md` | tactical | M10 v1 |
| profiles and uncertainty | `M11_CASUALTY_VARIATION_AND_BOUNDED_UNCERTAINTY.md` | variation | M11 v1 |

An unclassified constant is a coverage failure. A source may be a citation, design note, or an explicit statement that no external source exists; “unknown” is not an acceptable silent default.

## Reference injuries and model changes

`ReferenceInjurySuite.CreateBaseline()` enumerates all M12 cases: limb soft tissue, arterial, venous, junctional and concealed bleeding; stable/unstable fractures; spinal, pleural, hemothorax and cardiac injuries; cumulative hits; and effective, partial, failed, and interrupted treatment. Expected bands are deliberately broad validation expectations, while exact conservation, ordering, serialization, and state-transition behavior remains in software-invariant tests.

A developer adding a reference scenario must give it a stable ID, seeded input, qualitative expectations, outcome metrics with units and broad accepted bands, and at least one comparison case. A band change requires a model-change note explaining the old/new result, parameter provenance, model version decision, and reviewer. Never narrow a band solely until the current implementation passes.

## Calibration and sensitivity workflow

1. Fix model/anatomy versions, cohort seeds, and at least two distinct reference scenarios.
2. Register the candidate parameter and its permitted range.
3. Use `CalibrationRunner.Analyze` for one-at-a-time low/baseline/high sensitivity.
4. Use `CalibrationRunner.Compare` to rank candidates across multiple scenarios; single-case candidate scoring is rejected to expose overfitting.
5. Export `calibration-report-v1`, retain the baseline report, and record any accepted parameter/version change.

Calibration output is evidence for gameplay balancing, not evidence of clinical validity.

## Save and replay compatibility

Saves carry save, damage-model, anatomy, and lesion schema versions. Replays carry replay/model/anatomy versions, the root seed, named-stream seeds, and monotonically ordered action inputs. Readers reject missing or incompatible versions. A save migration must be explicitly registered from one exact schema to another; lesion schema mismatches are never silently reinterpreted. Replay schema changes currently fail clearly and require regeneration or a future explicit replay migrator.

When adding a lesion type, add its JSON discriminator, persistence round-trip test, migration decision, provenance entries, debug formatting, and reference case. When adding anatomy, increment the definition version if geometry or semantic identity changes and document whether old saves can map IDs without changing meaning. When adding treatment, cover completion, partial quality, failure, interruption, resource conservation, reassessment, replay payload, and reference outcomes.

## Performance pipeline

`DamageBenchmarkRunner` provides warm-up, repeated timing, JSON export (`damage-benchmark-v1`), and an explicit budget result. Benchmark definitions must cover anatomy construction, projectile resolution, lesion updates, physiology ticks at 1/16/32/64 actors, treatment updates, telemetry enabled/disabled, and save/replay serialization. Run Release builds on stable hardware; archive runtime/OS/CPU, iteration count, input size, median of repeated process runs, and raw JSON. A result over budget fails the performance gate. Optimization changes report before/after measurements from the same environment.

The unit suite contains only a fast harness contract test; wall-clock budgets do not belong in correctness tests. CI or the documented stable performance worker owns regression thresholds.

## Debug workflow

Reproduce with fixed profile, fixed model/anatomy versions, root seed, named streams, and ordered actions. Inspect the energy ledger, wound segments, structure intersections, persistent lesion inspector, blood destinations, treatment trace, physiology/capability state, then compare reference observations. Export the save/replay before changing parameters. Confirm the failure in an invariant or reference-band test, make the smallest causal change, rerun the full suite, cohort comparison, and performance pipeline.

## Release gate and known limitations

- All parameters have provenance; all required reference cases are present and inside reviewed bands.
- Deterministic replay round-trips and old saves either migrate explicitly or fail with a clear incompatibility.
- Full build/tests pass, model comparison has no unexplained drift, and benchmark budgets pass.
- Debug telemetry is ground truth and must not leak into ordinary fog-of-war UI.
- Reduced physiology, anatomy, projectile, treatment, and behavior models omit many real-world interactions. Outcomes are qualitative and population bounds are provisional. No result is an individual medical prediction.
