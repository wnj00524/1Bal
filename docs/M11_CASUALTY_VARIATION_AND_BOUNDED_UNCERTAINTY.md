# M11 — Casualty variation and bounded uncertainty

M11 adds scenario-configurable casualty baselines and reproducible variation without
making random results direct causes of injury outcomes. `CasualtyProfile` is a JSON
serializable, versioned input. The unchanged `standard-adult` profile preserves the
previous 70 kg, 5,000 ml, 80 bpm, and 93 mmHg baseline. The provisional body-size
helper uses 70 ml/kg and profile validation bounds supported scenario inputs to
20–250 kg and 1,000–12,000 ml pending M12 calibration.

Physiological uncertainty uses the existing named SplitMix64 replay streams. Version
`physiology-uncertainty-v1` bounds blood volume to ±8%, resting heart rate to ±8 bpm,
MAP to ±7 mmHg, and stress response to ±12%. Fixed mode returns neutral modifiers
without consuming random values. Samples modify causal-model inputs only.

Terminal projectile profiles separate construction, deformation, yaw, fragmentation,
and retained mass from nominal calibre. Defaults must remain conservative; values are
scenario data, not clinical claims. Wearable layers are core pre-entry hooks that can
remove energy, change entry speed, stop penetration, and report blunt energy. They do
not attempt detailed armor simulation.

The headless cohort runner derives one stable seed per index, keeps output ordering,
records elapsed time, and isolates case failures. Its result is JSON serializable and
includes the caller's model version, enabling M12 comparison tooling without Godot.

## Replay contract

Record the casualty profile/schema, uncertainty version and bounds, root seed,
terminal profile, wearable layers, model version, and ordered scenario inputs. A
fixed profile with uncertainty disabled is the debugging baseline.
