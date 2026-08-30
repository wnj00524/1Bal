## 2. Core ECS Data Structures

The simulation uses pure value-type components and tags implementing the native
`Friflo.Engine.ECS` interfaces. Components contain only simulation state; systems
contain behavior. The namespace for Milestone 1 types is `ProxyState.Simulation`.

### 2.1 Agent Components (Ground Truth)

```csharp
using Friflo.Engine.ECS;

public struct Tier1LodTag : ITag { } // Updated every simulation tick.
public struct Tier2LodTag : ITag { } // Reserved for hourly updates.
public struct Tier3LodTag : ITag { } // Reserved for daily updates.
public struct OperativeTag : ITag { } // Player-controlled intelligence source.

public enum IntelligenceRole : byte {
    None,       // Zero/default value for unassigned agents.
    Officer,
    Agent,
    Informant
}

public struct Identity : IComponent {
    public int NameId;       // Hash mapped to localization.
    public int OccupationId; // Hash mapped to job data.
    public IntelligenceRole IntelligenceRole;
}

public struct PoliticalAlignment : IComponent {
    public byte FactionId;   // JSON faction ID.
}

public struct AgentAttributes : IComponent {
    public float[] Values; // Values are ordered by data/agent-schema.json.
}

public struct Psychology : IComponent {
    public long TraitMask; // Bits are supplied by data/traits.json.
}

public struct AgentState : IComponent {
    public int SecretStateHash;    // Hash supplied by data/secret-states.json; 0 is None.
}

public struct IntentionState : IComponent {
    public int ActionHash;          // Goal selected by utility deliberation.
    public int TargetEntityId;     // Social target, or 0.
    public int TargetLocationId;   // Destination for location-bound goals.
    public long SelectedAtMinute;
    public float Utility;
}

public struct ActivityState : IComponent {
    public int CurrentActionHash;   // Public action moved out of AgentState.
    public ActivityKind Kind;       // Idle, Working, Resting, Socializing, Commuting.
    public long StartedAtMinute;
}

public struct DecisionState : IComponent {
    public long LastConsideredMinute;
    public bool Dirty;
    public int[] CooldownActionHashes;
    public long[] CooldownUntilMinutes;
}

public struct WorldTime : IComponent {
    public double ElapsedSimulationSeconds;
    public double DeltaSimulationSeconds;
}

public struct AgentLocation : IComponent {
    public int HomeLocationId;
    public int WorkLocationId;
    public int CurrentLocationId;
}

public enum AgentTravelMode : byte {
    AtHome, TravellingToWork, AtWork, TravellingHome
}

public struct AgentTravel : IComponent {
    public int[] RouteLocationIds;
    public int TotalTravelMinutes;
    public int RoutePosition;
    public float RemainingTravelMinutes;
    public AgentTravelMode Mode;
}
```

`AgentAttributeSchema` loads the ordered numeric definitions from
`data/agent-schema.json` and resolves IDs to indexes. Each generated agent stores
one floating-point value per definition, so adding an attribute requires only a
data-file change. Values are sampled from a bounded normal distribution centered
on the configured average and constrained to the configured range.

Intention, activity, effects, and covert state are distinct. `IntentionState`
stores what was selected, `ActivityState` stores what is happening now, and
JSON effect definitions describe attribute changes. `AgentState.SecretStateHash`
identifies a separate secret activity such as `Surveillance`. Secret states are loaded from
`data/secret-states.json`; the required `none` definition uses hash `0`, making a
default-initialized `AgentState` safe. Agents are spawned with `None`, and a
covert system may change the secret hash without changing intention or activity.

`data/actions.json` owns each candidate's eligibility predicate, base utility,
weighted numeric expressions, piecewise-linear response curves, trait modifiers,
minimum commitment, switching margin, cooldown, urgent-preemption threshold,
per-minute effects, and target definition. `TargetDefinition` selects `none`, a
direct agent `location`, or an `entity` query. Entity queries contain a relation,
compiled predicate requirements, ordered compiled numeric rankings, and an
optional positive candidate limit; the runtime result carries both entity and
location IDs alongside eligibility and score. Runtime cooldowns use parallel fixed-size arrays because
the first slice has exactly three actions and does not need per-agent dictionaries.

Numeric facts use stable `FactId` values composed of a `FactKind` and an optional
schema index. `FactRegistry` resolves authoring references such as
`agent.attribute.fatigue`, `time.minuteOfDay`, `job.workStartMinute`, and
`target.affinity` during catalog loading. `NumericExpressionDefinition` is the
JSON authoring tree; the loader compiles it to a bounded postfix instruction
array containing opcodes, fact handles, and numeric operands. The runtime stack
evaluator uses `stackalloc`, performs no string lookup, and supports `fact`,
`constant`, `normalize`, `normalizeRange`, arithmetic, `min`, `max`, `clamp`,
`oneMinus`, and `abs`. Trees are limited to 16 levels and 64 instructions.

`PredicateDefinition` is the eligibility authoring tree. It composes typed
boolean facts with `and`, `or`, and `not`, or compares numeric expressions with
`equal`, `notEqual`, `less`, `lessOrEqual`, `greater`, and `greaterOrEqual`.
`CompiledPredicate` stores bounded postfix boolean instructions and
precompiled numeric operands. Boolean/numeric type mismatches and malformed
arity fail during catalog loading; runtime evaluation uses a stack-allocated
boolean span and performs no string parsing or per-agent allocation.

Binary attributes are traits defined in `data/traits.json`. Their unique positive
single-bit values are combined in `Psychology.TraitMask`; `prevalence` controls the
independent probability that a generated agent has each trait. The `long` mask
currently supports up to 63 positive single-bit traits.

`Identity.OccupationId` stores the stable hash of the agent's assigned job. Jobs
are loaded from `data/jobs.json`; each job defines an integer start and end
minute, a set of workdays from 1 through 7, and the required workplace type.

World locations are loaded from `data/world.json` as typed nodes connected by
bidirectional edges. Each location has a stable integer hash, and each edge
has a positive travel duration in in-world minutes. `WorldTopology` validates
the graph and calculates deterministic shortest-time routes. Spawned agents
store their home, workplace, current location, and route in the location and
travel components above.

### 2.2 The Social Graph (Edge Entities)

To model the social network and intelligence discovery, relationships are created as distinct Entities containing the `EdgeData` component, linking two agents.

```csharp
public struct EdgeData : IComponent {
    public Entity Source;
    public Entity Target;
    public float Affinity;       // -100 to 100

    // KNOWLEDGE MASKS (Parallel Bitmasks)
    // 1 = Source knows this data about Target; 0 = Hidden
    public long KnownTraitMask;      
    public byte KnownStatsMask;      
    public byte KnownPoliticalMask;  
}

```

`SocialGraphBuilder` creates five unique peers for each agent using the
injected random source. Each peer relationship is stored as two directed edge
entities, one in each direction, with independent knowledge masks. Populations
smaller than six receive the largest valid graph degree for their size. Self-links
and duplicate peers are not created.

`InteractionSystem` processes every edge on the configured interval (60 ECS
ticks by default). A source's d100 plus `perception` competes with the target's
d100 plus `willpower`; a target with the `paranoid` trait receives a 20-point
willpower bonus. A successful contest reveals one present, previously unknown
trait by OR-ing its bit into `KnownTraitMask`. The mask records confirmed
present traits only, so confirmed absence is not represented. Affinity is the
normalized percentage of configured traits shared by the target and the
source's known mask.

### 2.3 Debug Inspection Snapshots

Debug inspection uses immutable copies rather than exposing `Entity` instances to ImGui. `DebugAgentSnapshot` contains the scalar identity, occupation, faction, public action, secret-state, and trait-mask values plus read-only collections for schema-defined attributes, every configured trait's present/absent state, named locations, travel state, and resolved network memberships. `DebugNetworkMembershipSnapshot` copies a network ID/display name/type, role hash/name, and optional supervisor ID/display name. `DebugNetworkSnapshot` copies a network's identity, resolved type, optional named anchor, and member count. `DebugInspectionSnapshot` groups the agent and network collections passed to the debug UI. `DebugSnapshotBuilder` is the ECS boundary that creates these snapshots; `DebugWindow` renders only the copied values.

### 2.4 World-Time Presentation Snapshot

`WorldTimeSnapshot` is an immutable UI-facing copy of the `WorldTime` calendar fields:

```csharp
public readonly record struct WorldTimeSnapshot(
    int DayNumber,
    int DayOfWeek,
    int MinuteOfDay);
```

The application creates this snapshot after the clock and simulation systems update. `WorldTimeBar` renders it without retaining or querying the ECS clock entity, preserving the intelligence isolation boundary for the ImGui layer.

### 2.5 Application Launcher State

`ApplicationId` identifies the presentation applications exposed by the launcher:

```csharp
public enum ApplicationId
{
    Dossiers,
    DebugWindow
}
```

`ApplicationIcon` pairs an application identifier with its launcher label and compact icon glyph. `ApplicationShell` keeps the selected icon and open/closed presentation state for the `Surveillance Terminal` and `Debug Window`; it contains no ECS entity references or simulation data.

### 2.6 Operative Intelligence Snapshots

`OperativeTag` identifies the five randomly selected agents controlled by the
player's team. `SimulationDefaults.OperativeCount` is `5`; smaller populations
select all available agents. Selection uses the spawner's injected `Random`, so
seeded runs reproduce the same team membership.

`PlayerIntelligenceAgentSnapshot` contains an agent ID, name ID, Operative
marker, intelligence role, and the team-known trait mask. It intentionally omits
the ground-truth secret state. `PlayerIntelligenceDB` contains a
read-only list of these snapshots plus the selected Operative IDs. Its capture
boundary scans only outgoing edges whose source has `OperativeTag` and combines
their known trait masks per target with bitwise `OR`. It does not copy
`Psychology` or retain ECS entities. Operatives are assigned the `Officer` role
when spawned; other agents default to `None`. The dossier displays non-empty
roles alongside each agent and applies each configured trait bit with
`knownMask & trait.Bit` before choosing a visible name or `Trait: ???`.

### 2.7 Agent Network Catalog

`data/networks.json` is the static source of network types, roles, and generation
policies. `AgentNetworkCatalog` validates that document at startup and exposes
immutable `NetworkTypeDefinition`, `NetworkRoleDefinition`, and
`NetworkGeneratorDefinition` records. Identifiers and references are converted
to stable FNV-1a integer hashes during loading; lookups by either ID or hash are
cached dictionaries and do not perform runtime string searches.

Hierarchy is deliberately limited to `Flat` and `SingleSupervisor`. Partitioning
is also a registered set (`HomeLocation` and `WorkLocation`), rather than a
content-supplied query language. A generator contains bounded weighted sizes,
an explicit remainder policy, data-driven role hashes, and (only for a
single-supervisor hierarchy) span-of-control and depth limits. Runtime ECS
network instances use `AgentNetworkData` ECS entities. `TypeHash` selects their
validated type, `AnchorLocationId` is the partition location (or zero for an
unanchored network), and `Ordinal` provides deterministic identity within a
generated series.

Agent membership is an `AgentNetworkMembership : ILinkRelation` stored on the
agent and keyed by its `Network` entity. It carries the data-driven `RoleHash`
and an optional `Supervisor` entity. The relation key guarantees at most one
membership per agent/network pair while still allowing unrelated family and
company memberships. The supervisor is intentionally not another relation key,
so deletion cleanup must update it explicitly.

`AgentNetworkService` is the only supported mutation boundary. It validates
live agent/network entities, catalog roles, per-type cardinality, flat versus
single-supervisor rules, and acyclic supervision. A removed manager's direct
reports move to that manager's supervisor. A live root can be removed only with
an explicit direct-report successor; when an agent is deleted externally, the
lowest-entity-ID direct report succeeds a deleted root deterministically.
Deleting a network removes every incoming membership before deleting its ECS
entity. Its type, anchor, and ordinal are immutable after creation.

Persistent network storage remains linear in population. ECS data contains no
display strings, member arrays, dictionaries, descendant closures, or redundant
supervisor indexes: each generated agent contributes approximately one family
and one company relation. Resolved names and summary collections are transient
debug projections and are discarded after presentation.
