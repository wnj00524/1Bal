# Decision Behaviour and Performance Baseline (Milestone 6)

This baseline protects the existing `work`, `rest`, and `socialize` semantics
before the data-defined intent refactor. `DecisionBaselineTests` constructs
small, fixed world states rather than relying on randomized population data. It
captures the winning action, target entity/location, utility, selection minute,
travel mode, activity, and cooldowns through the test-only immutable
`DecisionTrace` projection.

The fixtures lock the following behaviour:

- work is eligible from its configured early-start offset through the work
  interval on workdays, and is unavailable after the interval or on Sunday;
- high fatigue and stress can make rest win;
- socialize requires a co-located outgoing social peer and targets the best
  affinity peer;
- minimum commitment and switching margins suppress non-urgent oscillation;
- an urgent score preempts minimum commitment;
- an action configured with cooldown-on-exit cannot be selected before the
  cooldown's exclusive end minute;
- a work intention at home becomes commuting activity toward work.

`RuntimeIntentIdComparisonsRemainAnExplicitMigrationBaseline` is an
architectural debt sentinel. It identifies the current runtime comparisons with
the known `work`, `rest`, and `socialize` IDs in `AgentDecisionSystem` and
`CommutingSystem`. Later milestones should deliberately update this sentinel as
those comparisons are replaced by generic mechanics; new intent-ID comparisons
must not be added.

## 1,000-agent performance baseline

Measured on 2026-08-30 in a Linux x64 Ubuntu 24.04 container with .NET SDK
8.0.424, using a Release build. The container's CPU model and isolation were
not controlled, so elapsed time is a comparison baseline rather than a product
latency guarantee.

```text
dotnet test ProxyState.sln -c Release \
  --filter FullyQualifiedName~DecisionPerformanceBaselineTests \
  --logger "console;verbosity=detailed"
```

| Population | Full decision minutes | Elapsed | Allocated | ns/agent decision | bytes/agent decision |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 60 | 863.908 ms | 97,515,592 B | 14,398.5 | 1,625.26 |

The test uses seed `6001`, spawns the complete production population (including
assignments, networks, and social edges), warms the query and JIT paths once,
then advances sixty distinct simulation minutes. Each minute therefore performs
a full consideration pass for every Tier 1 agent. Allocation is measured with
`GC.GetAllocatedBytesForCurrentThread`; it is practical and reproducible but
does not include allocations performed by other threads. Run the same command
and environment for later comparisons.
