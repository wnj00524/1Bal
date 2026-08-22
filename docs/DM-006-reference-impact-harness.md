# DM-006 reference impact harness

The reference impact harness is a Godot-free regression and model-comparison boundary. It calls
`IProjectileInteractionService` for every impact and does not contain a second projectile traversal,
energy-transfer, cavitation, wound, or body-damage calculation.

## Built-in scenarios

- `rifle-arm`: a 5.56 mm body-local arm path.
- `rifle-leg`: a .308 body-local leg path.

Both use a freshly constructed `anatomical-dummy-v1` target for every run, a two-meter maximum
traversal, and a one-second physiology observation sampled every 0.1 seconds. Projectile and scenario
inputs carry explicit schema versions. Projectile input records mass, cross-sectional area, muzzle
velocity, the stable `standard-drag-curve-v1` identifier, and its coefficient.

These profiles are deterministic regression fixtures inherited from the former console harness. They
are provisional inputs, not calibrated injury claims or medical validation data.

## CLI

```text
dotnet run --project TacticalSim.ConsoleApp -- --list
dotnet run --project TacticalSim.ConsoleApp -- --scenario rifle-arm --model m5-foundations-v2 --seed 42 --format text
dotnet run --project TacticalSim.ConsoleApp -- --scenario rifle-leg --model m5-foundations-v2 --seed 42 --format json
dotnet run --project TacticalSim.ConsoleApp -- --scenario rifle-arm --seed 42 --compare legacy-v1,m5-foundations-v2 --format text
```

Options are `--scenario`, `--model`, `--seed`, `--format text|json`, `--list`, and `--compare`.
With no comparison models supplied, `--compare` uses `legacy-v1` as the baseline and the selected
`--model` as the candidate.

## Output contract

Each result contains:

- the `reference-impact-result-v2` output schema identifier (`reference-impact-comparison-v2` for comparisons);
- versioned scenario and projectile inputs;
- model identifier and a model-independent comparison key;
- the authoritative wound track and energy ledger;
- persistent lesion output for the authoritative model (legacy comparison runs remain lesion-free);
- an explicit serializable final-projectile state;
- immediate and timed physiology snapshots;
- capability snapshots including mobility, weapon handling, standing, consciousness, and action state;
- the requested root seed plus named-stream seeds and draw counts;
- numerical warnings;
- a lowercase SHA-256 deterministic hash.

The runner creates a request-scoped deterministic stream provider from the requested seed and
passes that provider to `ProjectileInteractionRequest`. The interaction service creates the
zero-draw `damage.projectile-interaction` stream and captures its own post-resolution metadata, so
the emitted seed and stream state cannot diverge from the randomness context used by the resolver.

The hash is calculated over compact JSON excluding the hash field itself. JSON object keys are sorted
ordinally and array order is retained before hashing. Typed quantities serialize using canonical unit
names such as `kilograms`, `squareMeters`, `seconds`, `cubicMeters`, and
`cubicMetersPerSecond`.

Cross-model comparison runs the baseline and candidate independently. Each run asks the scenario for
a fresh target, preventing baseline mutations from leaking into the candidate.

DM-104 advanced result and comparison output from v1 to v2 because capability snapshots gained the
additive `CanStand` field. Consumers that deserialize a fixed v1 shape must migrate explicitly; the
scenario input schema and model-independent comparison key remain unchanged. The standing value is
the bounded musculoskeletal bridge, not the full DM-207 capability resolver.

M5 projectile interaction does not draw random values. Later stochastic terminal behavior must draw
from the same request-scoped provider before the service captures its trace snapshot.
