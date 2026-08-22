# M5 damage-model foundations

This document describes the implementation boundary delivered by roadmap issues
DM-001 through DM-006. It is an engineering description, not a claim of medical
validation.

## Authoritative data flow

```text
recorded scenario seed / named random streams
                     |
body-local projectile input + typed projectile profile
                     |
          IProjectileInteractionService
                     |
 ordered voxel lookup -> per-segment energy loss -> one tissue deposit
                     |
 WoundTrack + EnergyLedger + ImpactDebugTrace + final projectile state
                     |
       tests / reference CLI / Godot presentation
```

`TacticalSim.Core` owns body intersection, energy transfer, wound-track assembly,
state mutation, and debug telemetry. Clients may calculate external flight and
render the immutable result, but they must not step voxels or distribute tissue
damage themselves.

## Model-version migration

- `m5-foundations-v2` is the default and authoritative model. It resolves stable,
  ordered swept intersections and applies only energy lost by the projectile.
- `legacy-v1` is an explicit comparison flag. It preserves the historical
  full-energy point deposit and reports its conservation error in telemetry.
- The legacy point path is named `ProcessLegacyImpact`, requires the `LegacyV1`
  enum value, and is not called by production clients.
- Raw voxel penetration and kinetic-energy mutation methods are internal to the
  core assembly, so a presentation client cannot create a competing injury path.

The flag is selected globally with `DamageModelOptions` or overridden on a
`ProjectileInteractionRequest` for a comparison run. Removing `legacy-v1` is a
later migration decision; M5 does not silently delete it.

## Units and coordinates

The adopted typed-unit strategy is recorded in
[ADR-001](ADR-001-typed-units.md). New damage contracts use `Energy` and
`Distance` directly. Scenario and debug boundaries use typed mass, area, time,
volume, and flow values where applicable. JSON converters write objects with
canonical unit names rather than unlabelled numbers.

All wound-track positions are `System.Numerics.Vector3` values in
`BodyLocalMeters`. Projectile velocity vectors are meters per second. A client
with a world-space actor origin translates positions exactly once before calling
the service and exactly once when rendering returned points.

M5 structure identifiers are deterministic spatial snapshots composed of the
semantic body hierarchy, current structure type, and body-local voxel coordinates.
They do not depend on child/voxel list order, and duplicate derived identifiers fail
resolution rather than creating an unstable comparator tie. M6 replaces these with
persistent anatomical structure identities without exposing voxels in the public
wound contract.

## Energy accounting

Every non-null interaction result contains one `EnergyLedger`:

```text
incoming = outgoing
         + ordered structure deposits
         + projectile deformation allocation
         + fragmentation allocation
         + numerical residual
```

The M5 model does not yet enable projectile deformation or fragmentation, so
those allocations are zero but remain explicit. A mixed numerical tolerance is
used: the larger of `0.0001 J` absolute or `0.000001` times the larger magnitude
of incoming and allocated energy. Failures remain serializable and produce a
warning instead of hiding the invalid result.

Canonical track construction also verifies endpoint/path-length agreement,
energy continuity between segments, fragment initial/final states, aggregate
terminal energy, and a one-to-one match between ordered segments and ledger
deposits. Authoritative tracks reject any segment that transfers more energy
than its projectile state lost. A ledger that claims conservation must also
balance each fragment locally and show the primary projectile funding all
fragment initial energy. Fragment initial energy is carried projectile energy;
the separate fragmentation allocation represents irreversible formation loss.
The explicitly flagged legacy track may retain its known over-allocation so
comparison telemetry can expose it.

Temporary-cavity geometry may be emitted as a debug effect from the direct tissue
deposit. It does not create a second energy deposit in neighboring voxels. This
removes the old behavior that counted the same projectile energy once at the
impact voxel and again as radial damage.

## Deterministic randomness and replay metadata

Simulation randomness comes from an injected root seed and stable ordinal stream
names. The current algorithm identifier is `fnv1a64-splitmix64-v1`; FNV-1a derives
named stream seeds and SplitMix64 advances each stream independently. Snapshots
record the root seed, stream seed, stream name, algorithm version, and draw count.

The M5 interaction itself has no stochastic terminal behavior, but it records the
zero-draw `damage.projectile-interaction` stream so future bounded uncertainty can
be introduced without changing the replay boundary. Shooting deviation uses an
actor-named injected stream rather than constructing `Random` from an actor hash.
Scenario/replay composition can supply recorded entity IDs through the explicit
`TacticalEntity(Guid, ...)` constructor.

Reference runs supply a request-scoped provider derived from their recorded seed.
The interaction service captures metadata from the provider it actually owns for
that resolution; callers cannot substitute an unrelated metadata snapshot.

## Debug trace

Each resolved impact returns an omniscient stable trace containing:

- impact, projectile profile, model version, shooter, and target identifiers;
- body-local entry, terminal point, ordered structures, and state changes;
- per-segment and aggregate energy accounting;
- immediate physiology and current capability snapshots before and after;
- seed and named-stream metadata;
- active treatments, current bleeding-source summaries, and numerical warnings.

Lesion and blood-destination collections are intentionally empty in M5. Their
schema positions are reserved for M6 and M7; the foundations milestone does not
create a premature competing lesion model.

## Parameter provenance

| Parameter or rule | Value | Classification | Purpose |
| --- | ---: | --- | --- |
| Absolute ledger tolerance | 0.0001 J | Provisional numerical | Detect accounting errors above float noise; review in M12. |
| Relative ledger tolerance | 0.000001 | Provisional numerical | Scale conservation checks for higher-energy projectiles. |
| Energy stop epsilon | max(0.0000001 J, 0.000001 × incoming energy) | Numerical guard | Normalize exhaustion residue without turning the ledger's absolute accounting tolerance into a physical stopping rule. |
| Speed divisor epsilon | 0.000001 m/s | Numerical guard | Keep traversal-time calculations finite. |
| Tissue drag equation and tissue properties | Existing repository values | Historical/provisional | Preserved pending M12 validation and calibration. |
| Legacy tear coefficient | 0.1 on the legacy MPa scalar | Provisional legacy calibration | Comparison behavior only; not used to justify medical validity. |
| Secondary energy duplication | Disabled | Architecture invariant | Energy is deposited once at the directly intersected structure. |
| FNV-1a / SplitMix64 constants | Published algorithm constants | Sourced technical | Stable named-stream derivation and generation. |

No new medical calibration constant is introduced by M5.

## Performance model

The current spatial adapter scans existing voxels once and sorts only ray
intersections: `O(V + I log I)` time and `O(V + I)` temporary references, where
`V` is the existing anatomy voxel count and `I` is the much smaller intersected
set. This replaces client-side ten-microsecond body stepping and the per-shot
2.2-million-slot Godot grid. M6 may replace the internal lookup with a spatial
index without changing callers or wound contracts.

## Known limitations and non-claims

- Voxels remain the M5 spatial implementation until M6 structure contracts land.
- Fragment tracks are supported by the contract but generation is disabled.
- Projectile deformation allocation is represented but not yet modeled.
- M5 snapshots expose existing physiology/capability values; M7 owns their
  replacement with layered lesion, circulation, and capability resolvers.
- Reference outputs verify equations, determinism, and invariants. They do not
  medically validate predicted injuries.
- External cover/material interactions remain separate from body injury; they may
  feed the final body-entry projectile state into this service.

## Verification

Run the focused M5 suite, followed by the complete solution:

```powershell
dotnet test TacticalSim.Tests/TacticalSim.Tests.csproj --filter "FullyQualifiedName~TypedUnitTests|FullyQualifiedName~DeterministicRandomTests|FullyQualifiedName~EnergyLedgerContractTests|FullyQualifiedName~WoundTrackContractTests|FullyQualifiedName~ProjectileInteractionServiceTests|FullyQualifiedName~ReferenceImpactHarnessTests"
dotnet build TacticalSim.slnx
dotnet test TacticalSim.slnx --no-build
```
