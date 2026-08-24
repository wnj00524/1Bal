# M10 isometric tactical integration

M10 connects the authoritative M7 capability output to tactical decisions without allowing actions, AI, scoring, or presentation to inspect organ damage.

## Contracts

- `CapabilityActionPolicy` is the single translation point for movement, posture, aiming, firing, reloading, command, self-aid, and rescue. Capacity below `0.2` blocks an action; otherwise healthy TU cost is divided by capacity. Firing also publishes capacity as its stability factor.
- `CasualtyTransportAction` requires the rescuer to be within 2 metres, checks movement and upper-limb capability, moves both entities through `ITacticalWorld`, blocks weapon use, and records time spent exposed. Drag uses 45% and carry 35% of capability-adjusted healthy speed. Its optional movement callback lets scenario composition interrupt sustained treatment or apply configured position/orientation effects.
- `CasualtyBehaviorPolicy` and `TeammateResponsePolicy` are deterministic. They receive only observable capability/status and mission context, never lesions or anatomy.
- `CasualtyOverlayFactory` produces the ordinary effective/impaired/critical/unconscious/dead contract. Exact authoritative debug strings are returned only when debug mode is explicitly enabled; reading them does not mutate simulation state.
- `CasualtyScenarioScorer` reports mission completion, survival, neutralization, evacuation, delay, exposure, and resources as separate dimensions. Rescue can be mandatory, optional, or irrelevant.
- The Godot scenario composes an `IntegratedV3` dummy. Projectile lesions are applied to its Core-owned `ActorMedicalState`; the compatibility physiology surface projects authoritative casualty/capability values into existing movement, firing, cancellation, treatment-menu, and overlay consumers. The medical report renders the same snapshot and does not calculate a competing `1500 ml / bleed rate` prognosis for integrated actors.

The `0.2` action gate, `0.75` impairment display threshold, transport speed factors, and default score weights are **provisional gameplay tuning**. They are not medical claims. Navigation-path planning, threat-field calculation, and the concrete Godot rendering style remain presentation/scenario responsibilities; the core contracts provide deterministic positions, exposure, and overlay data without duplicating physiology.
