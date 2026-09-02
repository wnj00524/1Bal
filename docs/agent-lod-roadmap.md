# Proxy State — Agent LOD and 100,000-Agent Roadmap

This is the canonical implementation playbook for [Epic #106: Three-tier agent LOD and 100,000-agent support](https://github.com/wnj00524/proxystate/issues/106). GitHub issues contain the same task cards so an agent can work from either location. If the roadmap and an issue disagree, stop and synchronize both before implementing.

## 1. Purpose and final outcome

Proxy State currently creates 1,000 agents and simulates every one at full detail. This roadmap introduces three levels of detail (LOD) so computational effort follows player relevance while the world remains deterministic and coherent.

| Tier | Who belongs here | Simulation behavior |
| --- | --- | --- |
| **Tier 1 — full detail** | Player Operatives and agents under investigation. | Keep the current once-per-simulated-minute utility decisions, dependency invalidation, travel, activities, effects, coordination, and relationship discovery. |
| **Tier 2 — reduced decisions** | Direct social neighbours of Tier 1, direct supervisors/reports, and temporary interaction partners. | Reconsider intentions every 60 simulated minutes. Continue movement, effects, coordination, and critical event wake-ups continuously. |
| **Tier 3 — coarse routine** | Everyone else. | Reuse a shared deterministic weekly itinerary compiled from JSON, job, traits, and commute time. Apply effects in bulk without utility decisions or coordination. |

The complete application—not only a headless benchmark—must support 100,000 agents. The ordinary launch remains at 1,000 agents. Milestone 20 adds the explicit --agents 100000 option.

## 2. Non-negotiable rules

1. Tier1LodTag, Tier2LodTag, and Tier3LodTag are mutually exclusive.
2. Only AgentLodService may change LOD tags, LOD interest state, detailed-component ownership, or coarse-shard membership.
3. OperativeTag and the Investigation interest reason place an agent in Tier 1.
4. Tier 2 relation scope is exactly:
   - an incoming or outgoing direct social EdgeData relationship;
   - a direct network supervisor;
   - a direct network report;
   - an active interaction pin.
5. Shared company membership alone does not create Tier 2 relevance. Family and friend groups already seed social edges.
6. Tier 1 retains existing behavior and deterministic decision traces.
7. Tier 2 decisions use a 60-simulated-minute cadence. Rendering ticks never control simulation cadence.
8. Tier 2 movement, activity effects, and coordination continue using live elapsed simulation time.
9. Tier 3 behavior is authored in data/lod.json; runtime code must not branch on intent IDs such as work, rest, or socialize.
10. Promotion is immediate. A requested demotion waits until the next in-world day boundary. Active coordination prevents demotion.
11. A fixed seed and identical content must produce identical classification, schedules, choices, and effects.
12. ImGui never queries EntityStore, ECS components, or Ground Truth services. It receives copied projections and sends commands.
13. Tier 3 agents do not own DecisionState caches, CoordinationState, or per-agent travel-route arrays.
14. Do not introduce population-wide target, edge, intelligence, or UI-detail snapshots into a per-frame path.

## 3. Current architecture and bottlenecks

The repository already defines all three LOD tags, but AgentSpawner gives every generated agent Tier1LodTag. Detailed decision, execution, coordination, effects, and fatigue systems filter to Tier 1, so Tier 2 and Tier 3 are scaffolding only.

The starting behavioral baseline is **126 passing tests**. Preserve that baseline before adding new tests.

Known scale blockers:

- AgentDecisionSystem.TargetResolver reconstructs dictionaries for every agent location, attribute array, social edge, affinity, membership, and network member during an update.
- Entity target ranking allocates temporary rank arrays.
- IntentExecutionSystem builds a full-population location dictionary each update.
- InteractionSystem scans all directed EdgeData entities at its interval.
- PlayerIntelligenceDB.Capture recopies every agent and scans every relationship each frame.
- The dossier and debug UI iterate or copy the entire population rather than only visible/selected rows.
- Every spawned agent owns detailed decision arrays and route storage even when irrelevant.

Milestone 17 records deterministic work counters before changing these paths. Do not substitute wall-clock timing for deterministic visit/evaluation counts.

## 4. Target architecture

### 4.1 Ordered simulation pipeline

~~~text
World clock
    ↓
Apply investigation commands and interest changes
    ↓
LOD classification, immediate promotions, due demotions
    ↓
Tier 3 sharded catch-up
    ↓
Tier 1 / Tier 2 decisions
    ↓
Promote entity targets that require detailed participation
    ↓
Coordination
    ↓
Detailed movement and execution
    ↓
Detailed activity effects
    ↓
Indexed relationship discovery
    ↓
Sanitized intelligence events
    ↓
Copied intelligence/debug projections
    ↓
ImGui presentation
~~~

This ordering is a contract. A Tier 3 invitation target must be caught up and promoted before coordination reads detailed components.

### 4.2 Component ownership

| State | Tier 1 | Tier 2 | Tier 3 |
| --- | :---: | :---: | :---: |
| Identity, faction, psychology, numeric attributes, secret state, home/work/current location, network membership, LOD state | Yes | Yes | Yes |
| DetailedSimulationTag | Yes | Yes | No |
| Intention, public activity, decision cache, travel progress, coordination | Yes | Yes | No |
| Shared routine profile ID/fingerprint and coarse watermark | Available | Available | Active |
| Tier-specific tag | Tier 1 | Tier 2 | Tier 3 |

AgentLodState holds the desired tier, direct-POI reference count, interest flags, scheduled demotion minute, coarse profile ID/fingerprint, and last coarse-simulated minute. The exclusive tags represent current tier and must not drift from service-owned state.

### 4.3 Classification and transitions

Initial classification runs after agents, networks, social edges, and persistent indexes exist.

- Operatives and investigated agents become Tier 1.
- Their direct social neighbours and direct supervisor/report neighbours become Tier 2.
- Multiple Tier 1 neighbours are reference-counted.
- Active interaction places an otherwise unrelated agent at least in Tier 2 without expanding the permanent one-degree frontier.
- Increasing detail applies immediately.
- Decreasing detail waits for the next day boundary and for stronger reasons to clear.
- Archetype/component changes are queued outside active ECS query iteration.

Milestone 18 uses enabled=true and tier3Enabled=false. Desired Tier 3 agents are temporarily clamped to detailed Tier 2 until Milestone 19 is complete.

### 4.4 Tier 3 routines

data/lod.json defines workday/non-workday segments, fixed or fill durations, symbolic home/work locations, effect roles, trait duration changes, a 60-minute Tier 2 cadence, end-of-day demotion, and 24 coarse shards.

A shared profile cache is keyed by occupation hash, trait mask, commute duration, LOD content revision, and world-topology revision. Each profile contains seven deterministic days. Agents store only a shared profile ID, material fingerprint, and coarse watermark.

Material changes are occupation, traits, home/work assignment, topology revision, or LOD content revision. Numeric attribute changes do not rebuild a routine.

One compact shard is processed per simulated hour. A specific Tier 3 agent is synchronously caught up before detailed code reads its effective attributes/location or promotes it.

### 4.5 Persistent lookup and intelligence boundaries

Build a packed deterministic outgoing social adjacency index after graph creation. Detailed systems enumerate only an actor's bounded adjacency range and native network relations. They access resolved target entities directly instead of reconstructing population dictionaries.

The player intelligence projection copies stable identity data once and receives sanitized trait-discovery and investigation events. The UI may display IsUnderInvestigation, but it must not receive LOD tier/reasons, Ground Truth traits, network membership, coordination, or Entity values.

Dossier and debug lists use search caches and ImGui clipping. Debug state for one selected agent is copied on demand; only debug projections may contain LOD diagnostics.

### 4.6 Milestone dependency chain

~~~text
M17 scalable lookup foundation
  └─ M18 classification + Tier 2 lifecycle
       └─ M19 Tier 3 coarse routines + transitions
            └─ M20 incremental projections + virtualized UI + 100k verification
~~~

Work is intentionally serial. Only the next unblocked issue should be Ready.

## 5. Implementation task cards

Each card below is also the body of its linked GitHub issue. Implement only one card at a time. Its Explicitly out of scope section is binding.

### [#107: M17.1 — Record LOD and large-population baselines](https://github.com/wnj00524/proxystate/issues/107)

**Tracking:** Milestone 17: Scalable Agent Lookup Foundation · Priority P1 · Size M · Initial status Ready

## Goal

Establish reproducible measurements and deterministic counters before changing lookup or LOD behavior.

## Prerequisites

Blocked by [#106: Epic — Three-tier agent LOD and 100,000-agent support](https://github.com/wnj00524/proxystate/issues/106). The existing 126-test suite must pass before baseline work begins.

## In scope

- Add counters for decision passes, candidate evaluations, target population visits, edge visits, and transient allocation-sensitive operations.
- Add Release benchmark fixtures for population generation and the current detailed loop at 1,000, 10,000, and 100,000 agents. A timeout or memory failure at the larger sizes is a result to record, not a reason to weaken the fixture.
- Record host/runtime details and results in the decision baseline document.

## Explicitly out of scope

- Do not add relationship indexes.
- Do not change agent classification, decision cadence, or simulation results.
- Do not optimize code in this issue.

## Likely code, data, and documentation areas

tests/ProxyState.Tests performance fixtures, Simulation decision diagnostics, docs/decisionbaseline.md, docs/coreecs.md.

## Ordered implementation steps

1. Run and record the unmodified full test suite.
2. Add deterministic counters that do not depend on wall-clock timing.
3. Add isolated generation and detailed-loop benchmark cases for the three required populations.
4. Run Release benchmarks on the same host and record elapsed time, allocations, counts, failures, and limitations.
5. Confirm counters do not alter fixed-seed decision traces.

## Required tests

- [ ] Counter repeatability for a fixed seed.
- [ ] Existing decision baseline and relationship behavior fixtures.
- [ ] Release benchmarks for 1,000, 10,000, and 100,000 agents.

## Acceptance criteria

- [ ] The baseline separates deterministic work counts from noisy elapsed timing.
- [ ] All existing behavior tests pass unchanged.
- [ ] The documentation clearly identifies every population-wide scan that later issues must remove.

## Handoff

M17.2 consumes these counters and must demonstrate that packed-index construction is deterministic without changing behavior.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#108: M17.2 — Build compact agent and social relationship indexes](https://github.com/wnj00524/proxystate/issues/108)

**Tracking:** Milestone 17: Scalable Agent Lookup Foundation · Priority P1 · Size L · Initial status Backlog

## Goal

Provide immutable, allocation-conscious lookup structures for agents and directed social edges after population generation.

## Prerequisites

Blocked by [#107: M17.1 — Record LOD and large-population baselines](https://github.com/wnj00524/proxystate/issues/107). This predecessor must be Done before implementation starts.

## In scope

- Create a compact agent directory for direct ID-to-entity access without rebuilding dictionaries per update.
- Create a packed outgoing social adjacency index containing target IDs and EdgeData entity IDs in deterministic order.
- Build indexes after agents, networks, and social edges are generated.
- Expose read-only span-style enumeration and explicit rebuild/notification entry points for future social graph mutations.

## Explicitly out of scope

- Do not change TargetResolver yet.
- Do not implement LOD classification.
- Do not replace EdgeData entities or redesign the social graph schema.

## Likely code, data, and documentation areas

Simulation/AgentSpawner.cs, Simulation/SocialGraph.cs, new Simulation index types, focused index tests, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Define compact index records with no strings or per-agent dictionaries.
2. Sort by source agent ID and then target agent ID so a fixed seed produces identical packed ranges.
3. Build and retain one index instance at bootstrap after graph generation.
4. Add lookup methods for agent ID, outgoing relationship count, outgoing edge span, and a specific directed edge.
5. Document the social graph as immutable after generation for this slice and make unsupported mutation behavior explicit.

## Required tests

- [ ] Empty and single-agent populations.
- [ ] Deterministic packed layout for a fixed seed.
- [ ] Every EdgeData entity appears exactly once in its source range.
- [ ] Lookup misses return safely without allocating.

## Acceptance criteria

- [ ] Construction is O(agents + directed edges) and persistent storage is linear.
- [ ] Enumerating one agent's social neighbours visits only that agent's range.
- [ ] Current simulation results remain unchanged.

## Handoff

M17.3 must consume these indexes instead of reconstructing population snapshots.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#109: M17.3 — Refactor decision targeting to indexed direct lookup](https://github.com/wnj00524/proxystate/issues/109)

**Tracking:** Milestone 17: Scalable Agent Lookup Foundation · Priority P1 · Size XL · Initial status Backlog

## Goal

Make detailed target resolution proportional to the actor's bounded relationships rather than the total population.

## Prerequisites

Blocked by [#108: M17.2 — Build compact agent and social relationship indexes](https://github.com/wnj00524/proxystate/issues/108). This predecessor must be Done before implementation starts.

## In scope

- Inject the persistent indexes into AgentDecisionSystem and CoordinationSystem target resolution.
- Read target location and attributes directly from the resolved entity.
- Use native AgentNetworkMembership relations for bounded network-member, supervisor, and direct-report enumeration.
- Replace candidate rank ToArray allocations with fixed/stack storage or an allocation-free comparison.
- Preserve stable ascending entity-ID tie breaking and all current target semantics.

## Explicitly out of scope

- Do not change LOD tags or decision frequency.
- Do not change actions.json semantics.
- Do not virtualize UI or intelligence snapshots.

## Likely code, data, and documentation areas

Simulation/DecisionSystems.cs, Simulation/CoordinationSystem.cs, Program.cs, decision/relationship tests, docs/coreecs.md.

## Ordered implementation steps

1. Replace TargetResolver's constructor-wide location, attribute, social, affinity, membership, and member dictionaries.
2. Resolve social candidates from packed outgoing spans and network candidates from native relations.
3. Keep target requirements and rank expressions compiled and allocation-conscious.
4. Update coordination relation validation to use the same resolver contract.
5. Compare decision traces and deterministic work counters with M17.1.

## Required tests

- [ ] Every social and network selector direction.
- [ ] Target attribute and affinity facts.
- [ ] Exact rank ties and missing/deleted targets.
- [ ] Existing decision and relationship-driven-life suites.

## Acceptance criteria

- [ ] A decision update does not enumerate all agents or all edges to construct target state.
- [ ] Selected intentions and targets match the pre-refactor deterministic fixtures.
- [ ] Hot target enumeration performs no avoidable per-candidate heap allocation.

## Handoff

M17.4 removes the remaining execution and relationship-interaction population scans.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#110: M17.4 — Remove population scans from execution and interaction](https://github.com/wnj00524/proxystate/issues/110)

**Tracking:** Milestone 17: Scalable Agent Lookup Foundation · Priority P1 · Size L · Initial status Backlog

## Goal

Complete the scalable detailed-loop foundation by removing full-population destination and edge scans.

## Prerequisites

Blocked by [#109: M17.3 — Refactor decision targeting to indexed direct lookup](https://github.com/wnj00524/proxystate/issues/109). This predecessor must be Done before implementation starts.

## In scope

- Resolve an IntentionState target entity directly rather than building a location dictionary every execution update.
- Change InteractionSystem to enumerate indexed outgoing edges for eligible source agents.
- Add explicit index rebuild or invalidation notifications at supported relationship mutation boundaries.
- Use M17.1 counters to prove detailed work follows active sources and their edges.

## Explicitly out of scope

- Do not implement Tier 2 or Tier 3 behavior.
- Do not change discovery probabilities, affinity calculation, or travel mechanics.
- Do not change player intelligence capture yet.

## Likely code, data, and documentation areas

Simulation/WorldSystems.cs, Simulation/SocialGraph.cs, Program.cs, execution/interaction tests, docs/coreecs.md.

## Ordered implementation steps

1. Replace IntentExecutionSystem's per-update entity-location dictionary with direct safe target lookup.
2. Drive relationship interactions by source-agent adjacency ranges while retaining the configured interval and random stream behavior.
3. Ensure deleted/missing targets invalidate decisions through the existing generic path.
4. Add instrumentation assertions that unrelated population growth does not increase one actor's target/edge visits.
5. Run all fixed-seed behavior suites.

## Required tests

- [ ] Moving entity targets, deletion, unreachable routes, and target loss.
- [ ] Trait discovery and affinity updates for indexed outgoing edges.
- [ ] Scaling fixture with constant detailed sources and increasing unrelated population.

## Acceptance criteria

- [ ] No detailed execution tick creates a full-population location snapshot.
- [ ] No interaction interval scans every EdgeData entity when only bounded sources are eligible.
- [ ] Milestone 17 behavior is identical to the recorded baseline.

## Handoff

M18.1 may now introduce LOD contracts without inheriting population-wide detailed-system costs.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#111: M18.1 — Add LOD configuration and runtime contracts](https://github.com/wnj00524/proxystate/issues/111)

**Tracking:** Milestone 18: Dynamic Agent LOD Lifecycle · Priority P1 · Size M · Initial status Backlog

## Goal

Define the authoritative data and ECS contracts for staged LOD rollout without enabling Tier 3.

## Prerequisites

Blocked by [#110: M17.4 — Remove population scans from execution and interaction](https://github.com/wnj00524/proxystate/issues/110). This predecessor must be Done before implementation starts.

## In scope

- Add data/lod.json with enabled, tier3Enabled, Tier 2 interval 60, relatedBy values social/networkSupervisor/networkDirectReport, endOfDay demotion, and Tier 3 shard count 24.
- Add AgentLodTier, AgentInterestReason, AgentLodState, and DetailedSimulationTag.
- Keep Tier1LodTag, Tier2LodTag, and Tier3LodTag mutually exclusive and define AgentLodService as their sole future writer.
- Load and structurally validate classification/rollout settings through ContentCatalog.

## Explicitly out of scope

- Do not classify agents or add investigation UI.
- Do not compile Tier 3 routine segments yet.
- Do not remove detailed components from any spawned agent.

## Likely code, data, and documentation areas

data/lod.json, Simulation/Components.cs, Simulation/ContentCatalog.cs, content validation tests, docs/datastructs.md, docs/editing-data.md.

## Ordered implementation steps

1. Define JSON authoring records and compiled runtime enums/values.
2. Use enabled=true and tier3Enabled=false for the two-tier transitional rollout.
3. Validate the exact supported relationship tokens and fixed positive cadence/shard values.
4. Add pure helpers that verify exactly one tier tag and synchronize DetailedSimulationTag for Tier 1/2.
5. Document ownership and rollout rules.

## Required tests

- [ ] Valid production lod.json loads.
- [ ] Unknown relation, invalid cadence/shard count, and invalid demotion policy fail with path-specific errors.
- [ ] Mutually exclusive tag helper behavior.

## Acceptance criteria

- [ ] All tuning values are data-backed and compiled before runtime.
- [ ] Tier 3 remains disabled after this issue.
- [ ] Existing all-Tier-1 behavior remains the bootstrap default until M18.2.

## Handoff

M18.2 implements classification and investigation state using these exact contracts.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#112: M18.2 — Implement POI classification and investigation service](https://github.com/wnj00524/proxystate/issues/112)

**Tracking:** Milestone 18: Dynamic Agent LOD Lifecycle · Priority P1 · Size XL · Initial status Backlog

## Goal

Make one service authoritatively classify Operatives, investigations, and their one-degree neighbourhood.

## Prerequisites

Blocked by [#111: M18.1 — Add LOD configuration and runtime contracts](https://github.com/wnj00524/proxystate/issues/111). This predecessor must be Done before implementation starts.

## In scope

- Implement AgentLodService as the sole interest/tier mutation boundary.
- Treat OperativeTag and Investigation interest as Tier 1 reasons.
- Count social neighbours plus direct supervisors/reports as Tier 2 relationships; do not include ordinary shared-company coworkers.
- Maintain reference counts so neighbours of multiple POIs do not demote early.
- While tier3Enabled is false, clamp desired Tier 3 agents to actual Tier 2.

## Explicitly out of scope

- Do not add dossier controls.
- Do not remove detailed components or run coarse routines.
- Do not broaden one-degree classification through a Tier 2 agent's relationships.

## Likely code, data, and documentation areas

new Simulation LOD service/system, Program.cs, AgentSpawner integration, classification tests, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Build initial POI and direct-neighbour sets after relationship indexes exist.
2. Add SetInvestigation(agentId, enabled) with live-agent validation and idempotent behavior.
3. Increment/decrement per-agent POI-neighbour counts on interest changes.
4. Apply promotions immediately through queued archetype-safe transitions.
5. Expose copied investigation events for the later UI boundary without exposing Entity values.

## Required tests

- [ ] Operatives and investigations become Tier 1.
- [ ] Social neighbours and direct supervisor/report pairs become Tier 2.
- [ ] Coworkers and two-hop neighbours remain desired Tier 3.
- [ ] Overlapping POIs, repeated commands, deletion, and small populations.

## Acceptance criteria

- [ ] Classification is deterministic and local to the changed POI neighbourhood after initial build.
- [ ] Only AgentLodService changes tier/interest state.
- [ ] With Tier 3 disabled, all non-Tier-1 agents remain safely detailed Tier 2.

## Handoff

M18.3 changes decision cadence for the Tier 2 population produced here.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#113: M18.3 — Implement Tier 2 cadence and critical wake-ups](https://github.com/wnj00524/proxystate/issues/113)

**Tracking:** Milestone 18: Dynamic Agent LOD Lifecycle · Priority P1 · Size L · Initial status Backlog

## Goal

Reduce Tier 2 deliberation frequency without reducing execution fidelity.

## Prerequisites

Blocked by [#112: M18.2 — Implement POI classification and investigation service](https://github.com/wnj00524/proxystate/issues/112). This predecessor must be Done before implementation starts.

## In scope

- Keep Tier 1's current once-per-simulated-minute and dependency-driven reevaluation behavior.
- Allow Tier 2 full deliberation only after 60 simulated minutes.
- Accumulate ordinary attribute dependency changes until the next Tier 2 pass.
- Add an immediate-wake flag/reason for target loss, coordination lifecycle changes, investigation, and promotion.
- Continue movement, activity effects, and coordination on every elapsed simulation update for both detailed tiers.

## Explicitly out of scope

- Do not batch Tier 2 movement/effects hourly.
- Do not enable Tier 3.
- Do not reinterpret action utilities or cooldowns.

## Likely code, data, and documentation areas

Simulation/DecisionSystems.cs, CoordinationSystem.cs, WorldSystems.cs, cadence tests, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Extend DecisionState with the minimum state needed to distinguish accumulated dirty facts from immediate wake requests.
2. Centralize ordinary versus critical invalidation helpers.
3. Gate deliberation by tier and elapsed simulated minutes, never rendering ticks.
4. Force a full pass on promotion and preserve cooldown/commitment semantics.
5. Add evaluation-count assertions across 59-, 60-, and multi-hour intervals.

## Required tests

- [ ] Unchanged Tier 1 decision traces.
- [ ] Tier 2 does not reconsider at minutes 1-59 and does at minute 60.
- [ ] Attribute changes accumulate without early wake.
- [ ] Target loss and coordination changes wake immediately.
- [ ] Movement/effects continue between Tier 2 decisions.

## Acceptance criteria

- [ ] Tier 1 behavior remains byte-for-byte equivalent in deterministic traces.
- [ ] Tier 2 evaluates at most hourly without a critical event.
- [ ] Critical events cannot leave Tier 2 blocked until the next hour.

## Handoff

M18.4 adds delayed demotion and temporary interaction pins around this scheduler.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#114: M18.4 — Add demotion grace, interaction pins, and network notifications](https://github.com/wnj00524/proxystate/issues/114)

**Tracking:** Milestone 18: Dynamic Agent LOD Lifecycle · Priority P1 · Size L · Initial status Backlog

## Goal

Make tier changes stable under changing investigations, relationships, and coordinated activities.

## Prerequisites

Blocked by [#113: M18.3 — Implement Tier 2 cadence and critical wake-ups](https://github.com/wnj00524/proxystate/issues/113). This predecessor must be Done before implementation starts.

## In scope

- Promote immediately when desired detail increases.
- Schedule reductions for the next simulated day boundary.
- Use AgentInterestReason.ActiveInteraction to keep a participant at least Tier 2.
- Release interaction pins when coordination ends, then apply normal grace.
- Notify AgentLodService from supported supervisor/report membership mutations and deletion cleanup.

## Explicitly out of scope

- Do not enable Tier 3 or remove detailed components.
- Do not treat all network membership changes as direct relationships.
- Do not add player-facing controls.

## Likely code, data, and documentation areas

AgentLodService, AgentNetworkService, CoordinationSystem, lifecycle tests, docs/coreecs.md.

## Ordered implementation steps

1. Compute the next day-boundary minute from WorldTime.
2. Make repeated lower-tier requests retain the earliest valid boundary without shortening active pins.
3. Acquire/release pins through coordination lifecycle ownership.
4. Reclassify only agents affected by a supervisor/report change.
5. Process queued demotions before detailed decision updates.

## Required tests

- [ ] Investigation removal at several times of day.
- [ ] Relationship removal with overlapping POIs.
- [ ] Interaction that crosses a day boundary.
- [ ] Supervisor reassignment, member deletion, and pair release.

## Acceptance criteria

- [ ] No promotion waits for a cadence boundary.
- [ ] No demotion occurs before the configured day boundary.
- [ ] Active pairs cannot be split by LOD demotion.
- [ ] Network changes leave reference counts and tags consistent.

## Handoff

M19.1 can now define the Tier 3 routine data that will make desired Tier 3 transitions safe.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#115: M19.1 — Compile and validate Tier 3 routine definitions](https://github.com/wnj00524/proxystate/issues/115)

**Tracking:** Milestone 19: Data-Driven Coarse Agent Routines · Priority P1 · Size L · Initial status Backlog

## Goal

Define complete, human-authorable Tier 3 workday and non-workday routines without hard-coded intent semantics.

## Prerequisites

Blocked by [#114: M18.4 — Add demotion grace, interaction pins, and network notifications](https://github.com/wnj00524/proxystate/issues/114). This predecessor must be Done before implementation starts.

## In scope

- Extend data/lod.json with workday and nonWorkday segment definitions.
- Each segment has a stable id, existing intent id, home/work symbolic location, fixedMinutes or fillRemaining, and explicit initiator/participant effect role.
- Add trait duration modifiers keyed by trait and segment id.
- Compile intent/trait references to hashes, masks, indexes, and generic enums.
- Validate coverage, ordering, duration, fill, effect role, job interval, and commute feasibility.

## Explicitly out of scope

- Do not build per-agent profiles or apply effects.
- Do not branch runtime code on work/rest/socialize IDs.
- Do not enable Tier 3.

## Likely code, data, and documentation areas

data/lod.json, ContentCatalog/compiler types, validation tests, docs/editing-data.md, docs/datastructs.md.

## Ordered implementation steps

1. Author production workday and non-workday routines using current intents.
2. Insert scheduled job work and commute through generic segment kinds, not action-name checks.
3. Resolve all identifiers during catalog load.
4. Reject unknown references, negative durations, duplicate segment IDs, missing/multiple fill segments, invalid subject roles, overlap, and uncovered minutes.
5. Document examples and validation messages for content authors.

## Required tests

- [ ] Production content validation.
- [ ] Every malformed category above with file/segment path in the error.
- [ ] Data-only use of a different intent in a coarse segment.

## Acceptance criteria

- [ ] Runtime routine evaluation performs no string parsing.
- [ ] A valid routine covers every minute of each day after job/commute insertion.
- [ ] No code knows the domain meaning of a named intent ID.

## Handoff

M19.2 compiles these definitions into shared seven-day profiles.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#116: M19.2 — Build shared deterministic weekly routine profiles](https://github.com/wnj00524/proxystate/issues/116)

**Tracking:** Milestone 19: Data-Driven Coarse Agent Routines · Priority P1 · Size L · Initial status Backlog

## Goal

Compile reusable seven-day itineraries without allocating segment arrays per Tier 3 agent.

## Prerequisites

Blocked by [#115: M19.1 — Compile and validate Tier 3 routine definitions](https://github.com/wnj00524/proxystate/issues/115). This predecessor must be Done before implementation starts.

## In scope

- Create a shared profile cache keyed by occupation hash, trait mask, commute duration, LOD content revision, and world-topology revision.
- Expand job workDays/work intervals, commute segments, fixed segments, fill segments, and trait adjustments into seven deterministic days.
- Store only profile ID, fingerprint, and coarse watermark per agent.
- Provide allocation-conscious segment lookup and overlap enumeration.

## Explicitly out of scope

- Do not mutate agent attributes.
- Do not promote/demote components.
- Do not add per-agent random schedules.

## Likely code, data, and documentation areas

new Simulation coarse-routine compiler/cache, WorldTopology integration, profile tests, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Define immutable compiled segment/profile structures using hashes and numeric indexes.
2. Calculate commute duration from the existing shortest route and cache profiles shared by identical keys.
3. Expand all seven days in stable authored order and verify exact minute coverage.
4. Create a material fingerprint from job, traits, home/work assignment, and content/topology revisions.
5. Expose current-segment and interval-overlap APIs.

## Required tests

- [ ] Office/shop schedules and workdays.
- [ ] Trait-adjusted duration profiles.
- [ ] Identical agents share a profile; material differences do not.
- [ ] Week wrap, midnight, commute, and exact boundary lookup.

## Acceptance criteria

- [ ] Profile output is deterministic for identical inputs.
- [ ] Persistent profile storage grows with unique profiles, not population.
- [ ] No Tier 3 agent owns a segment or route array.

## Handoff

M19.3 uses profile overlap enumeration to apply coarse effects in deterministic shards.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#117: M19.3 — Implement sharded Tier 3 effect integration](https://github.com/wnj00524/proxystate/issues/117)

**Tracking:** Milestone 19: Data-Driven Coarse Agent Routines · Priority P1 · Size XL · Initial status Backlog

## Goal

Advance Tier 3 attributes cheaply and correctly over arbitrary elapsed simulation intervals.

## Prerequisites

Blocked by [#116: M19.2 — Build shared deterministic weekly routine profiles](https://github.com/wnj00524/proxystate/issues/116). This predecessor must be Done before implementation starts.

## In scope

- Maintain 24 compact Tier 3 agent-ID shards in AgentLodService.
- Process one deterministic shard per simulated hour.
- Integrate existing compiled action effect rates over profile segment overlap.
- Respect initiator/participant subject selection and schema min/max clamping.
- Catch up any specific Tier 3 agent synchronously before its state is read by detailed targeting or promotion.
- Support skipped hours, multi-day jumps, and week wrap using each agent's watermark.

## Explicitly out of scope

- Do not add investigation UI.
- Do not yet remove detailed components from production-spawned agents.
- Do not simulate Tier 3 coordination or relationship discovery.

## Likely code, data, and documentation areas

new coarse routine system, AgentLodService shard storage, effect integration helpers, time-jump tests, docs/coreecs.md.

## Ordered implementation steps

1. Assign Tier 3 candidates to a stable shard from agent identity/ordinal.
2. Iterate only the scheduled shard IDs rather than scanning all agents.
3. Rebuild a profile when the material fingerprint changes before integrating further.
4. Apply each overlapped effect once using elapsed simulated minutes.
5. Update AgentLocation to the effective symbolic location when a coarse catch-up occurs.

## Required tests

- [ ] Single segment, multiple segments, midnight, multi-day, and full-week integration.
- [ ] Effect subject selection and clamping.
- [ ] Material fingerprint rebuild.
- [ ] Shard membership after tier change/deletion.
- [ ] Catch-up equivalence with frequent incremental updates.

## Acceptance criteria

- [ ] Coarse results are independent of rendering frame rate and shard processing frequency.
- [ ] Each inactive Tier 3 agent is visited approximately once per day.
- [ ] No coarse update runs utility decisions, coordination, or edge discovery.

## Handoff

M19.4 will use the same catch-up and segment lookup to move agents between coarse and detailed representations.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#118: M19.4 — Materialize promotion and demotion state](https://github.com/wnj00524/proxystate/issues/118)

**Tracking:** Milestone 19: Data-Driven Coarse Agent Routines · Priority P1 · Size XL · Initial status Backlog

## Goal

Transition agents between coarse and detailed representations without stale attributes or nonsensical location/activity state.

## Prerequisites

Blocked by [#117: M19.3 — Implement sharded Tier 3 effect integration](https://github.com/wnj00524/proxystate/issues/117). This predecessor must be Done before implementation starts.

## In scope

- On promotion, catch up coarse effects through the current minute and reconstruct current activity, location, and commute progress.
- Allocate IntentionState, ActivityState, DecisionState, AgentTravel, and CoordinationState only when entering detailed simulation.
- Force an immediate full decision after materialization while retaining the reconstructed public activity for the transition tick.
- On demotion, integrate detailed effects through the boundary, select/rebuild the coarse profile, remove detailed-only components, and enter the current itinerary segment.
- Keep AgentLodState and common identity/attribute/location state on every agent.

## Explicitly out of scope

- Do not expose LOD internals to PlayerIntelligenceDB.
- Do not enable Tier 3 globally until M19.5.
- Do not serialize per-agent route arrays in coarse state.

## Likely code, data, and documentation areas

AgentLodService transitions, spawner component ownership, WorldTopology route reconstruction, debug projection handling, transition tests, docs/datastructs.md.

## Ordered implementation steps

1. Define common versus detailed-only component sets in one service.
2. Queue archetype changes outside active ECS query iteration.
3. Materialize stationary and in-progress commute segments from shared route data.
4. Initialize decision caches to catalog size only on promotion.
5. Remove an agent from/add it to coarse shards atomically with tags/components.
6. Make debug snapshots tolerate and describe both representations.

## Required tests

- [ ] Promotion during rest, work, social, outbound commute, and return commute.
- [ ] Demotion at day boundary, while moving, and after coordination release.
- [ ] Repeated promotion/demotion without duplicate components or stale shard IDs.
- [ ] Attributes match coarse catch-up immediately before promotion.

## Acceptance criteria

- [ ] Tier 3 owns no decision cache, coordination state, or route array.
- [ ] Every transition leaves exactly one tier tag and correct DetailedSimulationTag state.
- [ ] Promotion produces a valid detailed agent before the next decision/coordination phase.

## Handoff

M19.5 enables Tier 3, integrates entity-target promotion, and runs the complete lifecycle suite.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#119: M19.5 — Enable Tier 3 and verify cross-tier coordination](https://github.com/wnj00524/proxystate/issues/119)

**Tracking:** Milestone 19: Data-Driven Coarse Agent Routines · Priority P1 · Size L · Initial status Backlog

## Goal

Activate the complete three-tier pipeline and make detailed-to-coarse interactions safe.

## Prerequisites

Blocked by [#118: M19.4 — Materialize promotion and demotion state](https://github.com/wnj00524/proxystate/issues/118). This predecessor must be Done before implementation starts.

## In scope

- Set tier3Enabled=true in production lod.json.
- Spawn common state first, build networks/indexes, classify, and allocate detailed components only for Tier 1/2.
- Before evaluating a mutual invitation to Tier 3, catch it up and promote it to Tier 2 with an ActiveInteraction pin.
- Release pins through normal coordination cleanup and retain end-of-day demotion grace.
- Run end-to-end deterministic lifecycle and behavior tests.

## Explicitly out of scope

- Do not implement the player-facing investigation toggle.
- Do not replace PlayerIntelligenceDB capture yet.
- Do not change default population size.

## Likely code, data, and documentation areas

Program.cs/system ordering, AgentSpawner.cs, CoordinationSystem.cs, production lod.json, integration tests, docs/coreecs.md.

## Ordered implementation steps

1. Order clock, LOD lifecycle, coarse updates, decisions, target promotions, coordination, execution, effects, and interaction explicitly.
2. Ensure target promotion completes before participant acceptance reads detailed components.
3. Verify agents unrelated to POIs start Tier 3 without detailed allocations.
4. Compare Tier 1 traces with pre-LOD fixtures and Tier 2 cadence with M18 fixtures.
5. Document the enabled pipeline and its transition invariants.

## Required tests

- [ ] Tier 1 invitation to Tier 2.
- [ ] Tier 2 invitation to Tier 3 with promotion.
- [ ] Rejection, release, deletion, unreachable travel, and day-boundary demotion.
- [ ] Fixed-seed spawn tier counts and component ownership.

## Acceptance criteria

- [ ] The production simulation uses all three tiers.
- [ ] Tier 1 behavior remains unchanged.
- [ ] Cross-tier coordination never reads missing detailed components.
- [ ] Tier counts and results are deterministic for a fixed seed.

## Handoff

M20.1 can now replace the per-frame intelligence capture and connect safe investigation commands to the completed backend.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#120: M20.1 — Add incremental player-intelligence projection and investigation commands](https://github.com/wnj00524/proxystate/issues/120)

**Tracking:** Milestone 20: Interactive 100,000-Agent Delivery · Priority P1 · Size XL · Initial status Backlog

## Goal

Remove per-frame full-population intelligence copies while preserving the Intelligence Isolation Layer.

## Prerequisites

Blocked by [#119: M19.5 — Enable Tier 3 and verify cross-tier coordination](https://github.com/wnj00524/proxystate/issues/119). This predecessor must be Done before implementation starts.

## In scope

- Replace PlayerIntelligenceDB.Capture-per-frame with a long-lived projection containing copied stable identity data and mutable sanitized fields only.
- Feed known-trait-mask updates from discovery events limited to Operative knowledge.
- Add InvestigationCommand(agentId, enabled) and process it before LOD lifecycle updates.
- Expose IsUnderInvestigation in the player snapshot; do not expose LOD tier/reasons.
- Use binary/indexed lookup without rebuilding a dictionary each frame.

## Explicitly out of scope

- Do not virtualize ImGui rows yet.
- Do not expose Psychology, networks, coordination, Entity, or mutable component references.
- Do not make the UI call AgentLodService directly.

## Likely code, data, and documentation areas

IntelligenceDossiers.cs, InteractionSystem event boundary, Program.cs command queue, isolation tests, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Build the stable identity projection once after spawn.
2. Publish sanitized trait-discovery deltas when Operative outgoing masks change.
3. Queue investigation commands from presentation and apply them through a simulation-owned adapter.
4. Update only affected projection entries after accepted commands/events.
5. Remove per-frame store/edge enumeration from the normal rendering loop.

## Required tests

- [ ] Union of multiple Operative masks.
- [ ] Incremental discovery and investigation updates.
- [ ] Rejected/missing agent commands.
- [ ] Reflection/architecture tests forbidding Ground Truth fields and Entity references.
- [ ] Work counters proving no per-frame population/edge capture.

## Acceptance criteria

- [ ] Player-facing state remains a copied projection.
- [ ] A normal frame performs no full agent or edge intelligence scan.
- [ ] Investigation status becomes visible after the command is applied without leaking LOD state.

## Handoff

M20.2 consumes this projection through search and clipped visible-row rendering.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#121: M20.2 — Virtualize dossier and debug inspection](https://github.com/wnj00524/proxystate/issues/121)

**Tracking:** Milestone 20: Interactive 100,000-Agent Delivery · Priority P1 · Size L · Initial status Backlog

## Goal

Keep the interactive UI responsive without constructing or drawing full-population detail every frame.

## Prerequisites

Blocked by [#120: M20.1 — Add incremental player-intelligence projection and investigation commands](https://github.com/wnj00524/proxystate/issues/120). This predecessor must be Done before implementation starts.

## In scope

- Add agent ID/name search whose filtered index is rebuilt only when search input or stable identities change.
- Use ImGuiListClipper so only visible dossier rows are formatted/drawn.
- Add Investigate/End Investigation controls that emit InvestigationCommand; Operatives display permanent-detail status.
- Replace debug full-snapshot capture with virtualized identity rows and an on-demand copied selected-agent snapshot.
- Show LOD tier/pending demotion/coarse profile only in debug projections.

## Explicitly out of scope

- Do not let ImGui query EntityStore or retain Entity values.
- Do not expose debug LOD fields through PlayerIntelligenceDB.
- Do not redesign the Windows 3.1 visual theme.

## Likely code, data, and documentation areas

IntelligenceDossiers.cs, DebugInspection.cs, ApplicationShell.cs, Program.cs, UI pure-helper tests, docs/coreecs.md.

## Ordered implementation steps

1. Separate list metadata from selected-agent detail projections.
2. Cache search results and invalidate them only on relevant copied-data changes.
3. Use clipping around row iteration and stable IDs for selection.
4. Route investigation buttons through the command callback.
5. Capture one selected debug agent on demand and handle both coarse/detailed representations.

## Required tests

- [ ] Search/filter stability and selection retention.
- [ ] Visible-row visit counts for 100,000 copied identities.
- [ ] Investigation button command output.
- [ ] Debug/player projection field isolation.
- [ ] Missing/deleted selection behavior.

## Acceptance criteria

- [ ] Closed or idle windows do not build full detail snapshots.
- [ ] Open lists visit only filtered visible rows per frame.
- [ ] The UI contains no Ground Truth query path.

## Handoff

M20.3 adds the population option and records complete end-to-end scale results.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---

### [#122: M20.3 — Add population CLI and complete 100k verification](https://github.com/wnj00524/proxystate/issues/122)

**Tracking:** Milestone 20: Interactive 100,000-Agent Delivery · Priority P1 · Size XL · Initial status Backlog

## Goal

Make 100,000 agents a supported, reproducible configuration and publish final evidence.

## Prerequisites

Blocked by [#121: M20.2 — Virtualize dossier and debug inspection](https://github.com/wnj00524/proxystate/issues/121). This predecessor must be Done before implementation starts.

## In scope

- Add validated --agents <count> parsing while keeping the default at 1,000.
- Run Release generation, classification, simulated-week, promotion/demotion, projection, UI work-count, allocation, and memory benchmarks at 1,000, 10,000, and 100,000.
- Fix only measured blockers that violate roadmap structural guarantees.
- Record host/runtime/results and compare with M17.1.
- Complete README, workplan, core ECS, data structures, LOD authoring, and decision baseline documentation.

## Explicitly out of scope

- Do not silently change the normal default to 100,000.
- Do not add platform-specific wall-time pass/fail thresholds.
- Do not broaden feature scope beyond measured LOD/index/projection/UI scale blockers.

## Likely code, data, and documentation areas

Program.cs argument parsing, performance tests, docs/decisionbaseline.md, README.md, docs/workplan.md, docs/coreecs.md, docs/datastructs.md.

## Ordered implementation steps

1. Add pure argument parsing with positive bounded integer validation and helpful errors.
2. Warm up and run the same deterministic benchmark scenario at all three populations.
3. Record elapsed time, allocations, peak/process memory, agent/tier counts, decision evaluations, target/edge visits, coarse visits, and UI visible-row visits.
4. Verify steady detailed work follows Tier 1/2 counts and Tier 3 has no detailed allocations.
5. Run full tests/content validation and perform a final documentation/acceptance audit.

## Required tests

- [ ] Default, valid override, missing value, nonnumeric, zero, negative, and excessive population arguments.
- [ ] All focused LOD, isolation, and UI scaling suites.
- [ ] Release benchmarks for the three required populations.
- [ ] Full solution tests and production content validation.

## Acceptance criteria

- [ ] The application can launch and interact with 100,000 agents using the documented option.
- [ ] No per-frame full-population target, edge, intelligence, or UI detail snapshot remains.
- [ ] Benchmark results and limitations are published and reproducible.
- [ ] Every epic acceptance item is verified or linked to a documented follow-up issue.

## Handoff

Close Milestone 20 and the parent epic only after this issue is merged, all child issues are Done, and the final acceptance audit passes.

## Verification

- [ ] Run `dotnet test ProxyState.sln`.
- [ ] Run the focused tests listed above.
- [ ] Run `dotnet run -- --validate-content` when JSON contracts or data changed.
- [ ] Update `docs/coreecs.md` when ECS behavior changed.
- [ ] Update `docs/datastructs.md` when structures changed.
- [ ] Confirm player-facing UI still receives no Ground Truth state.

## Completion

- [ ] Acceptance criteria are satisfied.
- [ ] No unrelated files were changed.
- [ ] Fixed-seed tests remain deterministic.
- [ ] The linked PR is merged.
- [ ] PState status is Done.
- [ ] The next dependency is moved to Ready.

---


## 6. Testing matrix

| Concern | Owning issue(s) | Required evidence |
| --- | --- | --- |
| Existing behavior and scale baseline | [#107](https://github.com/wnj00524/proxystate/issues/107) | 126-test starting baseline, deterministic counters, Release observations at 1k/10k/100k. |
| Packed relationship/agent lookup | [#108](https://github.com/wnj00524/proxystate/issues/108) | Deterministic layout, complete edge coverage, bounded enumeration, safe misses. |
| Target semantics and allocation removal | [#109](https://github.com/wnj00524/proxystate/issues/109) | All selectors, target facts, stable ties, unchanged decision traces. |
| Execution/interaction scaling | [#110](https://github.com/wnj00524/proxystate/issues/110) | Direct target lookup, indexed discovery, population-independent work counts. |
| LOD content/contracts | [#111](https://github.com/wnj00524/proxystate/issues/111) | Valid JSON and path-specific malformed-content failures. |
| Classification/investigation | [#112](https://github.com/wnj00524/proxystate/issues/112) | Operative/POI/direct-neighbour/two-hop/coworker/overlap/deletion cases. |
| Tier 2 cadence | [#113](https://github.com/wnj00524/proxystate/issues/113) | Minute 1–59 suppression, minute 60 pass, critical wakes, continuous execution. |
| Grace, pins, network changes | [#114](https://github.com/wnj00524/proxystate/issues/114) | Day-boundary demotion, active pair retention, supervisor changes, deletion. |
| Routine validation | [#115](https://github.com/wnj00524/proxystate/issues/115) | References, coverage, duration, fill, roles, commute, data-only action. |
| Weekly profiles | [#116](https://github.com/wnj00524/proxystate/issues/116) | Shared keys, material differences, boundaries and commute lookup. |
| Coarse integration | [#117](https://github.com/wnj00524/proxystate/issues/117) | Overlap, clamping, week wrap, jumps, shards and catch-up equivalence. |
| Transitions | [#118](https://github.com/wnj00524/proxystate/issues/118) | Activity/commute promotion, boundary demotion, component/tag/shard invariants. |
| Cross-tier coordination | [#119](https://github.com/wnj00524/proxystate/issues/119) | Target promotion and accept/reject/release/delete/unreachable paths. |
| Intelligence isolation | [#120](https://github.com/wnj00524/proxystate/issues/120) | Incremental updates and forbidden-field reflection tests. |
| UI scaling | [#121](https://github.com/wnj00524/proxystate/issues/121) | Search/selection, visible-row counts, commands, selected debug snapshot. |
| Final 100k delivery | [#122](https://github.com/wnj00524/proxystate/issues/122) | CLI validation, three-scale results, memory/allocation/work counters, audit. |

After every issue:

~~~powershell
dotnet test ProxyState.sln
~~~

After JSON contracts or production data change:

~~~powershell
dotnet run -- --validate-content
~~~

Run Release-scale benchmarks only in #107 and #122 unless another card explicitly requires a focused scaling assertion. Do not add unstable platform-specific wall-time gates; assert deterministic work counts and structural allocation rules.

## 7. Agent workflow

1. Read the complete task card and predecessor issue.
2. Confirm the predecessor is **Done** and the issue is **Ready**.
3. Move the issue to **In progress** before changing files.
4. Set and verify the repository-local Git author name using the full model name.
5. Start from current main on a codex/ branch unless instructed otherwise.
6. Inspect current implementation and tests; do not assume code has not changed.
7. Implement only the In scope steps. Do not pull later work forward.
8. Comment non-obvious logic for a human reviewer.
9. Update docs/coreecs.md for ECS logic and docs/datastructs.md for structures.
10. Run focused tests, full solution tests, and content validation when applicable.
11. Review the diff and preserve user-owned files.
12. Commit, push, open a linked PR, and move the issue to **In review**.
13. After merge, verify acceptance, close the issue, and move it to **Done**.
14. Move only the next native dependency from **Backlog** to **Ready**.

If blocked, record the exact failing command, error, and investigation. Never weaken determinism, data-driven content, or intelligence isolation to bypass a blocker.

## 8. Completion definition

The epic completes only when all sixteen sub-issues are Done, Milestones 17–20 are closed, 100,000-agent evidence is published, production content validates, the full test suite passes, and every item in [Epic #106](https://github.com/wnj00524/proxystate/issues/106) is satisfied.
