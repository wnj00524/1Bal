# Intent authoring guide

Intents live in `data/actions.json`. Run
`dotnet run --project ProxyState.csproj -- --validate-content data` to validate
and compile all content without opening Raylib. Errors identify `actions.json`,
the intent ID, and the problematic JSON path.

## Fact catalogue

| Facts | Type | Meaning |
|---|---|---|
| `time.minuteOfDay`, `time.dayOfWeek` | number | Current calendar values. |
| `job.workStartMinute`, `job.workEndMinute` | number | Occupation schedule. |
| `job.isWorkDay` | boolean | Today occurs in the job's work days. |
| `agent.attribute.<id>` | number | Value defined by `agent-schema.json`. |
| `agent.location.current`, `.home`, `.work` | number | Stable location hashes. |
| `travel.reachable` | boolean | Agent is home or has an available route. |
| `target.entity`, `target.location.current` | number | Resolved target IDs. |
| `target.attribute.<id>` | number | Target value from `agent-schema.json`. |
| `target.affinity` | number | Directional social affinity normalized to 0–1; neutral `0.5` without an edge. |

## Operators

Predicates support boolean `fact` and `constant`, `and`, `or`, `not`, plus
`equal`, `notEqual`, `less`, `lessOrEqual`, `greater`, and `greaterOrEqual`
numeric comparisons. Numeric expressions support `fact`, `constant`, `add`,
`subtract`, `multiply`, `divide`, `min`, `max`, `abs`, `oneMinus`, `clamp`,
`normalize`, and `normalizeRange`. `normalize` requires a direct ranged fact;
use `normalizeRange` with `min` and `max` for computed input. Division by zero
deterministically produces zero.

## Targets, executors, and effects

The validated combinations are `none` with `performHere` or `wait`, `location`
with `performAtLocation`, and `entity` with `performWithEntity`. Target-bound
executors require `destination: "intent.target"`. Location values are
`agent.location.current`, `.home`, or `.work`. Entity query relations are:

| Relation | `networkType` | Candidates |
|---|---|---|
| `social` | omitted | Outgoing directional social peers. |
| `network-member` | required | Other members of the named network type. |
| `network-supervisor` | required | The actor's direct supervisor; the type must be hierarchical. |
| `network-direct-report` | required | The actor's direct reports; the type must be hierarchical. |

Queries filter with predicate `requirements` and contain one or more numeric
`rankBy` entries ordered `ascending` or `descending`; `limit`, when present, is
positive. All relation and network IDs compile at load, so runtime selection
does not parse strings.

Effects contain an attribute ID, signed `perMinute` rate, and optional
`subject`. Omission or `initiator` affects the solo actor/inviter; `participant`
is valid only for mutual entity actions. The generic effect system applies and
schema-clamps each side independently only while both are performing.

## Mutual participation

Add `participation` only to an entity target using `performWithEntity`. Its
`mode` is `mutual`; minimum duration must be positive, maximum must be at least
the minimum, and rejection cooldown cannot be negative. `acceptance` is a
complete second utility definition with `baseUtility`, `eligibility`,
`utilityInputs`, and `traitModifiers`. It is evaluated with the invitee as
`agent` and the inviter as `target`.

The coordinator deterministically selects disjoint pairs, respects the
invitee's commitment/switching controls, reserves the invitee while the
initiator travels, and starts both together. Either partner may request normal
release after the minimum; release is forced at the maximum or immediately if
the partner/relation/travel becomes invalid. Content must not encode friendship,
family, or employment IDs into runtime code.

## Patterns and performance cautions

Use a normalized attribute with a two-point curve for a linear need, an `and`
predicate for schedule windows, and a location target for home/work activities.
Exactly one target-free `wait` intent must be the `fallback`.

Trees are limited to 16 levels and 64 instructions. Prefer shallow expressions
and direct facts: dependencies determine dirty reevaluation, while entity target
queries inspect social or network snapshots. Keep unique stable hashes unchanged after
shipping. JSON order defines dense runtime indexes; hashes break score ties.
Never branch on an intent ID in C#—new behaviour must compose generic data.
