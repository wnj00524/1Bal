# DM-005 deterministic randomness

## Contract

Gameplay randomness is rooted in an injected `IRootSeedProvider`. The root seed is part of
scenario/replay identity and must be recorded with the ordered actions that produced an outcome.
`IDeterministicRandomStreamProvider` derives independently advancing streams from that root seed
and an ordinal, stable stream name.

Callers obtain an `IDeterministicRandomSource` with `GetStream(name)` and keep drawing from that
named source. Requesting the same name from the same provider returns the same advancing source.
Draws from one name do not change any other stream.

`CaptureSnapshot()` produces replay/debug metadata containing:

- the algorithm version;
- the root seed;
- every created stream in ordinal name order;
- each derived stream seed;
- each stream's draw count.

Capturing metadata does not consume random values. The snapshot is diagnostic and replay metadata,
not a replacement for recording the scenario, model version, actor profiles, and ordered actions.

## Composition

`AddTacticalSimCore()` registers the root-seed and stream-provider contracts as singletons. Its
zero root seed is a deterministic scaffolding fallback, not entropy and not a scenario-selection
policy. A scenario or replay composition root supplies its recorded seed before core registration:

```csharp
services.AddSingleton<IRootSeedProvider>(new FixedRootSeedProvider(recordedRootSeed));
services.AddTacticalSimCore();
```

Actions that use randomness require `IDeterministicRandomStreamProvider` explicitly. This prevents
hidden `Random` construction and makes the scenario seed available to replay/debug tooling.

## Stable naming

Stream names are case-sensitive ordinal identifiers. Empty names and names with leading or trailing
whitespace are rejected. Names should identify the subsystem and stable partition key, not execution
order or an object hash code.

Shooting deviation uses `shooting.deviation.actor/{actor-guid-N}`. Repeated shots by an actor advance
that actor's stream, while draws made by other actors or subsystems cannot perturb it. A deviated shot
consumes two values: radial magnitude and angle.

## Algorithm and provenance

Version `fnv1a64-splitmix64-v1` hashes the UTF-8 stream name with the published FNV-1a 64-bit offset
basis and prime, combines it with the root seed, and uses the published SplitMix64 avalanche and
generator constants. These constants define the deterministic algorithm; they are not damage-model
calibration or gameplay tuning parameters.

The generator is selected for a small, explicitly implemented, cross-platform deterministic contract.
It is not cryptographically secure. Changing either derivation or generation requires a new algorithm
version and replay migration rather than silently reinterpreting recorded seeds.
