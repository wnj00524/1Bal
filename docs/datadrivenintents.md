# Proxy State — Data-Driven Intents Work Plan

## Objective

Make agent intents entirely data-defined while keeping runtime execution fast, deterministic, inspectable, and strongly validated.

The target architectural boundary is:

> **Code defines mechanics. Data composes mechanics into intents.**

A new intent such as `eat`, `shop`, `visit_friend`, `sleep`, `investigate`, or `go_to_pub` should normally require **data changes only**.

A new physical or simulation mechanic may still require C#.

The runtime must understand the grammar of decisions, but must not contain hard-coded knowledge of specific intent IDs such as `work`, `rest`, or `socialize`.

---

# 1. Architectural Principles

## 1.1 Core rule

No simulation runtime code may branch on a specific intent ID.

Forbidden patterns include:

```csharp
if (action.Id == "work") ...
if (action.Id == "rest") ...
if (action.Id == "socialize") ...

switch (intentHash)
{
    ...
}
```

Allowed runtime branching is on generic mechanics:

```csharp
switch (executor.Kind)
{
    case ExecutorKind.PerformAtLocation:
    case ExecutorKind.PerformWithEntity:
    case ExecutorKind.Wait:
}
```

## 1.2 Intended pipeline

```text
World / ECS state
        ↓
Fact extraction
        ↓
Compiled intent definitions
        ↓
Eligibility evaluation
        ↓
Target enumeration
        ↓
Utility scoring
        ↓
Arbitration / hysteresis / cooldown
        ↓
IntentionState
        ↓
Generic executor primitives
        ↓
Activity + movement + effects
```

## 1.3 Performance rule

Authoring data may be expressive and human-readable.

Runtime evaluation must operate on compiled structures:

- integer handles;
- dense indexes;
- enums/opcodes;
- direct array indexes;
- pre-resolved trait masks;
- pre-resolved attribute indexes;
- no JSON parsing;
- no string comparisons;
- no `ToLowerInvariant()` in hot paths;
- no per-decision heap allocations where practical.

## 1.4 Scope boundary

This project should **not** introduce a general-purpose scripting language.

Use a deliberately small declarative vocabulary for:

- facts;
- predicates;
- numeric expressions;
- target selectors;
- executors;
- effects.

---

# 2. Definition of Done

The work is complete when all of the following are true:

- Adding a new intent does not require changes to `AgentDecisionSystem`.
- Adding a new intent does not require changes to `CommutingSystem`.
- Runtime systems do not inspect `work`, `rest`, `socialize`, or any other domain intent IDs.
- Eligibility rules are data-defined.
- Utility source expressions are data-defined.
- Target selection is data-defined.
- Destination selection is data-defined.
- Public activity identity is data-defined.
- Execution uses generic mechanic primitives.
- Effect application remains generic.
- Content validation is structural rather than domain-specific.
- Intents are compiled to a compact runtime representation at startup.
- Existing work/rest/socialize behaviour remains functionally equivalent.
- Decision evaluation remains deterministic under a fixed world state and seed.
- Tests prove that a new intent can be added entirely through data.

---

# 3. Milestone Overview

| Milestone | Goal | Outcome |
|---|---|---|
| M6 | Baseline and behavioural lock | Existing behaviour is measured and protected |
| M7 | Fact and numeric expression IR | Remove hard-coded utility source semantics |
| M8 | Predicate IR | Remove hard-coded eligibility gate semantics |
| M9 | Data-defined targeting | Remove target and destination branching |
| M10 | Generic execution | Remove intent-specific behaviour from commuting/activity code |
| M11 | Data-defined activity identity | Remove domain activity enum coupling |
| M12 | Intent compiler and validation | Compile authoring data into efficient runtime IR |
| M13 | Dependency-driven reevaluation | Scale decision evaluation through dirty dependencies |
| M14 | Candidate indexing | Avoid evaluating irrelevant intents |
| M15 | Tooling, diagnostics, and content tests | Make the system maintainable for future content expansion |

---

# 4. Milestone M6 — Baseline and Behavioural Lock

## Goal

Establish a safe refactoring baseline before changing decision semantics.

## Instructions

1. Capture the current behaviour of:
   - `work`;
   - `rest`;
   - `socialize`.
2. Add deterministic simulation tests with fixed seeds.
3. Record:
   - winning intent;
   - target entity;
   - target location;
   - selected utility;
   - travel behaviour;
   - activity state;
   - cooldown behaviour;
   - switching behaviour.
4. Add a performance baseline for 1,000 agents over a representative simulation interval.
5. Add an architectural test or source scan that identifies runtime comparisons against known intent IDs.

## Issues

### Issue M6.1 — Add deterministic decision fixtures

**Description**

Create deterministic tests for representative agent/world states.

**Acceptance criteria**

- Tests cover work eligibility inside and outside schedule.
- Tests cover rest winning under high fatigue.
- Tests cover socialize with and without an available peer.
- Tests cover minimum commitment.
- Tests cover switching threshold.
- Tests cover urgent preemption.
- Tests cover cooldown behaviour.

---

### Issue M6.2 — Add decision trace test helper

**Description**

Create a reusable test-only representation of a decision result.

Suggested shape:

```csharp
public sealed record DecisionTrace(
    int IntentHash,
    int TargetEntityId,
    int TargetLocationId,
    float Utility,
    long SelectedAtMinute);
```

**Acceptance criteria**

- Tests can compare decision outputs without depending on UI state.
- Test helper does not alter production hot paths.

---

### Issue M6.3 — Record decision performance baseline

**Description**

Measure current decision-system runtime for 1,000 agents.

**Acceptance criteria**

- Baseline recorded in repository documentation.
- Measurement includes allocations if practical.
- Later milestones can be compared against the baseline.

---

# 5. Milestone M7 — Fact and Numeric Expression IR

## Goal

Remove semantic utility-source switches such as:

- `schedulePressure`;
- `lowWealth`;
- `timeOfDay`;
- `peerAffinity`.

## Instructions

Introduce a finite typed fact system.

Suggested conceptual facts:

```text
agent.attribute.*
agent.trait.*
agent.location.current
agent.location.home
agent.location.work

time.minuteOfDay
time.dayOfWeek

job.workStartMinute
job.workEndMinute
job.workDays

target.entity
target.location.current
target.affinity

travel.reachable
```

Introduce a small numeric expression vocabulary:

```text
fact
constant
normalize
normalizeRange
add
subtract
multiply
divide
min
max
clamp
oneMinus
abs
```

Compile expression definitions during content loading.

Do not interpret string expression names during each decision tick.

## Issues

### Issue M7.1 — Introduce `FactId` and fact registry

**Description**

Define stable runtime fact identifiers and a registry that resolves data references at load time.

**Acceptance criteria**

- Fact references are validated during content load.
- Runtime evaluation uses resolved handles, not strings.
- Unknown facts fail fast with clear diagnostics.

---

### Issue M7.2 — Introduce numeric expression model

**Description**

Replace `UtilityInputDefinition.Source` with an expression definition.

**Acceptance criteria**

- Existing attribute inputs can be represented.
- `lowWealth` can be represented compositionally.
- `schedulePressure` can be represented compositionally.
- `peerAffinity` can be represented without a special-case source.
- Expression depth or complexity is bounded.

---

### Issue M7.3 — Add compiled numeric evaluator

**Description**

Compile authoring expressions to a compact runtime form.

Possible implementation choices:

- postfix opcode array;
- typed evaluator structs;
- expression nodes resolved to delegates only if allocation-free and performant.

**Acceptance criteria**

- No string parsing in `AgentDecisionSystem`.
- No semantic source switch remains.
- Runtime evaluator is deterministic.
- Existing utility scores remain within test tolerances.

---

### Issue M7.4 — Migrate `actions.json`

**Description**

Rewrite current utility inputs using the expression model.

**Acceptance criteria**

- `work`, `rest`, and `socialize` retain equivalent scoring.
- Old `source` syntax is removed or explicitly deprecated.

---

# 6. Milestone M8 — Predicate IR

## Goal

Remove hard-coded eligibility gates:

- `workSchedule`;
- `homeReachable`;
- `availablePeer`.

## Instructions

Introduce a declarative predicate language.

Required operations should include:

```text
all
any
not

==
!=
>
>=
<
<=

contains
exists
```

Predicates should operate over the same fact/value system introduced in M7.

Example:

```json
{
  "eligibleWhen": {
    "all": [
      {
        "op": "contains",
        "left": "job.workDays",
        "right": "time.dayOfWeek"
      },
      {
        "op": ">=",
        "left": "time.minuteOfDay",
        "right": {
          "add": ["job.workStartMinute", -60]
        }
      },
      {
        "op": "<",
        "left": "time.minuteOfDay",
        "right": "job.workEndMinute"
      }
    ]
  }
}
```

## Issues

### Issue M8.1 — Define predicate authoring schema

**Acceptance criteria**

- Supports current work eligibility.
- Supports current rest eligibility.
- Supports current socialize eligibility.
- Invalid type combinations fail at load time.

---

### Issue M8.2 — Add compiled predicate evaluator

**Acceptance criteria**

- No hard-coded gate switch remains.
- Predicate evaluation uses pre-resolved operands.
- Evaluation produces no avoidable per-agent allocations.

---

### Issue M8.3 — Remove `ActionEligibilityDefinition.Gate`

**Acceptance criteria**

- `ActionEligibilityDefinition` is replaced with predicate data.
- No whitelist of specific gate names remains in content validation.

---

### Issue M8.4 — Migrate existing action eligibility

**Acceptance criteria**

- Existing three behaviours remain equivalent.
- Behavioural regression tests pass.

---

# 7. Milestone M9 — Data-Defined Targeting

## Goal

Remove special handling for social peers, work locations, and home locations from decision code.

## Instructions

Every intent should define its target strategy.

Supported target kinds should initially be:

```text
none
entity
location
```

Target selectors should support:

- direct value resolution;
- entity queries;
- requirements;
- ranking;
- deterministic tie-breaking;
- optional candidate limit.

Example social target:

```json
{
  "target": {
    "kind": "entity",
    "query": {
      "relation": "social",
      "requirements": [
        {
          "op": "==",
          "left": "target.location.current",
          "right": "agent.location.current"
        }
      ],
      "rankBy": [
        {
          "value": "target.affinity",
          "order": "descending"
        }
      ],
      "limit": 1
    }
  }
}
```

Example work target:

```json
{
  "target": {
    "kind": "location",
    "value": "agent.location.work"
  }
}
```

## Issues

### Issue M9.1 — Introduce target definition model

**Acceptance criteria**

- Supports `none`, `entity`, and `location`.
- Target type is validated against execution requirements.

---

### Issue M9.2 — Build generic social-relation target query

**Acceptance criteria**

- Can reproduce current highest-affinity colocated peer selection.
- Tie-breaking remains deterministic.
- `AgentDecisionSystem` no longer knows that socialize means peer targeting.

---

### Issue M9.3 — Move destination resolution into intent data

**Acceptance criteria**

- Work location is data-defined.
- Home location is data-defined.
- `AgentDecisionSystem` no longer branches on work/rest/socialize IDs.

---

### Issue M9.4 — Generalise decision result structure

Suggested runtime result:

```csharp
public readonly record struct DecisionResult(
    int IntentIndex,
    bool Eligible,
    float Score,
    int TargetEntityId,
    int TargetLocationId);
```

**Acceptance criteria**

- Target selection and scoring are represented in one deterministic candidate result.
- Winner application contains no domain-specific branches.

---

# 8. Milestone M10 — Generic Execution

## Goal

Remove action-specific semantics from `CommutingSystem`.

## Instructions

Replace domain behaviour with generic execution primitives.

Initial executor kinds:

```text
performHere
performAtLocation
performWithEntity
wait
```

Potential future executor kinds:

```text
moveToLocation
followEntity
exchange
observe
consume
produce
interact
```

Example:

```json
{
  "execution": {
    "executor": "performAtLocation",
    "destination": "intent.target"
  }
}
```

## Issues

### Issue M10.1 — Introduce executor definition

**Acceptance criteria**

- Executor kind is declared in intent data.
- Required target type is validated at load time.

---

### Issue M10.2 — Extract generic movement execution

**Description**

Move current travel-to-work/home behaviour into generic travel-to-target-location mechanics.

**Acceptance criteria**

- Movement code does not know intent IDs.
- Any intent can cause travel to a target location.
- Travel completion marks relevant decision state dirty.

---

### Issue M10.3 — Implement `performAtLocation`

**Acceptance criteria**

- Agent travels to the target location when required.
- Agent transitions to performing once colocated with destination.
- No work/rest-specific code exists in executor logic.

---

### Issue M10.4 — Implement `performWithEntity`

**Acceptance criteria**

- Executor can require co-location with target entity.
- Target loss or invalidation marks decision state dirty.
- No socialize-specific branch remains.

---

### Issue M10.5 — Replace or rename `CommutingSystem`

Recommended direction:

```text
CommutingSystem
    ↓
IntentExecutionSystem
MovementExecutionSystem
```

**Acceptance criteria**

- Movement is treated as execution mechanics rather than action semantics.
- Existing travel behaviour remains equivalent.

---

# 9. Milestone M11 — Data-Defined Activity Identity

## Goal

Remove domain semantics from `ActivityKind`.

Current semantic states such as:

- `Working`;
- `Resting`;
- `Socializing`;

should not be hard-coded engine states.

## Instructions

Replace semantic activity kinds with:

- content identity;
- generic execution phase.

Suggested model:

```csharp
public struct ActivityState : IComponent
{
    public int ActionHash;
    public int ActivityTypeHash;
    public ActivityPhase Phase;
    public long StartedAtMinute;
}
```

Suggested phase enum:

```csharp
public enum ActivityPhase : byte
{
    Idle,
    Moving,
    Performing,
    Blocked
}
```

## Issues

### Issue M11.1 — Introduce `ActivityPhase`

**Acceptance criteria**

- Enum contains only engine execution states.
- No domain-specific behaviour names remain.

---

### Issue M11.2 — Add data-defined activity identity

**Acceptance criteria**

- Activity type comes from intent data.
- UI/debug systems resolve display names through the content catalog.

---

### Issue M11.3 — Migrate existing consumers

**Acceptance criteria**

- Debug and UI remain functional.
- Effects system uses action/activity hashes rather than semantic enum cases.

---

# 10. Milestone M12 — Intent Compiler and Structural Validation

## Goal

Separate human-readable authoring format from fast runtime representation.

## Instructions

Rename or evolve:

```text
ActionDefinition
    ↓
IntentDefinition
```

Introduce:

```text
IntentDefinition
    ↓
IntentCompiler
    ↓
CompiledIntent
```

Suggested runtime shape:

```csharp
public sealed class CompiledIntent
{
    public int Hash;
    public ushort RuntimeIndex;

    public PredicateProgram Eligibility;

    public CompiledTargetSelector TargetSelector;

    public CompiledUtilityTerm[] UtilityTerms;
    public CompiledTraitModifier[] TraitModifiers;

    public IntentControls Controls;

    public ExecutorKind Executor;
    public ValueHandle Destination;

    public CompiledEffect[] Effects;

    public FactDependencyMask Dependencies;
}
```

## Issues

### Issue M12.1 — Introduce `IntentCompiler`

**Acceptance criteria**

- All string references resolve at startup.
- Compiler produces immutable runtime definitions.
- Runtime systems consume only compiled intents.

---

### Issue M12.2 — Add dense runtime indexes

**Description**

Retain stable content hash for persistence/content identity, and add dense runtime index for arrays/bitsets.

**Acceptance criteria**

- Stable hashes remain externally meaningful.
- Runtime intent lookup is array-based where practical.

---

### Issue M12.3 — Replace semantic validation

Remove rules such as:

```text
must define work
must define rest
must define socialize
must use one of these named gates
```

Replace with structural rules:

- unique IDs;
- unique hashes;
- valid references;
- valid operand types;
- valid target/executor combinations;
- monotonic curve X values;
- finite numeric values;
- valid control ranges;
- supported effects;
- optional required fallback intent.

**Acceptance criteria**

- Catalog validation contains no specific domain intent IDs.
- Invalid content fails with precise path-aware error messages.

---

### Issue M12.4 — Introduce fallback intent

Suggested definition:

```json
{
  "id": "idle",
  "fallback": true
}
```

**Acceptance criteria**

- Exactly one fallback can be designated if required by the design.
- Decision system always has a safe no-op result.

---

# 11. Milestone M13 — Dependency-Driven Reevaluation

## Goal

Move from broad periodic reconsideration toward fact-driven reevaluation.

## Instructions

Compile fact dependencies for each intent.

Example:

```text
work:
    TIME_MINUTE
    WORK_SCHEDULE
    FATIGUE
    STRESS
    WEALTH
    GREEDY_TRAIT
    LOCATION

socialize:
    PREFERENCE
    CHARISMA
    STRESS
    SOCIAL_PEERS
    PEER_AFFINITY
    LOCATION
```

When facts change, mark relevant decisions dirty.

Target direction:

```text
fact mutation
    ↓
changed fact mask
    ↓
DecisionState dirty dependencies
    ↓
evaluate affected intents only
```

## Issues

### Issue M13.1 — Introduce `FactDependencyMask`

**Acceptance criteria**

- Compiler derives dependencies from expressions, predicates, and target queries.
- Dependencies require no manual duplication in intent data.

---

### Issue M13.2 — Track changed fact categories

**Acceptance criteria**

- Attribute mutation can signal affected fact categories.
- Location changes can signal affected fact categories.
- target availability changes can signal affected fact categories.

---

### Issue M13.3 — Selective intent reevaluation

**Acceptance criteria**

- Only intents dependent on changed facts are rescored where practical.
- Minute-based reevaluation can remain as a safety fallback during migration.
- Behaviour remains deterministic.

---

### Issue M13.4 — Benchmark dependency-driven decisions

**Acceptance criteria**

- Compare against M6 baseline.
- Record evaluation count reduction.
- Record CPU and allocation impact.

---

# 12. Milestone M14 — Candidate Indexing

## Goal

Avoid scoring every intent for every agent as the catalogue grows.

## Instructions

Compile fast candidate indexes.

Possible dimensions:

```text
requires job
requires social relation
requires home
requires workplace
requires trait
requires network membership
requires capability
requires inventory class
```

Represent candidate groups using dense bitsets.

## Issues

### Issue M14.1 — Add intent candidate bitsets

**Acceptance criteria**

- Runtime indexes support compact bitsets.
- Global candidate set can be intersected with agent/context sets.

---

### Issue M14.2 — Build static intent indexes

**Acceptance criteria**

- Index construction happens at startup.
- Candidate generation avoids scanning full intent catalogue where possible.

---

### Issue M14.3 — Add scaling benchmark

Benchmark at minimum:

- 3 intents;
- 32 intents;
- 128 intents;
- 256 intents;

with 1,000 agents.

**Acceptance criteria**

- Results recorded in docs.
- Performance regressions are explained before merge.

---

# 13. Milestone M15 — Tooling, Diagnostics, and Content Safety

## Goal

Make data-driven behaviour understandable and debuggable.

A declarative system without diagnostics becomes expensive to tune.

## Issues

### Issue M15.1 — Add decision inspector

Display for a selected agent:

- candidate intent;
- eligibility result;
- rejected predicate;
- target;
- utility base;
- each utility contribution;
- trait modifiers;
- cooldown;
- commitment block;
- final score;
- selected winner.

**Acceptance criteria**

- Inspector is debug-only.
- Inspector does not expose additional state to player-facing intelligence systems.

---

### Issue M15.2 — Add content validation command/test

**Acceptance criteria**

- CI validates all intent data.
- Error output identifies file, intent ID, and problematic path.

---

### Issue M15.3 — Add intent authoring documentation

Document:

- fact catalogue;
- predicate operators;
- numeric operators;
- target selector syntax;
- executor kinds;
- effect kinds;
- common patterns;
- performance cautions.

---

### Issue M15.4 — Add data-only extensibility test

Create a test fixture with a new intent, for example `eat`.

The test intent should:

- have declarative eligibility;
- select a target location;
- use generic execution;
- modify attributes through generic effects.

**Acceptance criteria**

- Test adds no new runtime C# branch for the intent.
- Test proves the intent can be loaded, selected, executed, and applied from data.

---

# 14. Recommended File / Namespace Structure

Target structure:

```text
Simulation/
    Decision/
        IntentDefinition.cs
        IntentCompiler.cs
        CompiledIntent.cs

        Facts/
            FactId.cs
            FactRegistry.cs
            FactReader.cs

        Expressions/
            ValueProgram.cs
            PredicateProgram.cs

        Targeting/
            TargetDefinition.cs
            TargetSelector.cs

        Runtime/
            AgentDecisionSystem.cs
            IntentScorer.cs
            IntentArbitrator.cs

        Execution/
            IntentExecutionSystem.cs
            MovementExecutionSystem.cs
            ExecutorKind.cs

        Effects/
            ActivityEffectsSystem.cs
```

The conceptual dependency direction should remain:

```text
facts
  ↓
decision
  ↓
intent
  ↓
execution
  ↓
effects
```

Avoid circular dependencies between these layers.

---

# 15. Recommended Data Model Direction

The final authoring model should approximately support:

```json
{
  "id": "work",
  "hash": 1001,
  "name": "Work",

  "eligibleWhen": {
    "all": [
      {
        "op": "contains",
        "left": "job.workDays",
        "right": "time.dayOfWeek"
      }
    ]
  },

  "target": {
    "kind": "location",
    "value": "agent.location.work"
  },

  "utility": {
    "base": 10,
    "inputs": [
      {
        "value": {
          "oneMinus": {
            "normalize": "agent.attribute.wealth"
          }
        },
        "weight": 25,
        "curve": [[0, 0], [1, 1]]
      }
    ],
    "traitModifiers": [
      {
        "trait": "greedy",
        "modifier": 12
      }
    ]
  },

  "controls": {
    "minimumCommitmentMinutes": 30,
    "switchingThreshold": 8,
    "cooldownMinutes": 15,
    "urgentPreemptionThreshold": 85
  },

  "execution": {
    "executor": "performAtLocation",
    "destination": "intent.target"
  },

  "effects": [
    {
      "type": "attributeRate",
      "attribute": "wealth",
      "perMinute": 2
    }
  ]
}
```

The exact JSON schema can evolve during implementation.

The architectural requirements should not.

---

# 16. Migration Constraints

During migration:

1. Preserve existing save/content hashes.
2. Preserve deterministic tie-breaking.
3. Preserve current utility curve semantics.
4. Preserve minimum commitment behaviour.
5. Preserve switching thresholds.
6. Preserve urgent preemption.
7. Preserve cooldown semantics.
8. Preserve current travel timing.
9. Preserve player/ground-truth information boundaries.
10. Avoid mixing the new and old semantic models longer than necessary.

If a compatibility shim is introduced, mark it explicitly for removal in the relevant milestone.

---

# 17. Testing Strategy

Every milestone must include:

## Unit tests

For:

- expression compilation;
- expression evaluation;
- predicate compilation;
- predicate evaluation;
- target resolution;
- target ranking;
- executor validation;
- effect validation.

## Behavioural tests

For:

- existing work/rest/socialize outcomes;
- commitment;
- cooldown;
- preemption;
- target loss;
- travel completion;
- fallback behaviour.

## Property/invariant tests

Where practical:

- same world state gives same decision;
- invalid references fail at load time;
- curve evaluation remains bounded as defined;
- tie-breaking is deterministic;
- runtime never resolves content strings.

## Performance tests

Track:

- decision evaluations per simulated minute;
- mean decision-system CPU cost;
- allocation volume;
- cost as intent count rises.

---

# 18. Merge Strategy

Use small vertical changes.

Recommended sequence:

```text
M6
↓
M7
↓
M8
↓
M9
↓
M10
↓
M11
↓
M12
↓
M13
↓
M14
↓
M15
```

Do not combine M7–M12 into a single rewrite.

Preferred PR rule:

> Each PR should either preserve observable behaviour or introduce one explicitly tested new capability.

---

# 19. Priority Order

## Priority 1 — Remove semantic coupling

Complete first:

- M7 Fact/Expression IR;
- M8 Predicate IR;
- M9 Targeting;
- M10 Generic Execution;
- M11 Activity Identity.

These milestones achieve the central architectural objective.

## Priority 2 — Harden the architecture

Then complete:

- M12 Compiler and structural validation;
- M15 Diagnostics and tooling.

## Priority 3 — Scale

Then complete:

- M13 Dependency-driven reevaluation;
- M14 Candidate indexing.

Do not prematurely optimise candidate indexing before semantic coupling is removed.

---

# 20. Final Architectural Acceptance Test

The strongest acceptance test is:

> Add a novel intent using data only.

For example, add `eat` with:

- eligibility based on an attribute;
- target location selection;
- weighted utility;
- commitment and cooldown;
- generic travel;
- generic performance;
- generic attribute effects.

If adding `eat` requires changing any of the following, the architecture is not yet fully data-driven:

```text
AgentDecisionSystem
IntentExecutionSystem
MovementExecutionSystem
ActivityEffectsSystem
ContentCatalog semantic validation
```

The only permitted runtime-code change should be when the new intent requires a genuinely new simulation mechanic not already represented by existing generic primitives.

---

# 21. Immediate Next Issues

Start implementation with these issues in this order:

1. **M6.1 — Add deterministic decision fixtures**
2. **M6.3 — Record performance baseline**
3. **M7.1 — Introduce `FactId` and fact registry**
4. **M7.2 — Introduce numeric expression model**
5. **M7.3 — Add compiled numeric evaluator**
6. **M7.4 — Migrate existing utility inputs**
7. **M8.1 — Define predicate authoring schema**
8. **M8.2 — Add compiled predicate evaluator**
9. **M8.3 — Remove named eligibility gates**
10. **M9.1 — Introduce target definition model**

At the end of issue 10, review the architecture before beginning execution refactoring.

The review question should be:

> Can a new intent now become eligible, select a target, and win utility scoring without introducing any new decision-system branch?

If yes, proceed to M10.

If no, resolve the remaining semantic leakage before expanding the executor layer.
