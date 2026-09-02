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

## Milestone 13 dependency-driven comparison

Measured on 2026-08-31 in the same container class, using .NET 10 roll-forward
to execute the `net8.0` Release test because the .NET 8 runtime was unavailable.
The benchmark first runs the safety-fallback full pass, then holds the minute
fixed and signals only the fatigue attribute for sixty updates.

| Mode | Intent evaluations | Elapsed | Allocated | Change from current full pass |
|---|---:|---:|---:|---:|
| M13 full-minute fallback | 180,000 | 994.105 ms | 177,969,688 B | — |
| M13 fatigue-selective | 120,000 | 419.300 ms | 98,793,688 B | -33.3% evaluations, -57.8% CPU, -44.5% allocation |

The full-minute path is slower and allocates more than the M6 measurement
(+15.1% elapsed and +82.5% allocation). This is an explicitly recorded
regression from retaining per-agent candidate results while the minute fallback
remains enabled. The selective path already more than offsets CPU cost for the
measured mutation; M14 candidate indexing should address the remaining target
resolution and catalogue-scan allocation before the fallback is retired.

## Milestone 14 candidate-index scaling

Measured on 2026-08-31 in the same Linux x64 container class with the Release
build and .NET 10 major roll-forward. The focused benchmark models 1,000 agent
contexts and repeats candidate enumeration 100 times. One in four dense runtime
indexes is present, spread through every packed word, so visits grow with the
intersected candidate population rather than requiring an intent-object scan.

```text
DOTNET_ROLL_FORWARD=Major /root/.dotnet/dotnet test ProxyState.sln -c Release \
  --filter 'FullyQualifiedName~CandidateIndexScalingTests|FullyQualifiedName~DecisionPerformanceBaselineTests' \
  --logger 'console;verbosity=detailed'
```

| Catalogue intents | Indexed candidates | Candidate visits | Elapsed |
|---:|---:|---:|---:|
| 3 | 1 | 100,000 | 35.596 ms |
| 32 | 8 | 800,000 | 65.104 ms |
| 128 | 32 | 3,200,000 | 54.383 ms |
| 256 | 64 | 6,400,000 | 115.732 ms |

Elapsed results are intentionally treated as noisy microbenchmark observations;
the deterministic visit counts are the scaling assertion. Increasing the
catalogue from 128 to 256 doubles both selected candidates and visits rather
than evaluating all 256 definitions for every agent.

The production 1,000-agent, sixty-minute benchmark on the same run measured a
442.403 ms full pass with 101,124,056 B allocated (7,373.4 ns and 1,685.40 B per
agent decision), and a 197.295 ms fatigue-selective pass with 22,046,968 B
allocated. Compared with the recorded M13 run, full-pass elapsed time improved
55.5% and allocation improved 43.2%; the selective path improved 52.9% and
77.7%, respectively. The current three ordinary intents all have their required
agent context in the generated population, so evaluation counts do not fall in
that production fixture yet. The improvement comes from word-indexed traversal
and removing per-agent candidate LINQ arrays/sorts. Larger sparse catalogues are
where candidate rejection reduces evaluations as well.

## Milestone 16 relationship-driven comparison

Measured on 2026-09-01 on Windows x64 with .NET 8.0.13 in Release. This host is
not comparable for elapsed time with the earlier Linux runs, so allocation and
deterministic evaluation counts are the more useful change record.

| Mode | Intent evaluations | Elapsed | Allocated | Per agent decision |
|---|---:|---:|---:|---:|
| M16 full-minute pass | 488,000 | 1,300.567 ms | 1,038,153,808 B | 21,676.1 ns / 17,302.56 B |
| M16 fatigue-selective | 180,000 | 365.345 ms | 219,095,760 B | — |

The ordinary catalogue grew from three to eight scored intents, including six
entity-target behaviors and separately evaluated participant offers. Full-pass
evaluations rose 171.1% from the M13 180,000 count. Allocation rose roughly
10.3x versus the recorded M14 full pass because each minute now builds social,
location, attribute, and network target snapshots and evaluates a much larger
targeted catalogue; this is recorded performance debt, not a semantic failure.
Fatigue-only invalidation evaluates 180,000 candidates rather than all 488,000,
a 63.1% reduction, showing that the dependency masks still avoid unrelated
relationship work on same-minute mutations. Future optimization should reuse
target snapshot storage before weakening deterministic selection or content
generality.

## Milestone 17.1 large-population and work-count baseline

Measured on 2026-09-02 in an Ubuntu 24.04.4 LTS Linux x64 container with .NET
SDK 8.0.424 and an Intel Xeon E5-2673 v4 virtual CPU allocation (17 logical
processors). The container had 65 GiB RAM and no swap. Results use a Release
build and seed `17001`; timing and allocation remain host observations, while
the five work counters are deterministic assertions.

```text
PROXYSTATE_RUN_LARGE_BENCHMARKS=1 dotnet test ProxyState.sln -c Release \
  --filter FullyQualifiedName~Milestone17BaselineTests.PopulationGenerationAndDetailedLoopBenchmark \
  --logger "console;verbosity=detailed"
```

| Population | Generated edges | Generation elapsed | Generation allocated | One detailed minute | Loop allocated |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 11,074 | 186.730 ms | 11,353,824 B | 38.338 ms | 17,072,992 B |
| 10,000 | 111,428 | 1,243.621 ms | 111,595,392 B | 512.021 ms | 170,120,400 B |
| 100,000 | 1,110,552 | 16,226.007 ms | 1,218,924,728 B | 8,340.040 ms | 1,678,236,600 B |

| Population | Decision passes | Candidate evaluations | Target population visits | Edge visits | Transient operations |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 1,000 | 8,000 | 3,000 | 11,074 | 47,555 |
| 10,000 | 10,000 | 80,000 | 30,000 | 111,428 | 477,402 |
| 100,000 | 100,000 | 800,000 | 300,000 | 1,110,552 | 4,761,556 |

The fixture measures generation separately, then one full detailed decision
minute. Ordinary test runs execute the 1,000-agent case; setting
`PROXYSTATE_RUN_LARGE_BENCHMARKS=1` opts into the resource-intensive 10,000 and
100,000 cases. `WorkCountersAndDecisionsRepeatForFixedSeed` independently
asserts identical counters and selected action/target tuples across two worlds.

The scans to remove in later Milestone 17 slices are explicitly:

* `TargetResolver` visits every agent three times per update to copy locations,
  attributes, and network memberships, and visits every directed social edge;
* entity target ranking creates a rank array per visited candidate, while
  network selection creates transient candidate sets and ordered iterators;
* `IntentExecutionSystem` copies every agent location on every elapsed tick;
* `InteractionSystem` visits every directed `EdgeData` entity on its interval;
* The long-lived `PlayerIntelligenceDB` and debug/dossier projections retain separate
  full-population or relationship scans. Those presentation-boundary scans are
  recorded here but remain explicitly outside Milestone 17 optimization scope.
