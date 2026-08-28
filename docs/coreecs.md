# 4. Core ECS Systems (Logic)

### 4.1 Utility AI System

**Goal:** Determine what an agent does this tick.

* Query all entities with `AgentState` and `Tier1LodTag`.
* Iterate through available actions (Work, Rest, Socialize) loaded from `actions.json`.
* Score formula: `BaseScore + (TraitModifiers) - (Fatigue/Stress Penalties)`.
* Assign highest-scoring action to `AgentState.CurrentActionHash`.

### 4.2 Interaction & Discovery System

**Goal:** Handle target interrogation/surveillance based on Perception vs Willpower.

* When `Source` interacts with `Target`, calculate: `Source.Perception` vs `Target.Willpower` (modified by Target's `Paranoid` trait).
* On success, perform bitwise `OR` on `EdgeData.KnownTraitMask`. (e.g., `KnownTraitMask |= 0x0004` to reveal the Greedy trait).
* Recalculate `Affinity` by checking shared traits: `Target.Psychology.TraitMask & EdgeData.KnownTraitMask`.

---