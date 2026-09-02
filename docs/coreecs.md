# 4. Core ECS Systems (Logic)

### 4.0 Tier 3 shared routine profiles

`CoarseRoutineProfileCache` is simulation-owned immutable schedule storage. It
keys profiles by occupation, trait mask, and commute duration (with its
catalog/topology cache lifetime acting as the revision boundary) and expands
the compiled workday/non-workday templates into one contiguous seven-day
sequence. Job intervals and exact route duration reserve work and commute
blocks; authored fixed blocks use stable order and `fillRemaining` owns every
unreserved minute. Agents retain only an ID and fingerprint in `AgentLodState`;
profiles and their interval arrays are never copied per agent.

`CoarseRoutineSystem` owns 24 stable agent-ID shards. Each simulated hour it
updates one shard and integrates resolved intent effect rates over exact
profile overlaps, including week wrap and long jumps. It clamps attributes to
the schema range, sets the current symbolic location, and records a watermark.
`AgentLodService` is the only representation-transition boundary: Tier 3
removes decision, activity, coordination, and route-array components; a
promotion catches up first and recreates those components from shared route
data before the detailed systems can read them.

### 4.1 Utility AI System

**Goal:** Decide an intention from Ground Truth on simulation time.

* Content loading compiles every utility expression in `actions.json` to a
  bounded postfix opcode program. Fact strings are resolved once to typed
  `FactId` handles, including direct schema indexes for agent attributes.
  Eligibility predicates are likewise compiled from boolean facts, boolean
  combinators, and numeric comparisons. `AgentDecisionSystem` evaluates only
  these pre-resolved programs; it contains no named eligibility-gate switch.
  Current data preserves schedule/workday, home-route, and co-located-peer rules.
* Eligible utility is weighted-additive: base utility plus each compiled numeric
  expression passed through its piecewise-linear response curve, plus applicable
  trait modifiers. Low wealth, schedule pressure, night time, and peer affinity
  are compositions in data rather than semantic source cases in runtime code.
  Numeric and predicate evaluation use fixed stack spans and direct fact access with no runtime
  parsing, casing, string comparison, or expression allocation.
* Every action declares a `none`, `location`, or `entity` target. Direct location
  values resolve home, work, or current location without action-ID branches.
  Entity queries traverse a compiled social, network-member, network-supervisor,
  or network-direct-report relation, evaluate compiled target requirements,
  rank candidates lexicographically, and break exact ties by ascending entity
  ID. Target attribute facts use schema indexes; affinity comes from the
  directional edge or is neutral for network members without an edge.
* The highest score wins. Exact ties use ascending stable action hash, never
  JSON or query order. A switch additionally pays the configured switching
  threshold unless its score reaches the urgent-preemption threshold.
* Per-action minimum commitment, cooldown-on-exit, and urgent preemption
  controls prevent oscillation. Decisions run at most once per simulated
  minute; `DecisionState.Dirty` permits earlier event-driven reconsideration.
  Travel arrival marks the decision dirty.
* Deliberation writes `IntentionState`. `IntentExecutionSystem` translates each
  content-defined executor into generic travelling, performing, or idle state,
  and `ActivityEffectsSystem`
  applies data-defined attribute rates using elapsed simulation minutes.
* `AgentState.SecretStateHash` is neither read nor written by this pipeline, so
  covert state remains independent of public intentions and activities.
* Milestone 6 locks the original semantics with deterministic Ground Truth fixtures and
  a test-only immutable decision trace. The measured 1,000-agent Release
  baseline, allocation method, and current intent-ID architectural debt are
  recorded in `docs/decisionbaseline.md`. Milestone 9 preserves that baseline
  while representing score, eligibility, entity target, and location target in
  one candidate result whose winner is copied without domain-specific branches.

### 4.2 Interaction & Discovery System

**Goal:** Handle target interrogation/surveillance based on Perception vs Willpower.

* `SocialGraphBuilder` creates a randomized simple graph with at least five peers per agent (or the largest valid degree for smaller populations), then adds reciprocal clique edges for every family and friend group whose type enables `seedsSocialGraph`. Duplicate directed pairs are suppressed.
* `InteractionSystem` runs on every 60th ECS update by default. The interval is injectable for tests and future tuning. On an interval it queries eligible detailed source agents and enumerates only each source's packed outgoing adjacency range; it no longer scans every `EdgeData` entity.
* Each edge performs an opposed d100 contest: `Source` rolls d100 plus schema-defined `perception`; `Target` rolls d100 plus schema-defined `willpower`, with a 20-point bonus when the target has the `Paranoid` trait.
* On a source victory, one present and not-yet-known target trait is selected and revealed with bitwise `OR` on `EdgeData.KnownTraitMask` (for example, `KnownTraitMask |= 0x0004`).
* Reciprocal edges discover independently because each direction owns a separate knowledge mask.
* Recalculate `Affinity` as the number of shared known trait bits divided by the configured trait count and scaled to `0` through `100`.
* `KnownStatsMask` and `KnownPoliticalMask` remain reserved and unchanged in this milestone.

### 4.3 Fatigue and Stress System (Milestone 1)

**Goal:** Advance the short-term state of active agents every simulation tick.

* Query entities with `AgentAttributes` and `Tier1LodTag` using Friflo's `QuerySystem`.
* Resolve `fatigue` and `stress` indexes from `AgentAttributeSchema`.
* Increase both values by `0.1` per update by default.
* Reset each value independently to `0` when its updated value reaches or exceeds `100`.
* Entities without `Tier1LodTag` are excluded, leaving Tier 2 and Tier 3 agents for later systems.
* The application executes the system through `SystemRoot.Update(default)` before the Raylib rendering phase.

The system is configured with an optional positive per-tick increase so simulation tests can use a larger value without changing production defaults.

### 4.4 World Clock System (Milestone 2)

**Goal:** Advance a shared world calendar independently of rendering frame rate.

* Store one `WorldTime` component as the world-time singleton.
* Convert real elapsed seconds to simulation seconds using `600` real seconds per in-world day by default.
* Keep the last simulation delta on `WorldTime` so time-based systems consume the same elapsed interval.
* Job schedules use Monday as day `1`, integer minutes from midnight, and non-overnight intervals.

### 4.5 Intent Execution System (Milestones 2, 10, and 11)

**Goal:** Execute any Tier 1 intention through reusable movement and performance mechanics.

* Each action declares `performHere`, `performAtLocation`, `performWithEntity`,
  or `wait`; loading resolves that string to `ExecutorKind` and validates that
  its target type and `intent.target` destination are compatible.
* Location and entity executors share deterministic shortest-route traversal;
  the movement path contains no action IDs.
* Execution copies the action's data-defined activity hash into `ActivityState`.
  `ActivityPhase` contains only generic Idle, Moving, Waiting, Performing, and
  Blocked engine phases. Waiting reserves a mutual participant in place while
  the initiator travels; arrival changes both partners to Performing.
  A missing entity or unreachable destination blocks the actor and dirties its
  decision for reconsideration.
* `ActivityEffectsSystem` applies effects only for a Performing state whose
  action/activity hash pair matches the loaded content definition.

### 4.6 Debug Agent Inspector

**Goal:** Provide an opt-in development view of the complete simulated agent population.

* Debug mode is enabled only when the process receives the `-debug` command-line argument (case-insensitive).
* `DebugSnapshotBuilder` copies the current agent component values into immutable, UI-facing snapshots once per frame.
* The `Debug` ImGui window lists every agent and lets the user select one to inspect identity, faction, job, attributes, all trait states, current action, catalog-resolved activity identity and execution phase, secret state, locations, and travel state.
* The ImGui layer consumes snapshots only; it never queries or mutates the Ground Truth ECS store.

### 4.7 World-Time Status Bar

**Goal:** Keep the current in-game calendar visible in every application mode.

* After the ECS update, the bootstrapper copies the singleton `WorldTime` values into a `WorldTimeSnapshot`.
* The shared ImGui phase renders `WorldTimeBar` after the normal and optional debug windows, so the same bar appears in both modes.
* The bar is pinned to the bottom edge of the ImGui viewport and displays the simulation day, weekday, and time of day.
* Formatting uses only the copied snapshot; it never reads the local system clock or the Ground Truth ECS store.

### 4.8 Applications Launcher and Window Navigation

* `ApplicationShell` renders the `Applications` program-manager window and exposes only the applications allowed by the process mode: `Dossiers` always appears, while `Debug Window` appears only with `-debug`.
* Application tiles select on a single click and launch on a left-button double-click. The launcher stores only presentation state for whether the `Surveillance Terminal` or `Debug Window` is open.
* The dossier view is titled `Surveillance Terminal`; the debug inspector is titled `Debug Window`. Both are independently closable from their ImGui title bars.
* The launcher and navigation state never query the ECS store. Debug content continues to arrive through immutable `DebugAgentSnapshot` values captured at the ECS boundary.

### 4.9 Operative Intelligence Dossier

**Goal:** Present the player's team-level intelligence without exposing Ground Truth to ImGui.

* The spawner marks five distinct randomly selected agents with `OperativeTag`, or marks every agent when the population is smaller than five.
* Each selected Operative receives `Identity.IntelligenceRole = Officer`; all other spawned agents receive `IntelligenceRole = None`. `Agent` and `Informant` remain available for future assignment systems.
* `PlayerIntelligenceDB.Capture` copies all agent identity metadata and combines outgoing `EdgeData.KnownTraitMask` values from Operatives with bitwise `OR` for each target.
* The `Surveillance Terminal` consumes only the immutable database and static trait definitions. It never reads `Entity`, `Psychology`, or another Ground Truth component; intelligence roles are copied into the database at the ECS/UI boundary.
* Dossier trait visibility is resolved with `(knownMask & trait.Bit) != 0`; hidden traits render `Trait: ???`, while known traits render their configured names.

### 4.10 Agent Secret States

**Goal:** Represent covert activity independently from an agent's visible action.

* `ContentCatalog` loads and validates `data/secret-states.json` definitions.
* The catalog requires a unique `none` definition with hash `0`; spawned agents
  begin in that state.
* `AgentState.SecretStateHash` can identify a covert activity such as
  `Surveillance` while `CurrentActionHash` remains `Work`, `Rest`, or another
  public action.
* Secret state is copied only into `DebugAgentSnapshot`; the player dossier does
  not receive or display it.

### 4.11 Agent Network Service (Milestone 5)

**Goal:** Preserve flat-family and single-supervisor company invariants at one
runtime mutation boundary.

* `AgentNetworkService.CreateNetwork` creates an `AgentNetworkData` entity only
  after resolving its static type hash through `AgentNetworkCatalog`.
* Memberships are native Friflo link relations from agents to network entities.
  The service verifies both endpoints, role ownership, type-level cardinality,
  and uniqueness before adding one.
* Flat networks reject supervisors. Hierarchical networks have one root, require
  one same-network supervisor for every non-root, reject self-supervision, and
  walk the supervisor chain before a change to prevent cycles.
* Manager removal reparents direct reports to the removed manager's supervisor.
  Root removal requires a direct-report successor; deletion cleanup selects the
  lowest-ID direct report when external agent deletion makes explicit selection
  impossible.
* The service subscribes to ECS entity deletion events because Friflo cleans up
  the keyed `Network` link but cannot clean the non-key `Supervisor` field.
  Network deletion first removes all incoming membership relations.
* Every membership, role, supervisor, or network deletion mutation invalidates
  target availability and active coordination for all affected members.

### 4.12 Deterministic Agent Network Generation

**Goal:** Generate location-coherent families and bounded company hierarchies
without coupling unrelated replay outcomes.

* `AgentSpawner` derives separate population, Operative, network, and social
  graph random streams from one injected seed. Network content changes can
  consume different random values without changing assignments, Operatives, or
  social peers.
* Network generation runs after every agent has home and work assignments and
  before `SocialGraphBuilder` creates interpersonal edges.
* `AgentNetworkBuilder` buckets agents by the generator's registered home-,
  work-, or global-partition strategy, sorts buckets by location hash, shuffles each bucket,
  samples configured weighted sizes, and consumes every member exactly once.
  Each resulting network is anchored to its bucket location.
* Families are flat synthetic groupings whose members all use the configured
  member role and have no supervisor. Generation intentionally adds no inferred
  genealogy, ages, or surnames.
* Friend groups are flat, town-wide partitions anchored at location `0`.
  Weighted target sizes are redistributed deterministically so the final group
  remains within the authored three-to-six-member bounds.
* Companies are built breadth-first. Children are distributed evenly across a
  level up to the configured target span; validated size capacity guarantees
  the maximum depth is sufficient. The root is the only head, non-root agents
  with reports become managers, and leaves remain employees. A one-person
  company consists only of a supervisor-less head.
* All entity and relation creation goes through `AgentNetworkService`, so
  generated data is subject to the same role, cardinality, supervisor, and
  cycle invariants as future runtime mutations.

### 4.13 Agent Network Query and Inspection Costs

**Goal:** Inspect network ground truth without adding persistent indexes or
leaking ECS entities into ordinary presentation code.

* An agent's outgoing `GetRelations<AgentNetworkMembership>()` enumerates its
  network degree in `O(d)`; keyed `TryGetRelation` retrieves one membership in
  `O(d)` with the current compact relation representation.
* A network's incoming `GetIncomingLinks<AgentNetworkMembership>()` enumerates
  only its `k` members in `O(k)`. Direct reports are filtered from those members
  in `O(k)`; a management-chain walk follows supervisors in `O(depth)`.
* Network-wide work visits packed incoming relation pairs once, for `O(M)` total
  memberships. `DebugSnapshotBuilder.CaptureInspection` follows this path and
  creates transient immutable agent-membership and network-summary projections.
* No persistent member list, descendant closure, or manager-keyed reverse
  relation is maintained. This keeps storage `O(M)` and avoids redundant ECS
  indexes until profiling demonstrates a need.
* `DebugWindow` receives only `DebugInspectionSnapshot`. Network type/role names,
  anchor names, supervisor display names, and counts are resolved at the
  Ground Truth boundary; `PlayerIntelligenceDB` and dossiers remain unchanged.

### 4.14 Intent Compilation and Fallback (Milestone 12)

* `ContentCatalog.Load` validates authoring data and invokes `IntentCompiler`
  before constructing the runtime catalog. String references never reach the
  decision, target-resolution, execution, or effects hot paths.
* Dense runtime indexes follow deterministic JSON order while stable hashes
  remain the ECS, persistence, debug, and content identity.
* Decision evaluation excludes the fallback from normal scoring and selects it
  only when no ordinary intent is eligible. The fallback is structurally
  constrained to a target-free `Wait`, so every agent always has a safe result.
* Spawning initializes intention and activity state from the compiled fallback,
  eliminating the former dependency on a specifically named domain action.

### 4.14a Agent LOD Classification and Cadence (Milestones 18.1–18.4)

* `ContentCatalog` loads `data/lod.json` and compiles its relationship scopes
  and demotion policy to enums. Positive cadence/shard values and exact supported
  tokens are rejected with JSON-path-specific validation errors.
* The spawner initializes every agent's LOD state, then invokes classification
  only after networks, social edges, and the packed relationship indexes exist.
  Operatives and investigated agents are Tier 1. Their direct social neighbours,
  supervisors, and reports are Tier 2; coworkers and two-hop contacts do not
  expand the frontier.
* Exactly one of the three tier tags is valid. `AgentLodService` is the sole tag
  mutation boundary and synchronizes `DetailedSimulationTag` for Tier 1/2.
* The service reference-counts overlapping POI neighbourhoods. Investigation
  commands are ID-based and idempotent, update affected neighbours immediately,
  and emit copied `InvestigationChangedEvent` values without exposing entities.
  Deleting a POI releases its contribution to every surviving neighbour.
* Tier 3 remains disabled in the transitional content. A desired Tier 3 request
  is retained in state but materializes as detailed Tier 2 until rollout.
* Both detailed tiers continue executing movement, activity effects, and
  coordination on every elapsed simulation update. Tier 1 deliberation retains
  its minute and dependency-driven behavior; Tier 2 deliberates on its
  data-defined 60-minute cadence instead of on ordinary dirty signals.
* Target loss, coordination lifecycle changes, investigation changes, and
  promotion set explicit wake reasons. They force an immediate full Tier 2
  cache refresh, while ordinary dependency masks accumulate until the next
  cadence boundary. Scheduling is based only on elapsed simulation minutes.
* Promotions materialize immediately. Reductions retain the earliest queued
  next-day boundary and are applied before the detailed decision pass. The
  desired tier remains visible while grace is pending; disabled Tier 3 still
  materializes as Tier 2 after that boundary.
* Coordination owns reference-counted interaction pins for both members of an
  accepted pair. A pin keeps an agent at least Tier 2 across day boundaries;
  releasing the final pin starts a fresh normal demotion grace period.
* `AgentNetworkService` notifies the LOD boundary after supported hierarchy
  additions, supervisor changes, removals, and deletion cleanup. The service
  diffs only affected cached POI neighbourhoods, preserving overlap reference
  counts without treating ordinary shared network membership as interest.

### 4.15 Dependency-Driven Reevaluation (Milestone 13)

* Compilation unions every fact read by eligibility predicates, utility
  expressions, trait modifiers, and target queries into each intent's
  `FactDependencyMask`. Attribute dependencies retain their schema index bits.
* Mutation boundaries signal categories through `DecisionInvalidation`.
  Effects report precise attribute indexes, movement reports location/travel,
  and social-affinity or network mutations report target availability and
  coordination dependencies.
* `DecisionState` caches each candidate result. A same-minute dirty update only
  resolves and scores candidates whose dependency masks overlap changed facts;
  cached unaffected results still participate in deterministic score/hash
  ordering. Advancing to a new minute deliberately performs a full safety pass.
* `EvaluationCount` supplies a deterministic workload measure independent of
  wall-clock noise. CPU and allocation comparisons are recorded in
  `docs/decisionbaseline.md`.

### 4.16 Candidate Indexing (Milestone 14)

* `IntentCompiler` assigns the bit position as part of its existing dense
  runtime indexing. `CompiledIntentCatalog` constructs candidate bitsets once
  during content loading and excludes the fallback from ordinary candidates.
* Static indexes conservatively remove intents whose required home, workplace,
  social relation, or universal job context is absent. Runtime context is
  reduced to booleans before bitset intersection; no authored strings are read.
* `AgentDecisionSystem` enumerates the intersected set bits for reevaluation and
  cached winner selection. Its struct enumerator avoids catalogue scans,
  per-agent candidate arrays, LINQ sorting, and hot-path iterator allocation.
  Final ordering remains score descending then stable action hash ascending.
* The safe fallback is applied only when no indexed ordinary candidate remains
  eligible. Dependency masks continue to decide which members of that candidate
  set must be rescored on a same-minute update.

---

### 4.17 Decision Diagnostics and Content Safety (Milestone 15)

* `AgentDecisionSystem` allocates contribution and rejection caches only when
  launched with `-debug`. `DebugSnapshotBuilder` copies them into immutable
  candidate snapshots; player-facing intelligence remains unchanged.
* The decision inspector shows every candidate's eligibility, rejection path,
  target, score contributions, trait modifiers, cooldown, commitment state,
  final score, and selected-winner status.
* `--validate-content [directory]` loads and compiles content without Raylib.
  CI runs the test suite and this command so malformed intent content fails with
  its file, intent ID, and JSON path.

### 4.18 Mutual Activity Coordination (Milestone 16)

* `CoordinationSystem` runs after deliberation and before execution. Mutual
  winners produce invitations; participant eligibility and utility are evaluated
  from separately compiled content with the invitee as agent and initiator as
  target.
* Invitations are sorted by participant utility, initiator utility, action hash,
  initiator ID, and target ID. Greedy disjoint acceptance makes pairing
  deterministic and prevents double booking. Rejected initiators receive the
  authored cooldown and are dirtied for reconsideration.
* Normal minimum-commitment, switching, and urgent-preemption controls protect
  the invitee. The participant waits in place while the initiator follows the
  generic route; both begin Performing only when colocated.
* Before the mutual minimum duration both remain committed. After it, either
  partner's better alternative releases the pair under normal switching rules;
  the authored maximum duration releases them unconditionally. Missing partners,
  invalidated relations, and impossible travel release immediately.
* Effects declare `subject: initiator|participant` and are applied independently
  only while both partners are Performing. Release clears both coordination
  components, applies the safe fallback, and dirties both decisions.
* Debug snapshots copy partner, role, status, timing, duration, utilities, and
  release state. These coordination and network facts never enter
  `PlayerIntelligenceDB`.

### 4.19 Deterministic Work Diagnostics (Milestone 17.1)

* `SimulationWorkDiagnostics` is an optional observer injected into
  `AgentDecisionSystem`. Production behavior and the player-intelligence
  boundary are unchanged when it is absent.
* A snapshot separates five deterministic counts: actual decision passes,
  scored candidate evaluations, target-snapshot population visits, social-edge
  visits, and allocation-sensitive transient collection/rank operations.
  Counters never read elapsed wall time and can be reset between measured
  phases.
* The baseline deliberately instruments the existing population scans rather
  than optimizing them. Milestones 17.2–17.4 use these counts to prove that
  persistent indexes remove unrelated visits without changing decisions.

### 4.20 Agent and Social Lookup Bootstrap (Milestone 17.2)

* `AgentSpawner` owns one persistent `AgentSocialIndexes` instance and rebuilds
  it only after agents, network memberships, and all network-seeded and baseline
  social edges have been generated. Existing decision, execution, interaction,
  and intelligence behavior does not consume the indexes in this slice.
* The agent directory provides constant-time integer-ID lookup. Directed edges
  are radix-ordered by source, target, and edge entity ID into one packed array;
  source ranges provide allocation-free neighbour spans and binary search for a
  particular target without visiting unrelated agents or edges.
* The generated social graph is immutable after bootstrap for this milestone.
  Explicit population/social mutation notifications invalidate the relevant
  lookups, which throw until a full rebuild makes the snapshot current again.
  This guards future mutation entry points rather than silently serving stale
  ECS IDs.
* Indexes remain Ground Truth simulation infrastructure. They are not queried by
  ImGui and are never copied into `PlayerIntelligenceDB`.

### 4.21 Indexed Decision Targeting (Milestone 17.3)

* `AgentDecisionSystem` and `CoordinationSystem` share the bootstrap
  `AgentSocialIndexes`. Target resolution reads an agent's current location and
  attributes directly from the indexed entity rather than constructing
  population-wide dictionaries on every update.
* Social selectors traverse only the actor's packed outgoing span. Network
  selectors traverse the actor's native memberships and the bounded incoming
  membership links of matching networks; supervisor and direct-report checks
  use the membership relation itself.
* Requirements and ranking retain their compiled data-driven semantics. The
  best candidate's rank is compared in place, avoiding per-candidate arrays,
  sets, ordering iterators, and other transient target-enumeration allocations.
  Equal ranks continue to select the lowest entity ID.
* Work diagnostics now report zero population visits, edge scans, and transient
  target-snapshot operations during a detailed decision update. The indexes
  remain Ground Truth-only and do not alter the intelligence isolation layer.
