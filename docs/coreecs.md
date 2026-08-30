# 4. Core ECS Systems (Logic)

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
  Entity queries traverse the declared social relation, evaluate compiled target
  requirements, rank candidates lexicographically, and break exact ties by
  ascending entity ID. Losing or changing the selected target dirties the
  decision immediately through the same generic resolver.
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

* `SocialGraphBuilder` creates a randomized simple graph with five peers per agent (or the largest valid degree for smaller populations), storing each pair as two directed `EdgeData` entities.
* `InteractionSystem` runs on every 60th ECS update by default. The interval is injectable for tests and future tuning.
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

### 4.5 Intent Execution System (Milestones 2 and 10)

**Goal:** Execute any Tier 1 intention through reusable movement and performance mechanics.

* Each action declares `performHere`, `performAtLocation`, `performWithEntity`,
  or `wait`; loading resolves that string to `ExecutorKind` and validates that
  its target type and `intent.target` destination are compatible.
* Location and entity executors share deterministic shortest-route traversal;
  the movement path contains no action IDs.
* Arrival changes activity from Travelling to Performing and dirties the
  decision. A missing entity or unreachable destination idles the actor and
  dirties its decision for reconsideration.

### 4.6 Debug Agent Inspector

**Goal:** Provide an opt-in development view of the complete simulated agent population.

* Debug mode is enabled only when the process receives the `-debug` command-line argument (case-insensitive).
* `DebugSnapshotBuilder` copies the current agent component values into immutable, UI-facing snapshots once per frame.
* The `Debug` ImGui window lists every agent and lets the user select one to inspect identity, faction, job, attributes, all trait states, current action, secret state, locations, and travel state.
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

### 4.12 Deterministic Agent Network Generation

**Goal:** Generate location-coherent families and bounded company hierarchies
without coupling unrelated replay outcomes.

* `AgentSpawner` derives separate population, Operative, network, and social
  graph random streams from one injected seed. Network content changes can
  consume different random values without changing assignments, Operatives, or
  social peers.
* Network generation runs after every agent has home and work assignments and
  before `SocialGraphBuilder` creates interpersonal edges.
* `AgentNetworkBuilder` buckets agents by the generator's registered home- or
  work-location strategy, sorts buckets by location hash, shuffles each bucket,
  samples configured weighted sizes, and consumes every member exactly once.
  Each resulting network is anchored to its bucket location.
* Families are flat synthetic groupings whose members all use the configured
  member role and have no supervisor. Generation intentionally adds no inferred
  genealogy, ages, or surnames.
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

---
