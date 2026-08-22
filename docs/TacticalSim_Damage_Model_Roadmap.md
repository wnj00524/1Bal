# TacticalSim Damage Model Development Roadmap

**Status:** Implementation planning document  
**Audience:** Junior developers, technical leads, reviewers, and agentic coding systems  
**Repository:** `wnj00524/1Bal`  
**Primary product:** Isometric tactical simulation  
**Primary design goal:** Explore tactical decisions and consequences in high-threat environments  
**Not a product goal:** Clinical first-aid instruction, medical certification, or exact prediction of real casualty outcomes

---

## 1. Purpose of this document

This document converts the current damage-model review into an implementation-ready repository roadmap. It is intended to be copied into the repository and used to create:

- GitHub milestones;
- GitHub issues;
- project-board work items;
- implementation phases;
- architecture decision records;
- test and validation plans;
- agentic coding prompts;
- pull-request acceptance criteria.

The existing engine has a useful foundation: deterministic tactical actions, a simulation timeline, a spatial body model, projectile calculations, physiology state, treatment hooks, and a substantial automated test suite. The next stage is not to replace the whole project. It is to preserve the tactical engine and progressively replace the weakest parts of the injury model with a single, coherent, testable system.

The roadmap deliberately prioritizes **tactical consequences** over medical-detail maximalism. A casualty model is valuable here when it creates meaningful decisions about:

- whether an actor can continue fighting;
- whether an actor can move, crawl, aim, reload, communicate, or self-extract;
- whether another actor must expose themselves to assist;
- how long a casualty remains tactically useful;
- whether evacuation is urgent;
- whether treatment time, equipment, and exposure are worth the opportunity cost;
- how injuries accumulate across an engagement.

Exact clinical forecasting is not required. Causal coherence, internal consistency, reproducibility, and useful tactical differentiation are required.

---

## 2. Product constraints and design guardrails

### 2.1 Isometric tactical presentation

The game is viewed from an isometric perspective. The internal model may be spatially detailed, but the player will not normally inspect centimetre-scale anatomy during ordinary play.

Implications:

1. The damage model must be independent from render detail.
2. Projectile and injury resolution should be event-driven, not dependent on visible frame-by-frame bullet simulation.
3. Tactical feedback should use clear states, icons, overlays, animations, and concise telemetry.
4. Internal anatomy visualization may exist as a debug mode, developer tool, replay view, or optional inspection panel.
5. Visual gore is not necessary for fidelity. Functional consequence is more important than graphical detail.
6. Simulation work must be budgeted for multiple actors and simultaneous events, not a single anatomical dummy.

### 2.2 Omniscient information is allowed during development

Ground-truth information is currently useful and should remain available while the model is being debugged.

Development and debug interfaces may expose:

- exact structures hit;
- wound tracks;
- exact blood volume;
- exact bleeding sources and destinations;
- exact oxygen-delivery state;
- exact capability penalties;
- exact treatment effects;
- exact projectile energy accounting;
- expected and actual model transitions.

Do not remove this information merely to simulate diagnostic uncertainty. Instead, establish a clean separation between:

- **ground-truth debug telemetry**, which may remain omniscient; and
- **gameplay presentation**, which may later show simplified or uncertain information if the design calls for it.

The observation and uncertainty layer is therefore a later milestone, not a prerequisite for improving the core damage model.

### 2.3 The game is about tactics, not teaching first aid

Treatment mechanics exist to create tactical trade-offs. They should not become a procedural medical training syllabus.

The model should answer questions such as:

- Does this intervention materially change the casualty's trajectory?
- How long does it take?
- Does it require the responder to stop shooting, move closer, use both hands, or enter danger?
- Can it fail, be interrupted, or require reassessment?
- Does the actor have the equipment and capability to perform it?

The model does not need to teach exact placement landmarks, procedural memorization, drug dosing, or clinical documentation standards.

### 2.4 Fidelity target

The target is **mechanistically plausible tactical simulation**, not medical digital-twin fidelity.

The model should be able to distinguish, at minimum:

- a painful but non-critical wound;
- a disabling limb injury;
- a rapidly lethal major-vessel injury;
- concealed internal bleeding;
- impaired breathing without immediate collapse;
- progressive thoracic deterioration;
- neurological incapacitation;
- temporary unconsciousness;
- irreversible death;
- effective, partial, ineffective, and interrupted treatment.

The model should avoid false precision. Exact values may be exposed in debug mode, but player-facing language should not imply clinical certainty unless the underlying model has been validated for that claim.

---

## 3. Current-state summary

The current repository already contains several important capabilities:

- deterministic simulation timing;
- concurrent action scheduling;
- actor physiology ticking;
- persistent actor state;
- a voxel-based anatomical dummy;
- ballistic flight integration;
- tissue energy deposition;
- basic bone interaction;
- blood-volume loss;
- pressure-aware bleeding;
- respiratory deterioration;
- pain, shock, mobility, and weapon-handling effects;
- tourniquet, packing, chest-seal, and decompression hooks;
- Godot client integration;
- extensive unit and integration tests.

The largest current limitations are:

1. More than one projectile-to-injury pipeline exists.
2. The core `ProcessImpact` path does not conserve projectile energy across multiple voxels.
3. Tissue damage uses inconsistent units and heuristic constants.
4. Anatomy lacks explicit major vessels, major peripheral nerves, clinically meaningful bone segments, and physiological compartments.
5. Bleeding is based mainly on destroyed organ volume rather than explicit wounds.
6. Thoracic injury is reduced largely to destroyed lung fraction and a single tension scalar.
7. Treatments are instantaneous and perfectly effective.
8. Physiological outputs are not yet cleanly translated into tactical capability states.
9. Tests establish implementation consistency more often than model validity.
10. High-resolution projectile stepping is located in the Godot client instead of a single authoritative core service.

The roadmap below addresses these limitations in dependency order.

---

## 4. Core development principles

All issues and pull requests must follow these principles.

### 4.1 One authoritative model

`TacticalSim.Core` is the source of truth for:

- projectile interaction;
- wound generation;
- injury state;
- physiological progression;
- treatment effects;
- actor capability state.

The Godot client must consume core results. It must not independently implement medical or ballistic rules that produce different outcomes.

### 4.2 Deterministic replay

Every simulation must be reproducible from:

- model version;
- scenario definition;
- actor profiles;
- random seed;
- ordered player and AI actions.

Stochastic variability is permitted only when it is seeded and recorded.

### 4.3 Typed quantities and conservation

Energy, distance, mass, pressure, volume, flow, time, and rates must not be interchangeable primitive floats without explicit conversion boundaries.

At minimum, add strong domain types or a documented unit wrapper layer. The codebase must support automated conservation checks for:

- projectile energy;
- circulating blood volume;
- blood assigned to internal or external compartments;
- consumable equipment;
- actor action time.

### 4.4 Separate injury, physiology, and gameplay consequence

Do not directly map a voxel to a final tactical penalty.

Use three conceptual layers:

1. **Injury layer:** What structure was damaged and how?
2. **Physiology layer:** What systemic state follows over time?
3. **Capability layer:** What can the actor do now?

Example:

```text
Femoral artery transection
    -> high-pressure external bleeding
    -> falling circulating volume and cerebral perfusion
    -> reduced sprint speed, delayed actions, collapse, unconsciousness
```

### 4.5 Prefer high-value abstractions over raw detail

Do not add anatomical detail merely because it is possible. Add a structure when it changes a tactical outcome or treatment choice.

Priority order:

1. major vessels;
2. bleeding compartments;
3. weight-bearing bones and joints;
4. spinal cord and major peripheral nerves;
5. bilateral pleural spaces;
6. heart and great-vessel lesions;
7. additional organ detail;
8. cosmetic micro-anatomy.

### 4.6 Feature-flag migration

Major replacements should be introduced behind a model-version or feature flag until the new path is stable.

Recommended enum:

```csharp
public enum DamageModelVersion
{
    LegacyVoxel = 0,
    LesionModelV1 = 1
}
```

Do not maintain two production models indefinitely. The flag exists to support migration, comparison, and rollback.

### 4.7 No unexplained constants

Every non-trivial constant must have one of:

- a source citation in code or documentation;
- a calibration record;
- an explicit statement that it is a gameplay tuning parameter;
- a documented provisional status and removal issue.

Never alter physiological constants solely to make a failing test pass without documenting why the expected behavior changed.

---

## 5. Recommended target architecture

The target architecture should remain decoupled and suitable for deterministic simulation.

### 5.1 Suggested folder structure

This is a target, not an instruction to perform a single large folder-move PR.

```text
TacticalSim.Core/
  Damage/
    Ballistics/
      ProjectileInteractionService.cs
      ProjectileTerminalProfile.cs
      WoundTrack.cs
      WoundTrackSegment.cs
      EnergyLedger.cs
    Anatomy/
      AnatomicalStructure.cs
      StructureType.cs
      VesselStructure.cs
      BoneStructure.cs
      NerveStructure.cs
      CompartmentDefinition.cs
      AnatomyDefinition.cs
    Lesions/
      Lesion.cs
      VesselLesion.cs
      OrganLesion.cs
      FractureLesion.cs
      NerveLesion.cs
      PleuralLesion.cs
      LesionCollection.cs
    Hemorrhage/
      BleedingSource.cs
      BloodCompartment.cs
      HemorrhageResolver.cs
      HemostasisState.cs
    Physiology/
      CardiovascularState.cs
      RespiratoryState.cs
      NeurologicalState.cs
      CoagulationState.cs
      CasualtyPhysiology.cs
    Treatment/
      TreatmentAction.cs
      TreatmentApplication.cs
      TreatmentResult.cs
      TreatmentEquipment.cs
    Capabilities/
      ActorCapabilityState.cs
      CapabilityResolver.cs
    Debug/
      DamageDebugSnapshot.cs
      DamageTelemetryEvent.cs
      DamageModelTrace.cs
```

### 5.2 Main data flow

```text
Projectile fired
  -> flight and collision
  -> wound track
  -> structure intersections
  -> persistent lesions
  -> bleeding and compartment effects
  -> physiological progression
  -> actor capability state
  -> tactical action availability and performance
  -> debug and gameplay presentation
```

### 5.3 Tactical capability state

The damage model should publish a compact state that the rest of the game can consume without understanding anatomy.

Recommended first version:

```csharp
public sealed record ActorCapabilityState(
    float Consciousness,
    float Mobility,
    float SprintCapability,
    float CrawlCapability,
    float Balance,
    float WeaponStability,
    float ReloadCapability,
    float Manipulation,
    float Communication,
    float CognitiveSpeed,
    float SelfAidCapability,
    bool CanStand,
    bool CanFight,
    bool CanMove,
    bool CanFollowCommands,
    bool IsUnconscious,
    bool IsDead);
```

The tactical layer should consume this contract rather than reading individual organ or voxel state.

---

## 6. Performance model for an isometric tactics game

The current client performs extremely small projectile timesteps near the target. That can be useful for experimentation but is unlikely to scale to a tactical engagement with many actors.

Adopt an event-oriented performance model:

1. Resolve most projectile flight analytically or with coarse RK4 steps until near a collider.
2. Use swept-segment or ray-volume intersection to identify body entry and exit.
3. Traverse intersected anatomy cells in geometric order.
4. Resolve the complete wound track as a discrete event.
5. Advance physiology at a lower fixed frequency than rendering.
6. Render projectile visuals independently from the authoritative damage event.

Provisional performance targets, to be revised after profiling:

- 60 FPS rendering on the target desktop profile;
- at least 32 active actors without damage-model frame spikes;
- physiology updates at 10 Hz or lower where stable;
- deterministic projectile injury resolution below 2 ms at the 95th percentile for ordinary small-arms hits;
- no allocation-heavy loops across every body voxel on every frame;
- off-screen and incapacitated actors eligible for lower-frequency physiology updates where mathematically safe.

These targets are engineering goals, not fixed product requirements. Add benchmarks before optimizing.

---

# 7. Milestones and phases

Existing project milestones should be preserved. Create the following new milestones after the currently completed foundation work.

---

## Milestone M5 - Damage-model foundations

### Goal

Create one authoritative, dimensionally coherent, deterministic projectile-to-injury pipeline.

### Deliverables

- typed unit strategy;
- energy ledger;
- canonical wound-track contracts;
- core projectile interaction service;
- removal or deprecation of duplicate injury paths;
- seedable randomness contract;
- reference scenario harness;
- debug trace for every impact.

### Exit criteria

- identical inputs produce identical wound tracks;
- Godot and core tests use the same impact service;
- projectile energy is conserved within a documented tolerance;
- no full kinetic-energy value is deposited independently into multiple voxels;
- unit tests catch pressure, energy, volume, and rate conversion errors;
- legacy behavior remains available only behind a feature flag.

### Dependencies

None beyond the current codebase.

---

## Milestone M6 - Anatomical structures and persistent lesions

### Goal

Replace destroyed-voxel state as the primary medical ontology with explicit structures and lesions.

### Deliverables

- anatomical structure interfaces;
- lesion hierarchy;
- major-vessel map;
- weight-bearing bone segments;
- spinal cord and major nerve structures;
- structure-intersection generation from wound tracks;
- persistent lesion serialization;
- lesion debug inspector.

### Exit criteria

- a projectile can miss or hit a named major vessel independently of muscle damage;
- a vessel laceration differs from a complete transection;
- a fracture is represented as a persistent lesion rather than only destroyed bone volume;
- later physiology reads lesions instead of hard-coded organ-destruction coefficients;
- repeated hits accumulate on the same actor without rebuilding anatomy.

### Dependencies

M5.

---

## Milestone M7 - Hemorrhage, circulation, and tactical capability

### Goal

Create tactically meaningful bleeding and shock progression from explicit lesions.

### Deliverables

- bleeding-source model;
- external and internal blood compartments;
- pressure-dependent flow;
- simplified hemostasis and clot disruption;
- reduced-order cardiovascular state;
- oxygen-delivery state;
- stable unconsciousness and death transitions;
- capability resolver linking physiology to tactical actions.

### Exit criteria

- muscle injury, arterial injury, venous injury, and concealed internal bleeding produce different trajectories;
- all lost blood is assigned to a destination;
- treatment modifies the relevant bleeding source rather than globally changing actor state;
- falling perfusion changes action speed and availability before total collapse;
- death is latched and cannot be reversed by ordinary state updates;
- capability-state tests cover movement, firing, reloading, communication, and self-aid.

### Dependencies

M6.

---

## Milestone M8 - Thoracic injury model

### Goal

Represent the main thoracic mechanisms that create different tactical and treatment outcomes.

### Deliverables

- left and right pleural compartments;
- pleural gas and pressure state;
- hemothorax state;
- open chest-wound behavior;
- simple and tension pneumothorax;
- pulmonary injury;
- pericardial bleeding and tamponade;
- thoracic treatment effects;
- thoracic reference scenarios.

### Exit criteria

- simple pneumothorax does not automatically become immediate tension;
- left and right chest states are independent;
- needle decompression can help tension physiology but not massive hemothorax or tamponade;
- chest seals can be effective, partial, blocked, detached, or require reassessment;
- respiratory and circulatory effects reach the capability layer.

### Dependencies

M7.

---

## Milestone M9 - Timed interventions and resources

### Goal

Turn treatment into tactical actions with time, equipment, quality, interruption, and opportunity cost.

### Deliverables

- treatment-action contract;
- equipment inventory and consumption;
- provider requirements;
- treatment duration;
- application quality;
- interruption and resumption;
- partial and failed outcomes;
- reassessment events;
- debug quick-apply mode for rapid model testing.

### Exit criteria

- treatment does not occur instantaneously unless debug override is enabled;
- an actor cannot fire normally while performing a two-handed treatment;
- movement, suppression, incoming fire, loss of consciousness, or player cancellation can interrupt treatment;
- supplies are consumed and tracked;
- the same treatment can produce effective, partial, or ineffective control based on state and application quality;
- debug mode can still apply perfect treatment instantly for controlled experiments.

### Dependencies

M7. Some thoracic treatments depend on M8.

---

## Milestone M10 - Isometric tactical integration

### Goal

Make casualty state matter to movement, combat, rescue, AI behavior, scenario design, and player decisions.

### Deliverables

- action gating and action-cost modifiers;
- posture and locomotion effects;
- casualty drag and carry actions;
- rescue exposure and movement penalties;
- casualty AI behavior;
- teammate response behaviors;
- isometric casualty overlays;
- scenario scoring based on mission outcome and casualty state;
- optional ground-truth debug panel.

### Exit criteria

- an injured actor may remain dangerous, mobile, partially effective, collapsed, unconscious, or dead;
- injuries affect tactical action execution rather than only a medical report;
- rescuing a casualty creates time, exposure, speed, and manpower costs;
- AI decisions account for capability state and mission context;
- ordinary play can be completed without inspecting internal anatomy;
- debug overlays can inspect full internal state without changing simulation results.

### Dependencies

M7 and M9.

---

## Milestone M11 - Casualty variation and bounded uncertainty

### Goal

Avoid identical outcomes from identical-looking actors while retaining deterministic replay.

### Deliverables

- casualty profile;
- body-size-dependent blood volume;
- baseline cardiovascular variation;
- configurable stress response;
- bounded lesion and treatment uncertainty;
- ammunition terminal-behavior profiles;
- armor and clothing interaction hooks;
- batch scenario runner.

### Exit criteria

- the same injury across a seeded cohort produces a plausible distribution of trajectories;
- uncertainty is bounded, versioned, and reproducible;
- projectile construction can affect terminal behavior independently of nominal calibre;
- armor and clothing can alter entry conditions without placing those rules in the UI layer;
- scenario designers can choose deterministic fixed profiles or seeded populations.

### Dependencies

M5-M10.

---

## Milestone M12 - Validation, balancing, performance, and release hardening

### Goal

Make the model stable enough for sustained scenario development and tactical balancing.

### Deliverables

- parameter provenance registry;
- reference injury suite;
- calibration tooling;
- golden replay suite;
- performance benchmarks;
- regression dashboards;
- model versioning;
- save-game migration rules;
- developer documentation;
- gameplay balancing pass.

### Exit criteria

- reference scenarios remain within accepted qualitative and quantitative bands;
- no update silently changes legacy scenario outcomes without a recorded model-version change;
- all model parameters are sourced, calibrated, or explicitly marked as gameplay tuning values;
- batch simulation can compare model versions;
- projectile, physiology, and tactical updates meet performance budgets;
- the project can state clearly what the model does and does not claim to represent.

### Dependencies

All earlier milestones.

---

# 8. Initial issue backlog

Create the following issues in dependency order. The identifiers below are planning identifiers and may be retained in issue titles.

## M5 issues - Damage-model foundations

### DM-001 - Adopt a typed-unit strategy

**Type:** Architecture  
**Priority:** P0  
**Size:** M  
**Dependencies:** None

#### Scope

- Decide between lightweight domain wrappers and a maintained units library.
- Document canonical units for every model quantity.
- Add conversion helpers at serialization and UI boundaries.
- Convert the highest-risk calculations first: energy, pressure, volume, flow, distance, time, mass.

#### Acceptance criteria

- An ADR records the chosen strategy.
- MPa-to-Pa conversion cannot be omitted silently.
- Tests fail when incompatible quantities are combined.
- Public model contracts state their units.
- No large unrelated refactor is included.

---

### DM-002 - Add a projectile energy ledger

**Type:** Ballistics / Testing  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-001

#### Scope

Record, for every projectile interaction:

- incoming kinetic energy;
- outgoing kinetic energy;
- energy deposited into structures;
- energy assigned to deformation or fragmentation;
- numerical residual.

#### Acceptance criteria

- Every resolved impact produces an `EnergyLedger`.
- Conservation error remains within a documented tolerance.
- The ledger is available in debug telemetry.
- Tests cover passage, stop, ricochet, and multi-structure traversal.

---

### DM-003 - Introduce canonical wound-track contracts

**Type:** Architecture / Ballistics  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-001

#### Scope

Create immutable contracts for:

- entry point;
- exit point or retained projectile;
- ordered path segments;
- intersected structures;
- energy transfer per segment;
- projectile state changes;
- fragment tracks where enabled.

#### Acceptance criteria

- Wound tracks are serializable.
- Segment ordering is deterministic.
- Tracks can be displayed in debug mode without re-running physics.
- Contracts contain no Godot types.

---

### DM-004 - Create a single core projectile-interaction service

**Type:** Architecture / Ballistics  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-002, DM-003

#### Scope

- Move authoritative body interaction into `TacticalSim.Core`.
- Route core actions, tests, console tools, and Godot through the same service.
- Deprecate or remove the current direct full-energy `ApplyTrauma` path.

#### Acceptance criteria

- Equivalent inputs from Godot and tests produce equivalent wound tracks.
- No client code independently calculates tissue damage.
- A migration feature flag preserves legacy comparison during rollout.
- Existing scenarios remain runnable.

---

### DM-005 - Add deterministic random-source contracts

**Type:** Architecture  
**Priority:** P0  
**Size:** S  
**Dependencies:** None

#### Scope

- Replace ad hoc `Random` construction in simulation code.
- Introduce an injected seeded random source or deterministic stream provider.
- Record seeds in replays and debug snapshots.

#### Acceptance criteria

- Same scenario, seed, and actions produce the same outcome.
- Different subsystems use named streams or stable sequence partitioning.
- No gameplay-critical random source is created from actor hash codes alone.

---

### DM-006 - Build a reference impact scenario harness

**Type:** Testing / Tooling  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-003, DM-005

#### Scope

Create a non-visual harness that runs defined impacts and emits:

- projectile inputs;
- wound track;
- lesions;
- energy ledger;
- physiology timeline;
- capability timeline;
- deterministic hash.

#### Acceptance criteria

- Scenarios run from CLI and tests.
- Output is machine-readable JSON plus a concise text summary.
- Results can be compared across model versions.
- Harness execution does not require Godot.

---

## M6 issues - Structures and lesions

### DM-101 - Define anatomical structure contracts

**Type:** Architecture / Anatomy  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-003

#### Scope

Define spatially intersectable structures for:

- organs;
- vessels;
- bones;
- nerves;
- airway segments;
- pleural and pericardial boundaries;
- skin and fascial boundaries where needed.

#### Acceptance criteria

- Structures have stable IDs and types.
- Structures can be queried independently of rendering.
- The existing voxel grid may act as an index but is not the only structure identity.
- Structure definitions are versioned.

---

### DM-102 - Define persistent lesion hierarchy

**Type:** Architecture / Physiology  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-101

#### Scope

Create lesion types for:

- vessel laceration;
- vessel transection;
- parenchymal injury;
- fracture;
- nerve injury;
- airway disruption;
- pleural breach;
- cardiac injury;
- brain or spinal injury;
- open soft-tissue wound.

#### Acceptance criteria

- Lesions persist independently of projectile objects.
- Lesions can accumulate and be treated.
- Lesions contain severity, geometry, location, and treatment state.
- Physiology can enumerate lesions without scanning all voxels.

---

### DM-103 - Add a first-pass major-vessel map

**Type:** Anatomy / Physiology  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-101

#### Minimum vessel set

- aorta and vena cava;
- carotid and jugular vessels;
- subclavian, axillary, and brachial vessels;
- iliac, femoral, and popliteal vessels;
- major pulmonary or hilar vessels;
- major hepatic, splenic, and renal vascular pedicles.

#### Acceptance criteria

- Vessels are spatially distinct from surrounding muscle.
- A projectile can pass through a limb without automatically injuring a major vessel.
- Vessel calibre and pressure regime are represented.
- Unit tests cover near miss, partial laceration, and transection.

---

### DM-104 - Add clinically meaningful bone segments and fracture lesions

**Type:** Anatomy / Injury  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-101, DM-102

#### Scope

Prioritize:

- femur;
- tibia;
- pelvis;
- humerus;
- radius and ulna as a combined first pass;
- ribs and sternum;
- skull;
- cervical and thoracolumbar spine.

#### Acceptance criteria

- Fractures have location, displacement or stability class, and functional consequence.
- Weight-bearing fractures affect standing and movement.
- Bone injury no longer depends only on percentage of destroyed bone voxels.

---

### DM-105 - Add spinal cord and major peripheral nerve structures

**Type:** Anatomy / Neurology  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-101, DM-102

#### Acceptance criteria

- Spinal lesions are level-specific and laterality-aware where practical.
- Major limb nerve injury affects the relevant limb rather than all limbs.
- Neurological consequences are not based solely on global destroyed-brain fraction.

---

### DM-106 - Convert voxels into an implementation detail of spatial lookup

**Type:** Refactor / Performance  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-101, DM-102, DM-103

#### Scope

Retain useful voxel or grid indexing while removing direct assumptions that:

- every destroyed voxel is a complete lesion;
- every organ voxel contributes a fixed bleed coefficient;
- voxel destruction percentage directly equals organ function.

#### Acceptance criteria

- Structure and lesion APIs are authoritative.
- Voxel data may still support collision, visualization, or local tissue properties.
- Existing debug rendering continues to work or has a migration plan.

---

### DM-107 - Add lesion serialization and debug inspection

**Type:** Debug / Tooling  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-102

#### Acceptance criteria

- Lesions can be saved in replay snapshots.
- Debug UI lists structure, lesion type, severity, bleeding source, treatment state, and origin impact.
- The inspector does not mutate simulation state unless explicitly in a debug-edit mode.

---

## M7 issues - Hemorrhage, circulation, and capability

### DM-201 - Implement bleeding sources from vessel and tissue lesions

**Type:** Physiology  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-102, DM-103

#### Scope

Each bleeding lesion should define:

- source pressure regime;
- effective wound aperture;
- partial or complete transection;
- destination compartment;
- compressibility;
- current control state;
- clot or retraction state.

#### Acceptance criteria

- Bleeding does not derive primarily from destroyed cubic centimetres.
- Arterial, venous, and parenchymal sources differ.
- Flow changes with systemic and local pressure.
- Sources can be independently controlled or remain uncontrolled.

---

### DM-202 - Add blood destinations and compartment conservation

**Type:** Physiology  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-201

#### Minimum compartments

- external;
- local soft tissue;
- left pleural;
- right pleural;
- pericardial;
- peritoneal;
- retroperitoneal;
- airway.

#### Acceptance criteria

- Every lost millilitre is assigned to exactly one destination.
- Internal accumulation can produce compartment-specific effects.
- Debug telemetry reconciles circulating and lost blood.

---

### DM-203 - Add simplified hemostasis, compression, and rebleeding

**Type:** Physiology  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-201

#### Scope

Implement a deliberately reduced model for:

- vessel retraction;
- pressure reduction;
- clot formation;
- local compression;
- clot disruption by movement or renewed pressure;
- treatment-assisted control.

#### Acceptance criteria

- Some bleeding sources can slow without perfect treatment.
- Major vessel bleeding does not unrealistically self-resolve.
- Movement or failed treatment can restart bleeding where configured.
- All randomness is seeded.

---

### DM-204 - Replace hemorrhage classes with a reduced cardiovascular model

**Type:** Physiology  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-201, DM-202

#### Minimum state

- circulating blood volume;
- heart rate;
- stroke-volume or cardiac-output proxy;
- systemic vascular resistance proxy;
- mean arterial pressure;
- perfusion effectiveness.

#### Acceptance criteria

- Compensation and decompensation emerge from state, not only fixed blood-loss bands.
- Cardiac injury and blood loss can affect output through different mechanisms.
- Large timesteps and small timesteps remain acceptably close.
- State remains bounded and numerically stable.

---

### DM-205 - Separate oxygen saturation from oxygen delivery

**Type:** Physiology  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-204

#### Minimum state

- ventilation effectiveness;
- arterial oxygen saturation proxy;
- haemoglobin or red-cell-mass proxy;
- cardiac output;
- systemic oxygen-delivery index;
- cerebral oxygen-delivery index.

#### Acceptance criteria

- normal saturation with poor circulation can still produce cerebral failure.
- blood loss reduces oxygen-carrying capacity independently of saturation.
- respiratory and circulatory failures are distinguishable in debug telemetry.

---

### DM-206 - Define explicit incapacitation, unconsciousness, and death states

**Type:** Physiology / Gameplay  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-204, DM-205

#### Acceptance criteria

- `IsDead` is irreversible under ordinary simulation.
- unconsciousness may be reversible where the model permits.
- incapacity is distinct from unconsciousness.
- the turn resolver cancels or pauses actions according to explicit state rules.
- terminal state no longer depends on a single fragile conjunction of variables.

---

### DM-207 - Add physiology-to-capability resolver

**Type:** Gameplay / Architecture  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-104, DM-105, DM-204, DM-205, DM-206

#### Acceptance criteria

- movement, posture, aiming, firing, reloading, communication, and self-aid use capability state.
- capability penalties are traceable to injuries and physiology.
- the tactical layer does not inspect voxels or organ percentages directly.
- capability outputs are deterministic and unit tested.

---

## M8 issues - Thoracic injury

### DM-301 - Add bilateral pleural compartments

**Type:** Physiology / Anatomy  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-202

#### Acceptance criteria

- left and right pleural spaces track gas, blood, and pressure independently.
- lesions identify the affected side.
- lung compression and respiratory effect derive from compartment state.

---

### DM-302 - Implement simple, open, and tension pneumothorax

**Type:** Physiology  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-301

#### Acceptance criteria

- a lung injury does not automatically imply immediate tension physiology.
- open chest wounds exchange gas with the environment according to a simplified conductance model.
- tension state produces both respiratory and circulatory effects.
- progression rates are configurable and testable.

---

### DM-303 - Implement hemothorax and pericardial tamponade

**Type:** Physiology  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-202, DM-301

#### Acceptance criteria

- intrathoracic blood is conserved.
- hemothorax differs from pneumothorax.
- pericardial accumulation can reduce cardiac output independently of lung state.
- decompression does not cure unrelated mechanisms.

---

### DM-304 - Implement thoracic treatment interactions

**Type:** Treatment / Physiology  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-302, DM-303, DM-401

#### Acceptance criteria

- chest seal state affects an open chest lesion.
- seals may be vented, non-vented, blocked, detached, or partial.
- needle decompression targets a side and site and may be partial or ineffective.
- recurrence is possible.
- debug quick-treatment remains available.

---

### DM-305 - Add thoracic reference scenarios

**Type:** Testing  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-302, DM-303, DM-304

#### Required scenarios

- unilateral simple pneumothorax;
- open chest wound;
- tension pneumothorax;
- massive hemothorax;
- pulmonary injury without tension;
- cardiac bleeding with tamponade;
- incorrect intervention;
- successful and failed decompression.

---

## M9 issues - Timed interventions and resources

### DM-401 - Define treatment-action contracts

**Type:** Architecture / Simulation  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-102, DM-207

#### Minimum fields

- provider;
- target actor;
- target lesion or body region;
- duration;
- required equipment;
- required hands or posture;
- progress;
- interruption policy;
- application quality;
- result;
- reassessment requirement.

#### Acceptance criteria

- treatment actions run through the turn resolver.
- treatment can be queued, cancelled, failed, completed, or interrupted.
- physiology continues to progress during treatment.

---

### DM-402 - Implement tourniquet application quality

**Type:** Treatment  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-201, DM-401

#### Acceptance criteria

- tourniquets target a limb and placement zone.
- application consumes time and equipment.
- control may be complete, partial, or ineffective.
- a second device may improve control where configured.
- treatment affects distal perfusion and later capability.
- debug mode may apply a perfect tourniquet instantly.

---

### DM-403 - Implement direct pressure and wound packing

**Type:** Treatment  
**Priority:** P0  
**Size:** M  
**Dependencies:** DM-201, DM-401

#### Acceptance criteria

- treatment applies only to accessible and compressible wounds.
- packing is not limited to a hard-coded abdomen body part.
- sustained pressure occupies the provider and can be interrupted.
- treatment does not control non-compressible internal bleeding.

---

### DM-404 - Implement treatment equipment inventory

**Type:** Gameplay / Simulation  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-401

#### Acceptance criteria

- actors and teams have finite treatment items.
- items are consumed or remain attached as appropriate.
- scenario designers can configure loadouts.
- debug mode can ignore inventory when explicitly enabled.

---

### DM-405 - Add treatment interruption and reassessment

**Type:** Simulation / Gameplay  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-401

#### Acceptance criteria

- movement, incoming suppression effects, incapacitation, or explicit cancellation can interrupt treatment.
- partially completed actions have documented behavior.
- completed interventions can schedule a reassessment event.
- repeated assessment can identify deterioration or failed control in debug telemetry.

---

### DM-406 - Add a developer quick-treatment console

**Type:** Debug / Tooling  
**Priority:** P1  
**Size:** S  
**Dependencies:** DM-401

#### Purpose

Preserve rapid iteration despite realistic timed actions.

#### Acceptance criteria

- developers can apply, remove, fail, or partially apply treatment through a debug interface.
- the operation is marked in the model trace.
- debug commands are unavailable in production builds unless explicitly enabled.

---

## M10 issues - Tactical integration

### DM-501 - Connect capability state to tactical-action costs

**Type:** Gameplay  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-207

#### Acceptance criteria

- movement speed, aim time, fire stability, reload time, and command latency respond to capability state.
- modifiers are centralized and testable.
- no action class reads organ damage directly.
- severe impairment can block actions rather than only make them slower.

---

### DM-502 - Add casualty drag and carry actions

**Type:** Gameplay / Simulation  
**Priority:** P0  
**Size:** L  
**Dependencies:** DM-401, DM-501

#### Acceptance criteria

- dragging and carrying require proximity and capability.
- rescuer movement speed and weapon use are affected.
- casualty orientation and movement may affect bleeding or treatment state where configured.
- actions are compatible with the isometric navigation model.

---

### DM-503 - Add casualty behavior states for AI

**Type:** AI / Gameplay  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-207, DM-501

#### Candidate states

- fighting effectively;
- fighting impaired;
- seeking cover;
- self-aiding;
- crawling to safety;
- calling for help;
- disoriented;
- unconscious;
- dead.

#### Acceptance criteria

- AI behavior depends on mission context and capability state.
- behavior transitions are deterministic for fixed seeds and inputs.
- AI does not gain access to hidden state beyond the configured debug or design policy.

---

### DM-504 - Add tactical rescue exposure and opportunity cost

**Type:** Gameplay / Scenario Systems  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-502

#### Acceptance criteria

- rescue actions consume actor time and position actors in the world.
- scenario scoring can account for mission delay, exposure, and casualty outcome.
- treatment is never a free pause in combat.

---

### DM-505 - Create isometric casualty-status overlays

**Type:** UI / Debug  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-207

#### Ordinary-play overlay candidates

- effective;
- impaired;
- critical;
- unconscious;
- dead;
- being treated;
- bleeding controlled or uncontrolled where the design chooses to reveal it.

#### Debug overlay candidates

- exact lesion list;
- blood-source map;
- compartment volumes;
- wound track;
- capability breakdown;
- model trace.

#### Acceptance criteria

- ordinary overlays are readable at isometric scale.
- debug data can be toggled without affecting state.
- UI does not reimplement capability logic.

---

### DM-506 - Add casualty-aware scenario objectives and scoring

**Type:** Gameplay / Content Tools  
**Priority:** P2  
**Size:** M  
**Dependencies:** DM-502, DM-504

#### Acceptance criteria

- scenarios can score mission completion, friendly survival, enemy neutralization, evacuation, delay, and resource expenditure separately.
- designers can choose whether casualty rescue is mandatory, optional, or irrelevant.
- scoring does not imply that medical intervention always outranks the tactical mission.

---

## M11 issues - Variation and uncertainty

### DM-601 - Add configurable casualty profiles

**Type:** Physiology / Content  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-204

#### Minimum fields

- body mass;
- blood volume;
- baseline heart rate and pressure;
- haemoglobin or oxygen-carrying proxy;
- stress-response profile;
- selected comorbidity modifiers where tactically relevant.

#### Acceptance criteria

- the default profile reproduces current expected baseline behavior within a documented range.
- profiles are serializable and scenario-configurable.

---

### DM-602 - Add bounded, seeded physiological uncertainty

**Type:** Physiology / Testing  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-005, DM-601

#### Acceptance criteria

- uncertainty distributions are bounded and documented.
- outcomes remain reproducible by seed.
- no random result bypasses the causal model.
- fixed deterministic mode remains available for debugging.

---

### DM-603 - Add terminal projectile behavior profiles

**Type:** Ballistics  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-004

#### Candidate profile properties

- mass;
- diameter;
- construction;
- yaw tendency;
- deformation threshold;
- expansion curve;
- fragmentation behavior;
- retained-mass behavior;
- empirical penetration or energy-deposition profile.

#### Acceptance criteria

- two projectiles with similar muzzle energy can produce different wound tracks.
- profiles are data-driven.
- defaults remain conservative until calibrated.

---

### DM-604 - Add armor and clothing interaction hooks

**Type:** Ballistics / Gameplay  
**Priority:** P2  
**Size:** L  
**Dependencies:** DM-603

#### Acceptance criteria

- armor and clothing modify projectile state before body entry.
- blunt effects can exist without penetration.
- rules live in core and are inspectable in debug telemetry.
- no detailed armor simulation is required in the first version.

---

### DM-605 - Build a batch cohort runner

**Type:** Tooling / Validation  
**Priority:** P1  
**Size:** M  
**Dependencies:** DM-006, DM-602

#### Acceptance criteria

- run hundreds or thousands of seeded variants without Godot.
- export outcome distributions and model-version comparisons.
- include performance timing and failure diagnostics.

---

## M12 issues - Validation and hardening

### DM-701 - Create a parameter provenance registry

**Type:** Documentation / Validation  
**Priority:** P0  
**Size:** M  
**Dependencies:** Ongoing

#### Acceptance criteria

Every model parameter is marked as one of:

- externally sourced;
- empirically calibrated;
- inferred;
- provisional;
- gameplay tuning.

The registry records source, version, owner, and affected tests.

---

### DM-702 - Create a reference injury suite

**Type:** Validation / Testing  
**Priority:** P0  
**Size:** L  
**Dependencies:** M7-M9

#### Required reference cases

- soft-tissue limb wound without major-vessel injury;
- major arterial injury;
- major venous injury;
- junctional bleeding;
- concealed abdominal bleeding;
- stable and unstable fracture;
- spinal injury;
- simple pneumothorax;
- tension pneumothorax;
- hemothorax;
- cardiac injury;
- multiple-hit cumulative trauma;
- effective, partial, failed, and interrupted treatment.

#### Acceptance criteria

- each case has expected qualitative ordering and broad time bands.
- tests distinguish software invariants from validation expectations.
- changes outside accepted bands require a model-change note.

---

### DM-703 - Add calibration and sensitivity tooling

**Type:** Tooling / Validation  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-605, DM-701, DM-702

#### Acceptance criteria

- identify which parameters most influence key outcomes.
- compare candidate parameter sets.
- prevent hidden overfitting to one scenario.
- export repeatable reports.

---

### DM-704 - Add damage-model performance benchmarks

**Type:** Performance  
**Priority:** P0  
**Size:** M  
**Dependencies:** M5-M10

#### Benchmarks

- anatomy construction;
- projectile-to-wound resolution;
- lesion update;
- physiology tick for 1, 16, 32, and 64 actors;
- treatment updates;
- debug telemetry overhead;
- save and replay serialization.

#### Acceptance criteria

- benchmarks run in CI or a documented performance pipeline.
- regressions over the agreed threshold are reported.
- optimization PRs include before-and-after measurements.

---

### DM-705 - Add model-versioned save and replay support

**Type:** Architecture / Persistence  
**Priority:** P1  
**Size:** L  
**Dependencies:** DM-003, DM-102, DM-107

#### Acceptance criteria

- save files identify model and anatomy versions.
- replay files include deterministic seeds and actions.
- incompatible changes fail clearly or migrate explicitly.
- no silent reinterpretation of old lesion state.

---

### DM-706 - Publish damage-model developer documentation

**Type:** Documentation  
**Priority:** P0  
**Size:** M  
**Dependencies:** All major architecture work

#### Required documents

- architecture overview;
- data-flow diagram;
- units and conventions;
- parameter provenance;
- adding anatomy;
- adding a lesion type;
- adding treatment;
- adding reference scenarios;
- debug workflow;
- performance guidance;
- known limitations and non-claims.

---

# 9. Reference tactical scenarios

Use these scenarios to guide content and integrated testing. They are not medical-training exercises. Each should force a tactical decision.

## Scenario A - Mobile but degrading teammate

- Friendly actor sustains a significant but initially non-disabling injury.
- Actor can still move and fire, but performance degrades over time.
- Player must decide whether to continue the assault, withdraw, provide aid, or reassign the actor.

**Model purpose:** Demonstrate gradual capability loss rather than binary alive/dead behavior.

## Scenario B - Major limb bleed under fire

- Casualty is in an exposed position.
- Bleeding is rapidly progressive.
- A responder must choose between suppression, movement to cover, casualty drag, and treatment.

**Model purpose:** Treatment must have tactical time and exposure cost.

## Scenario C - Concealed internal injury

- Casualty appears less dramatic than an externally bleeding casualty.
- Performance and consciousness deteriorate despite little visible blood.
- External treatment cannot fully solve the problem.

**Model purpose:** Different injuries with similar initial combat presentation should diverge over time.

## Scenario D - Thoracic deterioration

- Casualty remains conscious and useful initially.
- Breathing and circulation degrade over minutes.
- Correct and incorrect interventions have distinct results.

**Model purpose:** Avoid immediate scripted death while preserving urgency.

## Scenario E - Multiple casualties and mission pressure

- Two or more casualties have different urgency and tactical value.
- The team has limited treatment supplies and manpower.
- Mission objectives continue to evolve.

**Model purpose:** Evaluate prioritization, resource allocation, and rescue opportunity cost.

## Scenario F - Wounded hostile remains dangerous

- Hostile actor is injured but not immediately incapacitated.
- Accuracy, movement, and decision speed change.
- Hostile may surrender, withdraw, continue fighting, or collapse depending on state and AI policy.

**Model purpose:** Prevent unrealistic assumptions that every hit immediately neutralizes a threat.

---

# 10. Debugging and telemetry requirements

Omniscient debug information is explicitly permitted and encouraged during development.

Every impact should be able to emit a trace resembling:

```text
Impact ID
Projectile profile and model version
Shooter and target IDs
Entry and exit points
Ordered structures intersected
Energy before and after each segment
Generated lesions
Bleeding sources
Blood destinations
Physiological state before and after
Capability state before and after
Treatments active
Random seed and stream IDs
Numerical warnings
```

Recommended debug views:

1. **Wound-track view** - entry, path, exit, fragments, and structures hit.
2. **Lesion view** - persistent injury list with severity and treatment status.
3. **Hemorrhage view** - active bleeding sources and compartment totals.
4. **Physiology timeline** - blood volume, pressure, oxygen delivery, consciousness.
5. **Capability timeline** - mobility, firing, reload, communication, self-aid.
6. **Energy ledger** - conservation and residual errors.
7. **Model comparison** - legacy versus new damage model on the same shot.

Debug tooling must not become the production gameplay API. The UI should read a stable snapshot contract rather than reaching into internal mutable objects.

---

# 11. Testing strategy

## 11.1 Test categories

### A. Mathematical and unit tests

- unit conversion;
- energy conservation;
- blood conservation;
- bounded state;
- deterministic random streams;
- timestep convergence;
- serialization round trips.

### B. Component tests

- projectile traversal;
- vessel intersection;
- lesion generation;
- bleeding flow;
- compartment accumulation;
- cardiovascular progression;
- respiratory progression;
- treatment application;
- capability mapping.

### C. Integrated reference scenarios

- predefined impacts;
- timed progression;
- treatment sequences;
- tactical action interactions;
- multi-actor simulations.

### D. Golden replay tests

- fixed scenario;
- fixed seed;
- fixed action sequence;
- stable event hash and accepted output bands.

Golden tests should not freeze every floating-point output forever. Use exact checks for invariants and broad accepted bands for model outputs that may legitimately evolve.

## 11.2 Definition of a good test

A good test states why the behavior matters.

Prefer:

```text
A femoral artery transection should produce materially faster blood loss than an equivalent muscle-only wound, and a successful proximal tourniquet should reduce that source substantially.
```

Avoid tests that merely restate an arbitrary formula:

```text
Twenty percent destroyed lung must equal exactly X tension points after ten seconds.
```

## 11.3 Do not confuse verification and validation

- **Verification:** The code implements its equations correctly.
- **Validation:** The resulting behavior is plausible for the intended tactical simulation.

Both are required, but they are not the same.

---

# 12. Repository and project-board setup

## 12.1 Recommended labels

### Type labels

- `type:architecture`
- `type:ballistics`
- `type:anatomy`
- `type:physiology`
- `type:treatment`
- `type:gameplay`
- `type:ai`
- `type:ui`
- `type:testing`
- `type:validation`
- `type:performance`
- `type:documentation`
- `type:tooling`

### Priority labels

- `priority:P0`
- `priority:P1`
- `priority:P2`
- `priority:P3`

### Risk labels

- `risk:high`
- `risk:medium`
- `risk:low`
- `breaking-change`
- `model-change`
- `save-compatibility`

### Workflow labels

- `needs-design`
- `needs-data`
- `needs-tests`
- `blocked`
- `ready-for-agent`
- `human-review-required`
- `debug-tooling`
- `legacy-removal`

## 12.2 Recommended project-board columns

1. Backlog
2. Needs design
3. Ready
4. In progress
5. Review
6. Validation
7. Blocked
8. Done

## 12.3 Issue sizing

- **S:** One focused change, normally one to two implementation files plus tests.
- **M:** Several related files, one clear subsystem boundary, limited design risk.
- **L:** Cross-cutting change requiring an ADR, migration plan, or multiple staged PRs.

Large issues should be split before assignment to a junior developer or autonomous coding agent.

---

# 13. Copy-paste GitHub issue template

```markdown
## Summary

Describe the user-visible or model-level outcome in one paragraph.

## Why this matters

Explain the tactical, architectural, performance, or validation problem this issue solves.

## Scope

- In scope item 1
- In scope item 2
- In scope item 3

## Out of scope

- Explicit exclusion 1
- Explicit exclusion 2

## Dependencies

- Depends on #ISSUE
- Blocks #ISSUE

## Suggested implementation area

- `TacticalSim.Core/...`
- `TacticalSim.Tests/...`
- `TacticalSim.GodotClient/...`

## Acceptance criteria

- [ ] Criterion stated as observable behavior
- [ ] Deterministic tests added
- [ ] Existing relevant tests updated
- [ ] Debug telemetry updated where applicable
- [ ] Build passes with no warnings
- [ ] Full test suite passes
- [ ] Documentation or ADR updated

## Required test cases

1. Normal case
2. Boundary case
3. Failure or interruption case
4. Deterministic replay case
5. Regression case

## Model and data notes

State whether each new parameter is sourced, calibrated, provisional, or a gameplay tuning value.

## Performance notes

State expected complexity and whether a benchmark is required.

## Migration notes

State whether legacy behavior, saves, replays, or public interfaces are affected.

## Definition of done

The issue is complete only when implementation, tests, telemetry, and documentation agree on the new behavior.
```

---

# 14. Pull-request requirements

Every damage-model pull request must include:

1. linked issue;
2. concise problem statement;
3. affected model layer;
4. tests added or changed;
5. deterministic seed where stochastic behavior is involved;
6. before-and-after behavior for reference scenarios;
7. parameter provenance;
8. performance effect where relevant;
9. migration or compatibility notes;
10. debug telemetry update where applicable.

For model-changing PRs, include this checklist:

```markdown
- [ ] This PR changes model behavior.
- [ ] The behavior change is intentional and documented.
- [ ] Reference scenarios were run.
- [ ] Results were compared with the previous model version.
- [ ] New constants are sourced, calibrated, provisional, or marked as gameplay tuning.
- [ ] Deterministic replay remains intact.
- [ ] Godot does not duplicate the new core logic.
```

---

# 15. Instructions for junior developers

1. Read the issue, its dependencies, and the relevant architecture document before coding.
2. Identify the authoritative model layer. Do not place physiology in UI code or rendering code.
3. Add or update tests before changing constants.
4. Keep the PR focused on one issue.
5. Do not refactor unrelated files for style.
6. Do not delete legacy behavior until the migration issue says to do so.
7. Use deterministic test data and explicit seeds.
8. Add debug telemetry when adding hidden state.
9. Document units in every new public contract.
10. Ask for review when an issue changes a public interface, save format, or model behavior outside the acceptance criteria.

A junior developer should not be assigned a broad instruction such as "make bleeding realistic." Assign a bounded issue such as "implement external blood destination conservation for vessel lesions."

---

# 16. Instructions for agentic coding systems

Agentic coders must operate under the following constraints.

## 16.1 Required workflow

1. Inspect the issue and dependency chain.
2. Read the current implementation and relevant tests.
3. State the implementation plan in the task log.
4. Make the smallest architecture-consistent change.
5. Add tests that fail before the change and pass after it where practical.
6. Run the targeted tests.
7. Run the full build and test suite.
8. Update documentation and debug telemetry.
9. Report changed files, behavior changes, test results, and remaining risks.

## 16.2 Prohibited behavior

An agent must not:

- create a second competing damage pipeline;
- place authoritative physiology in Godot UI code;
- introduce unseeded simulation randomness;
- change constants only to satisfy an expected test value;
- silently reinterpret units;
- remove debug telemetry without replacement;
- claim medical validation from unit-test success;
- perform a repository-wide rename or folder move inside a narrow issue;
- delete legacy behavior before migration acceptance criteria are met;
- alter save formats without versioning.

## 16.3 Agent completion report

Every agent completion report should include:

```text
Issue implemented:
Files changed:
Architecture decisions:
Behavior before:
Behavior after:
Tests added:
Tests run:
Build result:
Performance impact:
Model parameters added or changed:
Known limitations:
Follow-up issues:
```

---

# 17. Non-goals and deferred work

The following are explicitly deferred unless separately approved:

- a full finite-element tissue solver;
- histological anatomy;
- exact clinical vital-sign prediction;
- accredited medical training content;
- detailed drug pharmacology;
- surgery or hospital care;
- exact wound-care procedure teaching;
- photorealistic gore;
- fully hidden medical information during the present development phase;
- modelling every vessel or organ before the major tactical differentiators work;
- real-time microsecond projectile stepping for every bullet in every scene;
- networking optimization before deterministic single-machine replay is stable.

---

# 18. Recommended immediate sequence

The first implementation tranche should consist of these issues only:

1. DM-001 - Adopt typed units.
2. DM-005 - Add deterministic random-source contracts.
3. DM-002 - Add the projectile energy ledger.
4. DM-003 - Introduce wound-track contracts.
5. DM-006 - Build the reference impact harness.
6. DM-004 - Unify the projectile-interaction pipeline.
7. DM-101 - Define anatomical structures.
8. DM-102 - Define persistent lesions.
9. DM-103 - Add the first-pass major-vessel map.
10. DM-201 - Generate bleeding sources from lesions.

Do not start thoracic detail, treatment timing, or broad AI integration before the single injury pipeline and lesion model exist.

---

# 19. First playable target

The first new-model playable target should be intentionally narrow.

## Vertical slice

- One small isometric combat map.
- Four friendly actors and four hostile actors.
- Deterministic rifle and pistol profiles.
- Head, chest, abdomen, arm, and leg impact paths.
- Major limb vessels.
- External and concealed bleeding.
- Mobility and weapon-handling consequences.
- Timed tourniquet and packing actions.
- Casualty drag to cover.
- Full omniscient debug panel.
- Scenario replay and event trace.

## Success criteria

The vertical slice succeeds when:

- a hit does not imply immediate neutralization;
- injury location and structure intersection change tactical outcome;
- a casualty can remain combat-capable while degrading;
- a rescuer must trade firepower and exposure for assistance;
- treatment changes some trajectories but not all;
- outcomes are reproducible;
- developers can explain every major state transition using the model trace;
- the simulation remains performant with all actors active.

---

# 20. Final implementation rule

When choosing between additional medical detail and clearer tactical consequence, prefer the change that improves tactical consequence while preserving causal coherence.

The damage model is successful when it produces varied, understandable, reproducible battlefield consequences that interact meaningfully with movement, fire, cover, time, rescue, and mission objectives. It is not necessary for it to teach medicine. It is necessary for it to make injury matter tactically.
