# Scope: Milestone 2 — Material Penetration System

## Objective
Implement Issue #4 (Material Penetration System) within `TacticalSim.Core.Materials` and comprehensive unit tests in `TacticalSim.Tests/MaterialPenetrationTests.cs`.

## Exclusive Write Ownership
The worker(s) in this milestone own and may modify ONLY:
- `TacticalSim.Core/Materials/MaterialType.cs`
- `TacticalSim.Core/Materials/MaterialProperties.cs`
- `TacticalSim.Core/Materials/IMaterialRegistry.cs`
- `TacticalSim.Core/Materials/MaterialRegistry.cs`
- `TacticalSim.Core/Materials/PenetrationOutcome.cs`
- `TacticalSim.Core/Materials/PenetrationResult.cs`
- `TacticalSim.Core/Materials/IMaterialPenetrationSystem.cs`
- `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs`
- `TacticalSim.Tests/MaterialPenetrationTests.cs`

## Key Requirements
1. Environmental Cover Materials: Predefined materials (`Wood`, `Concrete`, `Steel`, `Glass`, `Drywall`, `Sand`, `Kevlar`) and dynamic custom material registration.
2. Material Properties: Density ($\text{kg/m}^3$), Resistance coefficient, Ricochet angle threshold, and Yield energy threshold.
3. Obliquity & Effective Thickness: $T_{eff} = T_0 / \cos\theta$ (or explicit 3D entry/exit points distance).
4. Terminal Ballistics Retardation & Energy:
   - Initial kinetic energy: $E_{k0} = \frac{1}{2} m v_0^2$.
   - Drag force: $F_{drag} = \frac{1}{2} \rho_{mat} C_{res} A v_0^2$.
   - Work-energy loss: $\Delta E = \min(F_{drag} \cdot T_{eff}, E_{k0})$.
   - Exit energy: $E_{exit} = E_{k0} - \Delta E$.
   - Exit velocity: $v_{exit} = \sqrt{\frac{2 E_{exit}}{m}}$ (if $E_{exit} > 0$, else $0$).
   - Strict conservation of energy: $E_{k0} = E_{exit} + E_{transferred}$.
5. Outcome Classification: `Perforated`, `Stopped`, `Ricochet`, `Miss` with updated `ProjectileState` and direction vectors.
6. Programmatic xUnit tests covering density monotonicity, thickness monotonicity, obliquity angle scaling, energy conservation, perforation, stopping, ricochet, and custom material registration.

## References
- `c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md`
- `c:\Users\jdwil\source\repos\Codex\1bal\.agents\explorer_survey_3\handoff.md`
