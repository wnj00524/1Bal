# Future Architecture & Implementation Plan

> **Roadmap status:** The damage-model portions of this earlier planning note are superseded by [TacticalSim_Damage_Model_Roadmap.md](TacticalSim_Damage_Model_Roadmap.md) and its [GitHub issue map](DAMAGE_MODEL_ISSUE_MAP.md). Keep the implementation order, contracts, and acceptance criteria in the roadmap authoritative. The sections below remain useful historical context for the original feature intent, but they must not create a second competing damage pipeline.

Based on our design alignment, here is the architectural plan to implement the requested features while maintaining strict performance constraints for a 10-actor simulation. This plan can be handed off to subagents (e.g., via `/teamwork-preview`) to begin implementation.

---

## 1. Advanced Physiological Modeling (Neurological Control)
**Goal:** Model *why* a brain or heart shot is fatal, rather than applying an arbitrary "instakill" flag.

- **Autonomic Drive System:** Introduce an `AutonomicDrive` percentage (0-100%) to `IActorPhysiology`. The Brain acts as the neurological controller.
- **Brain Trauma:** If the Brain voxels are destroyed, `AutonomicDrive` instantly drops to 0%.
- **Downstream Effects:** 
  - `HeartRate` and `BreathingRate` are explicitly multiplied by `AutonomicDrive`. 
  - When the heart stops, Mean Arterial Pressure (MAP) drops to 0. 
  - With 0 MAP, blood oxygenation (`SpO2`) ceases to circulate to the brain, causing Hypoxic Death within seconds.
- **Spinal Cord / Nerves (Optional Next Step):** Specific neck/spine voxels can be mapped. Destruction severs motor control to limbs (setting `MobilityLevel` to 0 without requiring the limb itself to be destroyed).

## 2. Complex Internal Ricochets
**Goal:** Model bullet deflection when hitting dense bone, rather than just flying straight or stopping.

- **Physics-Based Deflection:** Instead of a simple random cone, implement a deterministic ricochet formula.
- **Calculations:** 
  - Calculate the angle of incidence between the projectile's velocity vector and the bone voxel's normal.
  - Determine the **Shatter Threshold** based on the bullet's kinetic energy, mass, cross-sectional area, and the bone's exact density/shear strength.
  - If Kinetic Energy > Shatter Threshold: The bone shatters, bullet loses energy but continues relatively straight.
  - If Kinetic Energy < Shatter Threshold (glancing angle or low energy): The bullet retains its structural integrity and deflects. Its velocity vector is mirrored across the bone normal, minus a calculated energy penalty.

## 3. Environmental Cover Penetration
**Goal:** Allow actors to take cover behind physical materials (Wood, Concrete, Steel) that mathematically reduce bullet energy.

- **Data Structure:** Do *not* use 3D voxels for the environment (to save memory for the 10 actors). Instead, model cover as **2D Polygons** with a designated `Thickness` property.
- **Material Profiles:** Create a `MaterialRegistry` (similar to `TissueRegistry`) with properties for `Density` ($kg/m^3$), `Hardness`, and `YieldStrength`.
- **Intersection Math:** During the ballistic trajectory phase, use 2D line-segment intersection to detect if the bullet path crosses a cover polygon. 
- **Energy Loss:** Apply the same aerodynamic drag / direct crush formula used in tissue, scaled by the material's density and the polygon's thickness. Calculate exit velocity and residual kinetic energy before the bullet reaches the actor.

## 4. Godot Camera Movement & Tracking
**Goal:** Allow the user to navigate the battlefield and inspect impacts closely.

- **Input Handling (`Camera3D`):**
  - **Pan:** WASD keys and Middle-Mouse drag.
  - **Zoom:** Mouse Scroll Wheel (adjusting the Camera's `Size` property for orthogonal/fov changes).
- **Event-Driven Auto-Tracking:** 
  - Implement a tracking state machine. By default, it is in `FreeRoam`.
  - When a `ShootTacticalAction` is fired, the camera automatically pans to the shooter.
  - The camera smoothly interpolates (lerps) its position to follow the projectile's X/Z coordinates during flight.
  - Upon impact, the camera zooms in slightly on the target dummy to clearly display the cavitation and UI medical reports.

## 5. Performance Scheduling
**Goal:** Ensure the simulation runs smoothly with 10 concurrent actors.

- **Decoupled Tick Rates:** 
  - **Micro-Ticks:** Ballistic flight and cavitation math must run at high precision (e.g., $1000$ Hz, or $0.001s$ steps) to prevent bullet tunneling.
  - **Macro-Ticks:** Physiological ticking (`TickPhysiology` - updating blood loss, heart rate, and hypoxia) is computationally heavy and does not need millisecond precision. It will be clamped to run globally at **$1.0$ Hz** (once per simulation second).
- **Spatial Hashing:** Ensure the newly implemented $O(1)$ spatial hash grid for voxels remains strictly bound to individual actors, so checking a bullet against Actor A doesn't waste CPU cycles checking Actor B's grid.
