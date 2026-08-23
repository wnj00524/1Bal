# M5-M12 Implementation Audit and Agentic Remediation Plan

## Audit metadata

- Repository: `wnj00524/1Bal`
- Branch reviewed: `main`
- Commit reviewed: `e05477003f83a90d97b58ac58ccae257842cd503`
- Review date: 2026-08-23
- Scope: M5 through M12 of `docs/TacticalSim_Damage_Model_Roadmap.md`

## Executive conclusion

The repository implements many of the correct M5-M12 domain concepts, but it does not yet implement them as one authoritative end-to-end damage-model system.

The decisive architectural split is after lesion generation:

```text
Current live path

projectile
  -> M5 ProjectileInteractionService
  -> M6 lesions appended to TacticalActorPhysiology
  -> legacy voxel-derived TickPhysiology
  -> legacy scalar capability properties
  -> immediate legacy treatment mutations
  -> direct Godot movement / partially capability-aware actions
```

The intended path is:

```text
projectile / wearable inputs
  -> authoritative projectile interaction
  -> ordered named anatomical intersections
  -> persistent lesions
  -> hemorrhage / thoracic / neurological / musculoskeletal mechanisms
  -> one composite physiology tick
  -> casualty state + capability state
  -> tactical action policy + TurnResolver
  -> timed treatment / rescue / AI / scoring
  -> immutable ordinary/debug snapshots
  -> Godot presentation
  -> versioned save, replay, validation, cohort, calibration, and performance gates
```

M5 is substantial and should be retained as the foundation. M6 is the blocking migration: named structures and lesions exist, but destroyed voxels still remain medically authoritative in the live actor. M7-M10 are mostly credible model islands with direct tests but incomplete production composition. M11 is partly wired at actor construction while its terminal/wearable path remains disconnected. M12 is primarily scaffolding rather than a functioning release-hardening regime.

## Recommended status reclassification

| Milestone | Local component maturity | Authoritative/live-path maturity | Recommended status |
|---|---:|---:|---|
| M5 | 80-90% | 65-75% | Substantial; closure work remains |
| M6 | 70-80% | 30-40% | In progress; ontology migration incomplete |
| M7 | 65-75% | 10-20% | Component-complete prototype; integration incomplete |
| M8 | 65-75% | 5-15% | Component-complete prototype; not actor-owned |
| M9 | 55-65% | 5-15% | Contracts and local behavior only |
| M10 | 50-60% | 5-15% | Policy kit, not tactical integration |
| M11 | 50-60% | 15-25% | Partial hooks; terminal path/cohort outputs incomplete |
| M12 | 30-40% | 5-10% | Scaffolding; release gates not implemented |

Percentages are estimates against observable roadmap acceptance criteria, not line-of-code estimates.

## Definition of “implemented”

A roadmap issue is complete only when all applicable layers are satisfied:

1. **Contract** — domain type and invariants exist.
2. **Mechanism** — required causal behavior is implemented.
3. **Composition** — the authoritative actor/simulation service owns and invokes it.
4. **Tactical consumption** — actions, AI, scenarios, and presentation consume the same state.
5. **Observability** — immutable telemetry explains the causal chain without recomputation.
6. **Persistence/replay** — state and ordered inputs round-trip under explicit versions.
7. **Validation** — integrated scenarios test intended qualitative ordering and broad outcome bands.
8. **Release evidence** — build, tests, deterministic replay, provenance, and performance gates run in automation.

---

# Milestone audit

## M5 — Damage-model foundations

### Present

- Typed wrappers for distance, area, volume, mass, density, time, energy, pressure, and flow.
- Canonical damage-model JSON converters.
- Projectile energy ledger and deterministic wound-track contracts.
- Central `IProjectileInteractionService` used by the Godot client.
- Deterministic named random streams and metadata.
- Real reference-impact harness with CLI, JSON/text output, model comparison, timelines, and deterministic hash.

### Gaps

- Quantity wrappers still unwrap to floats for most calculations and permit several semantically invalid negative quantities.
- M11 terminal/wearable energy transformations bypass the authoritative M5 ledger.
- Tissue drag evaluation is under-specified and not clearly tied to actual impact speed/Mach.
- Ricochet is predominantly terminal bookkeeping rather than a continued reflected trajectory.
- The reference harness still samples the legacy physiology path.
- M5 issue-state documentation is stale relative to GitHub.

## M6 — Anatomical structures and persistent lesions

### Present

- Versioned named anatomy catalogue with major vessels, bones, nerves, organs, pleural spaces, and pericardium.
- Persistent lesion hierarchy and repository.
- Lesion JSON/debug inspection.
- Musculoskeletal and neurological functional resolvers.

### Gaps

- `TacticalActorPhysiology.TickPhysiology` still derives bleeding and organ consequences from destroyed voxels.
- Wound tracks still contain provisional voxel identities; named anatomy is queried after traversal instead of producing canonical ordered intersections.
- Lesion generation can duplicate one structure across adjacent wound segments unless aggregation/idempotency is added.
- Structure ordering should be by geometric entry distance, not merely catalogue stability.
- Re-processing the same impact ID lacks a defined idempotent result.
- Lesion timestamps are not driven by the simulation clock.
- Debug lesion inspection is not fully connected to the production Godot debug UI.
- Actual GitHub state leaves DM-105/106/107 open while the markdown issue map reports M6 complete.

**Disposition:** M6 is the primary blocker. Do not deepen later model islands before making structures/lesions authoritative.

## M7 — Hemorrhage, circulation, and capability

### Present

- Independent arterial/venous/parenchymal bleeding sources.
- Explicit blood destinations and conservation telemetry.
- Simplified hemostasis, compression, movement rebleeding.
- Reduced cardiovascular and oxygen-delivery state.
- Explicit casualty state with irreversible death latch.
- Deterministic physiology-to-capability resolver with explanatory reasons.

### Gaps

- The live casualty does not own `HemorrhagePhysiologyModel` as authoritative state.
- Lesion-to-bleeding synchronization is manual rather than automatic and idempotent.
- M7 physiology is not the state advanced by the production scenario loop.
- Turn resolution does not centrally enforce casualty/capability state transitions.
- Legacy scalar mobility/weapon-handling state remains authoritative in existing actions.

## M8 — Thoracic injury model

### Present

- Bilateral pleural gas/blood/pressure state.
- Simple/open/tension pneumothorax mechanics.
- Hemothorax and pericardial tamponade effects.
- Seal and decompression quick-treatment surfaces.
- Direct reference-style unit tests for required mechanisms.

### Gaps

- The production casualty does not own a `ThoracicInjuryModel`.
- M6 pleural/cardiac lesions are not automatically synchronized into M8 mechanisms.
- Thoracic state is not included in complete save/replay state.
- Chest-seal and decompression effects are not routed through the M9 timed treatment system in production.
- Thoracic deterioration does not yet propagate through a single integrated actor tick into live tactical capability.

## M9 — Timed treatment actions and resources

### Present

- `TreatmentAction`, inventory, quality, interruption metadata, reassessment object, and trace.
- Tourniquet, pressure, packing, and debug quick-treatment helpers.
- Local tests for time, equipment, cancellation, and physiology continuing during treatment.

### Gaps

- Production Godot treatment is still immediate mutation rather than scheduled `TreatmentAction` execution.
- Hands, posture, reach/proximity, movement, suppression, and provider incapacitation requirements are not centrally enforced.
- Reassessment is data, not a scheduled simulation event.
- Thoracic treatment is not integrated as timed treatment.
- Limb placement is mostly trace metadata rather than authoritative anatomical targeting.
- Tourniquets do not yet produce explicit distal perfusion consequences feeding later capability.
- Sustained direct pressure can persist after provider release; it must occupy the provider while sustained.
- Attached devices/equipment need persistent state for removal, failure, save/replay, and reassessment.
- Production debug tooling should expose apply/remove/fail/partial operations behind an explicit gate.

## M10 — Isometric tactical integration

### Present

- `CapabilityActionPolicy` for action gating/cost/stability.
- Drag/carry action.
- Casualty behavior, teammate response, overlays, and scenario scorer.
- DI registrations and direct tests.

### Gaps

- Existing Move/Aim/Shoot actions do not uniformly use the centralized policy.
- TurnResolver does not enforce capability gates as the sole action eligibility boundary.
- Godot movement bypasses TurnResolver and M10 policy.
- Drag/carry is not wired into navigation/UI/AI/scenario flow.
- Transport does not automatically apply movement stress or interrupt treatment where configured.
- Behavior policies are not part of the production AI update loop.
- Overlay factory output is not fully rendered in Godot.
- Scenario scorer is not tied to authoritative scenario completion.
- No integrated scenario currently demonstrates the intended suppression/rescue/treatment/exposure/mission trade.

## M11 — Variation and bounded uncertainty

### Present

- Serializable casualty profiles.
- Part of the profile is consumed by actor construction.
- Bounded seeded physiology variation with deterministic-off mode.
- Terminal projectile profile and wearable barrier types.
- Deterministic generic cohort runner with timing and failure isolation.

### Gaps

- Oxygen-carrying capacity and comorbidity modifiers are declared but not causally integrated.
- Physiological uncertainty is not automatically sampled/owned/persisted by scenario actor construction.
- Terminal projectile profiles are not a stage of `IProjectileInteractionService`.
- Wearable barriers are not an authoritative pre-entry stage.
- Energy removed by wearables/terminal transformations is not fully reconciled through the M5 ledger.
- Non-penetrating blunt energy does not create an injury/capability consequence.
- Fragmentation does not yet create fragment tracks or explicit fragmentation energy accounting.
- No versioned projectile/wearable catalogue is wired into scenario content.
- Cohort tooling lacks damage-model distribution summaries, percentiles, and model-version comparison output.

## M12 — Validation and release hardening

### Present

- Contracts/helpers for provenance, reference injuries, calibration, benchmarks, save/replay, and documentation.
- Unit tests for those helper contracts.

### Gaps

- No production parameter provenance inventory covering outcome-affecting constants.
- Baseline reference injury suite is placeholder data rather than executable integrated injuries with mechanism-specific metrics and relative comparisons.
- Calibration tooling is detached from a versioned parameter override mechanism and real cohort execution.
- Benchmark runner lacks canonical workloads and stable-worker/CI integration.
- Save state currently persists lesions rather than complete medical/tactical state.
- Replay stores generic actions/seeds but has no authoritative executor and no golden final/event hash.
- Persistence is not consistently based on the repository’s canonical damage-model JSON contract.
- No explicit migration chain is registered and validated.
- Issue/milestone documentation does not match actual GitHub state.

---

# Target architecture

Introduce one Core-owned composition root (name to match repository conventions), conceptually:

```text
ActorMedicalState
  - CasualtyProfile + realized PhysiologicalVariation
  - IAnatomicalStructureCatalog / spatial index
  - LesionRepository
  - bleeding source registry
  - HemorrhagePhysiologyModel
  - ThoracicInjuryModel
  - neurological + musculoskeletal functional state
  - CapabilityState
  - CasualtyState
  - active treatments / attached equipment
  - treatment inventory
  - simulation time
  - deterministic random stream state/metadata
  - immutable ordinary/debug snapshots
```

Expose one authoritative advance boundary. All submodels must advance exactly once from one monotonic actor clock. Godot types must never enter this boundary.

Use explicit model-version migration rather than indefinite dual behavior:

- `legacy-v1`: existing voxel-derived medical behavior.
- `m5-foundations-v2`: current transition behavior for comparison.
- new integrated identifier: authoritative M5-M12 path.

Legacy adapters may project the integrated state into old read-only interfaces during migration, but integrated actors must not accept legacy medical mutation.

---

# Agentic remediation work packages

## Wave 0 — Establish trustworthy execution and status

### R0-01 — Current CI and architecture gate

**Deliverables**

- Add/repair CI for solution build and full tests on the current head.
- Run architecture tests that forbid Core -> Godot/presentation dependencies.
- Publish test result artifacts tied to commit SHA.
- Treat compiler warnings introduced by changed files as failures.

**Acceptance**

- Current head has a reproducible green build/test check.
- Failure artifacts identify the exact test/commit.

### R0-02 — Truthful roadmap/issue state

**Deliverables**

- Reconcile `DAMAGE_MODEL_ISSUE_MAP.md` with actual GitHub issue state.
- Do not mark a roadmap item Done solely because a type/direct unit test exists.
- Add a documented status convention distinguishing local component completion from integrated acceptance.

### R0-03 — Integrated characterization suite before migration

**Deliverables**

- Add tests that characterize the current live projectile -> lesion -> legacy physiology -> action path.
- Record deterministic hashes/metrics for representative arm, leg, chest, and abdominal impacts.
- These are migration evidence, not permanent medical truth.

---

## Wave 1 — Complete M5/M6 and establish one injury ontology

### R1-01 — Actor medical composition root

**Deliverables**

- Introduce the authoritative actor medical-state owner.
- Move simulation time, profile, lesions, mechanism models, capability, treatments, inventory, and random metadata under it.
- Provide immutable debug/ordinary snapshots.
- Add a compatibility adapter for existing `IActorPhysiology` reads where necessary.

**Acceptance**

- One public tick advances the actor medical state exactly once.
- No Godot dependency.
- Integrated-model actors reject legacy mutation paths.

### R1-02 — Canonical named structure intersections

**Deliverables**

- Add immutable `StructureIntersection` records with stable structure ID/type, entry/exit points, path distance, and ordering.
- Make projectile traversal produce/query named anatomy as the canonical structure identity.
- Keep voxels as spatial/local-material implementation detail only.

**Acceptance**

- Integrated wound tracks use named structure IDs rather than voxel IDs.
- Overlapping structures are ordered geometrically by entry distance with deterministic tie-breaking.

### R1-03 — Deterministic lesion aggregation and idempotency

**Deliverables**

- Aggregate contiguous/intersecting wound segments per named structure/impact before lesion generation.
- Define stable lesion identity and duplicate-impact handling.
- Replace epoch timestamps with simulation timestamps.

**Acceptance**

- Replaying one impact ID cannot duplicate lesions.
- One structure is not spuriously represented as many adjacent lesions without an explicit mechanism reason.

### R1-04 — Remove voxel-derived medical authority

**Deliverables**

- Stop using destroyed voxel counts/organ percentages as the integrated model’s primary bleeding or functional source.
- Retain voxels only for collision, visualization, and local material data where useful.
- Route bleeding, thoracic, neurological, and musculoskeletal consequences from persistent lesions/structures.

**Acceptance**

- Integrated actor physiology can advance without scanning destroyed voxels for bleeding/organ-function outcomes.
- Legacy path remains only behind the legacy model version.

### R1-05 — Close M5 energy/unit gaps

**Deliverables**

- Define non-negative versus signed quantity semantics where needed.
- Route every terminal/wearable energy transformation into the authoritative ledger.
- Make tissue drag evaluation depend on the actual projectile state.
- Decide and implement continued ricochet traversal or explicitly constrain the model and tests.

**Acceptance**

- Energy conservation includes pre-entry barriers, deformation/fragmentation, body deposits, outgoing projectile state, and numerical residual.

### R1-06 — Upgrade reference impact harness to integrated state

**Deliverables**

- Allow the harness to instantiate the integrated actor model.
- Output named intersections, persistent lesions, mechanism sources, complete physiology/capability timeline, treatment state, random metadata, and full-state hash.
- Keep cross-version comparison using fresh targets.

---

## Wave 2 — Make M7/M8 authoritative actor physiology

### R2-01 — Lesion-to-mechanism synchronizer

**Deliverables**

- Deterministically map vessel/tissue lesions to bleeding sources.
- Map pleural/lung/cardiac lesions to thoracic mechanisms.
- Map fracture/nerve/spinal lesions to functional state.
- Maintain a stable lesion -> mechanism identity map.

**Acceptance**

- Adding/removing/updating a lesion produces exactly one corresponding mechanism update.
- Synchronization is idempotent.

### R2-02 — Composite physiology tick and conservation

**Deliverables**

- Define causal update order for hemorrhage, pleural/pericardial effects, cardiovascular state, oxygen delivery, neuro/musculoskeletal state, casualty state, and capability.
- Advance all from one actor clock.
- Preserve blood conservation across circulating and destination compartments.

**Acceptance**

- Large/small timestep comparisons remain within documented tolerance.
- No pleural/pericardial double counting.

### R2-03 — Casualty/capability authority

**Deliverables**

- Make M7 `CasualtyState` and `CapabilityState` the sole tactical impairment contract for integrated actors.
- Remove production decisions based on destroyed-organ percentages or legacy scalar impairment values.
- Preserve traceable reasons.

### R2-04 — Thoracic lesion integration

**Deliverables**

- Integrated actor owns one M8 model.
- Side-specific pleural gas/blood, lung compression, tension, and tamponade derive from lesion/mechanism state.
- State appears in debug snapshots and persistence.

---

## Wave 3 — Replace M9/M10 production bypasses

### R3-01 — Route all production treatment through TurnResolver

**Deliverables**

- Replace immediate Godot treatment mutation with command -> `TreatmentAction` scheduling.
- Add timed thoracic treatments.
- Ensure physiology progresses while treatment consumes provider time.

### R3-02 — Enforce treatment requirements and interruptions

**Deliverables**

- Enforce proximity, hands, posture, required equipment, and provider capability.
- Automatically interrupt on configured movement, suppression, incapacity, or cancellation.
- Convert reassessment into scheduled simulation events/actions.

### R3-03 — Correct treatment physiology/state

**Deliverables**

- Model tourniquet anatomical placement and distal perfusion consequences.
- Distinguish sustained pressure (continuous provider occupation) from packing (persistent intervention).
- Persist attached devices and their state.
- Support recurrence/failure and reassessment.

### R3-04 — Centralize action capability policy

**Deliverables**

- Make movement, posture, aim, fire, reload, command, self-aid, treatment, and transport query one capability-action policy.
- Severe impairment blocks actions centrally.
- Remove independent action-specific reads of legacy pain/shock/organ damage.

### R3-05 — Integrate casualty transport

**Deliverables**

- Wire drag/carry into navigation, commands, TurnResolver, and world movement.
- Apply transport burden, treatment interruption, movement stress, exposure, and casualty movement through authoritative events.

### R3-06 — Integrate AI, overlays, and scoring

**Deliverables**

- Execute casualty/teammate behavior policies in production AI updates using observable state only.
- Render ordinary/debug casualty overlays from immutable snapshots.
- Invoke casualty-aware scoring at scenario resolution.

---

## Wave 4 — Make M11 part of the authoritative input pipeline

### R4-01 — Actor-owned profiles and realized uncertainty

**Deliverables**

- Scenario actor factory validates profile, samples bounded variation from named streams, and stores the realized sample as actor state.
- Apply every supported field causally or explicitly remove/defer it.
- Persist profile schema/version and realized variation.

### R4-02 — Integrate terminal projectile profiles

**Deliverables**

- Route construction/yaw/deformation/fragmentation behavior through the projectile interaction pipeline.
- Add versioned projectile profile catalogue.
- Record terminal state transitions and energy allocations in telemetry/ledger.

### R4-03 — Integrate wearable barriers and blunt effects

**Deliverables**

- Add ordered clothing/armor layers before body entry.
- Account for all absorbed/removed energy.
- Allow non-penetrating blunt transfer to generate an explicit lesion/effect where configured.
- Add debug snapshot and replay fields.

### R4-04 — Real cohort/model comparison tooling

**Deliverables**

- Run hundreds/thousands of integrated seeded cases without Godot.
- Export distributions, percentiles, failure diagnostics, elapsed time, model/profile/parameter versions, and baseline-vs-candidate comparison.
- Keep output ordering deterministic.

---

## Wave 5 — Implement M12 as an actual release gate

### R5-01 — Populate and enforce parameter provenance

**Deliverables**

- Create a production provenance catalogue covering every outcome-affecting parameter introduced by M5-M11.
- Store stable ID, component, value/unit, classification, source/design note, version, owner, and affected tests.
- CI fails when the required parameter inventory contains an unregistered ID.

### R5-02 — Executable reference injury suite

**Deliverables**

Implement real integrated reference cases for:

- soft-tissue limb wound without major-vessel injury;
- major arterial/venous injury;
- junctional bleeding;
- concealed abdominal bleeding;
- stable/unstable fracture;
- spinal injury;
- simple/tension pneumothorax;
- hemothorax;
- cardiac injury;
- cumulative hits;
- effective/partial/failed/interrupted treatment.

Each case must include seeded inputs, mechanism-specific metrics with units, broad reviewed bands, qualitative expectations, and at least one relative comparison.

**Critical rule:** never widen/narrow a band solely to make current output pass.

### R5-03 — Bind calibration/sensitivity to real versioned parameters

**Deliverables**

- Introduce immutable/versioned parameter sets and explicit override ranges.
- Run sensitivity against multiple integrated scenarios/cohorts.
- Export baseline and candidate reports with model/profile/seed/parameter versions.
- Require model-change notes for accepted parameter changes.

### R5-04 — Complete save/load state

**Deliverables**

Persist, at minimum:

- model/anatomy/lesion/profile/parameter schema versions;
- simulation clock;
- persistent lesions;
- bleeding sources and control/clot state;
- blood compartments;
- cardiovascular/oxygen/thoracic state;
- casualty/capability state;
- active/completed treatments and attached devices;
- treatment inventory;
- scheduled medical/tactical events required for continuation;
- realized variation and random stream state.

Use one canonical JSON configuration and test full value equality, not subtype only.

### R5-05 — Replay executor and golden state hashes

**Deliverables**

- Define typed/versioned replay action payloads rather than opaque action strings.
- Execute replay from saved scenario/profile/version/root seed/named stream state and ordered actions.
- Produce deterministic event hash and final-state hash.
- Add explicit incompatibility/migration decisions for schema changes.

### R5-06 — Canonical performance pipeline and release gate

**Deliverables**

Canonical workloads must cover:

- anatomy construction;
- projectile -> wound -> lesion resolution;
- lesion/mechanism update;
- integrated physiology tick for 1/16/32/64 actors;
- treatment updates;
- telemetry enabled/disabled;
- save/load/replay serialization/execution.

Run release builds on documented stable hardware/worker, archive runtime/OS/CPU/iterations/input size/raw JSON, and fail agreed regression budgets. Optimization PRs require before/after evidence from the same environment.

---

# Dependency and parallelization rules

1. **R0 work may run immediately.**
2. **R1-01/R1-02/R1-03 are the first substantive architecture tranche.** Do not integrate M7-M12 into the live actor before the authoritative composition and lesion identity model are established.
3. R1-04 depends on the composition root and lesion/mechanism boundary.
4. R2 depends on R1 ontology work.
5. R3 depends on authoritative R2 capability/casualty state.
6. R4-02/R4-03 depend on R1-05 energy-ledger integration rules.
7. R5 reference/calibration/persistence/replay work must execute the integrated model, not standalone helper classes.
8. Avoid parallel edits to single-owner files such as the actor composition root, projectile interaction service, TurnResolver, canonical JSON/persistence contract, and Godot simulation manager unless ownership is explicitly partitioned.

---

# Mandatory integrated vertical slices

## VS-01 — Major limb bleed under fire

```text
fixed-seed limb impact
  -> named femoral/brachial intersection
  -> persistent vessel lesion
  -> pressure-dependent bleeding + destination conservation
  -> degrading cardiovascular/capability state
  -> movement/aim/fire/reload consequences
  -> responder drag or treatment decision
  -> timed proximal tourniquet through TurnResolver
  -> movement/suppression interruption variant
  -> reassessment
  -> save/replay identical final hash
```

Required comparisons:

- muscle-only near miss vs major-vessel hit;
- untreated vs effective vs partial/failed tourniquet;
- treatment in place vs drag then treatment.

## VS-02 — Thoracic deterioration

```text
fixed-seed chest impact
  -> named lung/pleural or cardiac structures
  -> side-specific lesions
  -> pleural gas/blood or pericardial blood
  -> ventilation/cardiac-output effects
  -> capability deterioration over time
  -> timed side-specific seal/decompression
  -> wrong-side and failed-intervention variants
  -> recurrence where leak persists
```

## VS-03 — Concealed abdominal bleeding

```text
abdominal vascular/parenchymal lesion
  -> concealed blood destination
  -> little/no visible external blood
  -> declining oxygen delivery/capability
  -> external packing rejected or ineffective
  -> distinct debug explanation
```

## VS-04 — Multiple-hit cumulative trauma

```text
ordered hits with persistent lesions
  -> cumulative mechanism state
  -> no actor reset between impacts
  -> save after first hit
  -> load and apply second hit
  -> same result as uninterrupted replay
```

## VS-05 — Wounded hostile remains tactically active

```text
non-immediately-incapacitating hit
  -> degraded capability
  -> deterministic AI transition using observable state
  -> continued threat / withdrawal / surrender policy / collapse according to mission inputs
```

---

# Cross-cutting verification matrix

| Property | Required invariant/test |
|---|---|
| Projectile energy | Incoming equals outgoing + named deposits/transfers + residual within tolerance |
| Blood | Baseline equals circulating + every destination, without pleural/pericardial double counting |
| Determinism | Same versions, inputs, seed, and ordered actions yield identical event/full-state hash |
| Structure identity | Integrated wound tracks/lesions use stable named structure IDs, not voxel IDs |
| Lesion idempotency | Reprocessing one impact ID cannot duplicate lesions/mechanism sources |
| Time | One monotonic actor clock; each submodel advances once per public tick |
| Timestep behavior | Large vs small timesteps stay within documented tolerances |
| Capability | Tactical layer consumes capability/casualty snapshots, not voxels or organ percentages |
| Treatment | Production treatment is timed, resource-aware, interruptible, and replayable |
| UI boundary | Godot submits commands and renders snapshots; it does not calculate/mutate authoritative physiology |
| Persistence | Full value equality after round-trip, including geometry, compartments, treatments, clocks, and units |
| Replay | Replay executor reproduces ordered events and final state |
| Provenance | Every required outcome parameter has exactly one valid registry entry |
| Validation | Required reference injuries run the real integrated model with reviewed bands/orderings |
| Performance | Canonical workloads run on a documented worker with regression budgets/raw metadata |

---

# Agent operating rules

## Required workflow

1. Read the roadmap issue, dependencies, this audit, production path, and relevant tests.
2. State the authoritative owner/data flow before editing.
3. Add or enable a failing test that reaches the production composition boundary.
4. Make the smallest architecture-consistent change.
5. Preserve deterministic ordering and explicit units.
6. Update immutable telemetry/snapshots for hidden state.
7. Run targeted tests, full tests, reference scenarios, and applicable benchmarks.
8. Update provenance for every new/changed outcome parameter.
9. Update schema/model versions and migration decisions when public state changes.
10. Report before/after behavior, tests, artifacts, compatibility, performance, and remaining risks.

## Prohibited shortcuts

Agents must not:

- create another competing physiology/damage pipeline;
- call a new model only from tests and claim integration;
- place authoritative calculations or medical mutation in Godot;
- retain inert profile/configuration fields without explicit deferral;
- introduce unseeded/hash-derived gameplay randomness;
- adjust validation bands merely to satisfy current output;
- reduce velocity/energy without ledger allocation;
- use destroyed voxels as the integrated model’s bleeding/organ-function source;
- implement immediate production treatment outside TurnResolver;
- close issues merely because classes exist while production bypasses them;
- change persistence format without explicit version/compatibility decision;
- remove legacy behavior before migration/comparison acceptance is met;
- perform unrelated repository-wide refactors inside bounded tickets.

## Completion report template

```markdown
## Issue implemented

## Authoritative owner and data flow

## Files changed

## Behavior before

## Behavior after

## Tests added or changed

## Commands and scenarios run

## Build/test/CI result

## Deterministic hashes or comparison artifacts

## Parameters added or changed and provenance IDs

## Persistence/replay compatibility

## Performance impact

## Known limitations

## Follow-up issues
```

---

# Definition of done for M5-M12

M5-M12 may be represented as implemented only when all of the following are true:

- [ ] A projectile traverses ordered named anatomical structures through one Core service.
- [ ] The energy ledger covers wearables, terminal/construction changes, fragments/blunt transfer, body structures, and outgoing state.
- [ ] Persistent lesions are deterministic, aggregated, idempotent, and simulation-timestamped.
- [ ] Integrated physiology derives from lesions/compartments, not destroyed-voxel percentages.
- [ ] Hemorrhage, thoracic, neurological, and musculoskeletal state share one actor clock and causal update.
- [ ] Casualty state and capability are the only tactical impairment contracts.
- [ ] Move, posture, aim, fire, reload, command, self-aid, treatment, and transport use centralized capability policy.
- [ ] Every production treatment is a timed action with enforced requirements, resources, interruption, and reassessment.
- [ ] Godot only submits commands and renders immutable snapshots.
- [ ] Profiles and bounded uncertainty are actor-owned, causal, seeded, and replayable.
- [ ] Projectile construction and wearable layers are data-driven stages in the authoritative pipeline.
- [ ] Cohort runs export reproducible distributions and model comparisons.
- [ ] Parameter provenance is populated and CI-enforced.
- [ ] Reference injury cases execute the real model with reviewed bands and relative comparisons.
- [ ] Complete saves round-trip and golden replays reproduce event/full-state hashes.
- [ ] Canonical performance workloads run on a documented worker and report regressions.
- [ ] Current CI is green and attached to the reviewed commit.
- [ ] GitHub issue/milestone state and repository documentation match the evidence.

---

# Evidence map

- Roadmap: `docs/TacticalSim_Damage_Model_Roadmap.md`
- Typed quantities: `TacticalSim.Core/Units/PhysicalQuantities.cs`
- Projectile interaction: `TacticalSim.Core/Damage/Ballistics/ProjectileInteractionService.cs`
- Reference harness: `TacticalSim.Core/Damage/Scenarios/ReferenceImpactRunner.cs`
- Live actor physiology: `TacticalSim.Core/ActorPhysiology.cs`
- Anatomy/lesions: `TacticalSim.Core/Damage/Anatomy/`
- M7: `TacticalSim.Core/Damage/Physiology/HemorrhageModel.cs`
- M8: `TacticalSim.Core/Damage/Physiology/ThoracicInjuryModel.cs`
- M9: `TacticalSim.Core/Damage/Treatment/TreatmentModel.cs`
- M10: `TacticalSim.Core/Tactical/TacticalIntegration.cs`
- M11: `TacticalSim.Core/Damage/Variation/`
- M12 validation: `TacticalSim.Core/Damage/Validation/`
- M12 persistence: `TacticalSim.Core/Damage/Persistence/DamageModelPersistence.cs`
- Godot live path: `TacticalSim.GodotClient/SimulationManager.cs`
- Issue-state map: `docs/DAMAGE_MODEL_ISSUE_MAP.md`

## Implementation priority

The first substantive engineering tranche should be:

1. current CI/integrated characterization;
2. actor medical composition root;
3. canonical named structure intersections;
4. deterministic lesion aggregation/idempotency;
5. remove voxel-derived medical authority;
6. lesion-to-mechanism synchronization;
7. composite M7/M8 physiology tick;
8. casualty/capability authority;
9. timed treatment and centralized tactical action policy;
10. M11 terminal/wearable integration;
11. complete M12 persistence/replay/validation/performance gates.

The key rule is simple: **do not add more standalone model classes before making the existing M5-M12 components participate in one authoritative vertical slice.**