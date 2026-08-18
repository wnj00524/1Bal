## 2026-08-17T21:24:26Z
You are the Worker for Milestone 2: Material Penetration System in TacticalSim.
Your working directory for metadata/reports is: c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m2

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Context Files:
- Original Request: c:\Users\jdwil\source\repos\Codex\1bal\ORIGINAL_REQUEST.md
- Project Document: c:\Users\jdwil\source\repos\Codex\1bal\PROJECT.md
- Milestone Scope: c:\Users\jdwil\source\repos\Codex\1bal\.agents\sub_orch_m2\SCOPE.md
- Reference Analysis: c:\Users\jdwil\source\repos\Codex\1bal\.agents\explorer_survey_3\handoff.md

Your Exclusive Write Ownership (you may ONLY create/modify these files):
1. `TacticalSim.Core/Materials/MaterialType.cs`
2. `TacticalSim.Core/Materials/MaterialProperties.cs`
3. `TacticalSim.Core/Materials/IMaterialRegistry.cs`
4. `TacticalSim.Core/Materials/MaterialRegistry.cs`
5. `TacticalSim.Core/Materials/PenetrationOutcome.cs`
6. `TacticalSim.Core/Materials/PenetrationResult.cs`
7. `TacticalSim.Core/Materials/IMaterialPenetrationSystem.cs`
8. `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs`
9. `TacticalSim.Tests/MaterialPenetrationTests.cs`

Requirements:
1. `MaterialType.cs` (`namespace TacticalSim.Core.Materials`):
   - Enum: `Wood`, `Concrete`, `Steel`, `Glass`, `Drywall`, `Sand`, `Kevlar`, `Custom`.
2. `MaterialProperties.cs`:
   - Struct `MaterialProperties`:
     - `public string Name { get; set; }`
     - `public MaterialType Type { get; set; }`
     - `public float Density { get; set; }` (kg/m^3)
     - `public float ResistanceCoefficient { get; set; }` (dimensionless multiplier)
     - `public float RicochetAngleThreshold { get; set; }` (radians)
     - `public float YieldEnergyThreshold { get; set; }` (Joules)
3. `IMaterialRegistry.cs` & `MaterialRegistry.cs`:
   - Interface:
     - `MaterialProperties GetMaterial(MaterialType type);`
     - `MaterialProperties GetMaterial(string name);`
     - `bool TryGetMaterial(string name, out MaterialProperties material);`
     - `void RegisterMaterial(MaterialProperties material);`
   - Pre-register standard materials with realistic physical constants:
     - Wood: Density = 600 kg/m^3, Resistance = 1.0, RicochetThreshold = 1.48 rad (85 deg), Yield = 50 J.
     - Concrete: Density = 2400 kg/m^3, Resistance = 1.8, RicochetThreshold = 1.31 rad (75 deg), Yield = 200 J.
     - Steel: Density = 7850 kg/m^3, Resistance = 2.5, RicochetThreshold = 1.22 rad (70 deg), Yield = 500 J.
     - Glass: Density = 2500 kg/m^3, Resistance = 0.5, RicochetThreshold = 1.48 rad (85 deg), Yield = 20 J.
     - Drywall: Density = 800 kg/m^3, Resistance = 0.4, RicochetThreshold = 1.52 rad (87 deg), Yield = 10 J.
     - Sand: Density = 1600 kg/m^3, Resistance = 1.5, RicochetThreshold = 1.55 rad (89 deg), Yield = 30 J.
     - Kevlar: Density = 1440 kg/m^3, Resistance = 3.2, RicochetThreshold = 1.48 rad (85 deg), Yield = 100 J.
   - Dynamic custom registration and thread-safe lookup.
4. `PenetrationOutcome.cs`:
   - Enum: `Perforated`, `Stopped`, `Ricochet`, `Miss`.
5. `PenetrationResult.cs`:
   - Struct:
     - `public PenetrationOutcome Outcome { get; set; }`
     - `public Vector3 EntryPoint { get; set; }`
     - `public Vector3 ExitPoint { get; set; }`
     - `public float EffectiveThickness { get; set; }` (meters)
     - `public float AngleOfIncidence { get; set; }` (radians)
     - `public float InitialVelocity { get; set; }` (m/s)
     - `public float ExitVelocity { get; set; }` (m/s)
     - `public float InitialKineticEnergy { get; set; }` (Joules)
     - `public float RemainingKineticEnergy { get; set; }` (Joules)
     - `public float TransferredKineticEnergy { get; set; }` (Joules)
     - `public Vector3 ExitVelocityVector { get; set; }` (m/s)
     - `public ProjectileState ExitState { get; set; }` (updated ProjectileState from TacticalSim.Core.Ballistics)
6. `IMaterialPenetrationSystem.cs` & `MaterialPenetrationSystem.cs`:
   - Overloads:
     - `PenetrationResult CalculatePenetration(in ProjectileState projectile, in BallisticProfile profile, in MaterialProperties material, float nominalThickness, Vector3 surfaceNormal)`
     - `PenetrationResult CalculatePenetration(in ProjectileState projectile, in BallisticProfile profile, in MaterialProperties material, Vector3 entryPoint, Vector3 exitPoint, Vector3 surfaceNormal)`
   - Exact Physics:
     - Obliquity: unit direction `d = Normalize(projectile.Velocity)`.
       Angle of incidence $\theta = \arccos(\text{clamp}(|-\hat{d} \cdot \hat{n}|, 0, 1))$. Note: incident angle relative to normal, where normal points outward from barrier.
     - Effective thickness: $T_{eff} = T_0 / \max(\cos\theta, 1e-4)$ or $T_{eff} = \|\vec{p}_{exit} - \vec{p}_{entry}\|$.
     - Initial kinetic energy: $E_{k0} = \frac{1}{2} m v_0^2$.
     - If $\theta \ge \text{RicochetAngleThreshold}$:
       - Outcome: `Ricochet`.
       - Reflected direction: $\vec{d}_{refl} = \hat{d} - 2(\hat{d} \cdot \hat{n})\hat{n}$.
       - Energy loss: $E_{loss} = E_{k0} \cdot (1 - \sin\theta) \cdot 0.3f$.
       - $E_{rem} = E_{k0} - E_{loss}$, $E_{trans} = E_{loss}$.
       - $v_{exit} = \sqrt{\frac{2 E_{rem}}{m}}$, $\vec{v}_{exit} = \vec{d}_{refl} \cdot v_{exit}$.
       - Exit state position = entry point.
     - Else (penetration attempt):
       - Drag force: $F_{drag} = \frac{1}{2} \cdot \rho_{mat} \cdot C_{res} \cdot A \cdot v_0^2$.
       - Energy loss: $\Delta E = \min(F_{drag} \cdot T_{eff}, E_{k0})$.
       - $E_{rem} = E_{k0} - \Delta E$.
       - $E_{trans} = $\Delta E.
       - Conservation of energy: strictly $E_{k0} == E_{rem} + E_{trans}$.
       - If $E_{rem} > 0.001f$ and $E_{k0} \ge material.YieldEnergyThreshold$:
         - Outcome: `Perforated`.
         - $v_{exit} = \sqrt{\frac{2 E_{rem}}{m}}$.
         - $\vec{v}_{exit} = \hat{d} \cdot v_{exit}$.
         - Exit position = entryPoint + $\hat{d} \cdot T_{eff}$ (or explicit exit point).
       - Else:
         - Outcome: `Stopped`.
         - $v_{exit} = 0$, $\vec{v}_{exit} = \text{Vector3.Zero}$.
         - $E_{rem} = 0$, $E_{trans} = E_{k0}$.
7. `TacticalSim.Tests/MaterialPenetrationTests.cs`:
   - Comprehensive unit tests covering:
     - `Penetration_VelocityLoss_MonotonicWithDensity` (Wood < Concrete < Steel for energy loss, Wood > Concrete > Steel for exit velocity).
     - `Penetration_VelocityLoss_MonotonicWithThickness` ($T=0.02, 0.05, 0.10$).
     - `Penetration_AngledImpact_IncreasesEffectiveThickness` ($\theta=0^\circ, 45^\circ, 60^\circ$).
     - `Penetration_ConservesTotalKineticEnergy` ($E_{k0} == E_{rem} + E_{trans}$).
     - `Penetration_ThinBarrier_PerforatesWithCorrectExitEnergy`.
     - `Penetration_ThickBarrier_StopsProjectile`.
     - `Penetration_HighObliquity_TriggersRicochet`.
     - `MaterialRegistry_StandardMaterials_ArePreloaded`.
     - `MaterialRegistry_CustomMaterial_RegistersAndRetrievesCorrectly`.
     - `CalculatePenetration_ExplicitCoordinates_CalculatesCorrectly`.

Verification:
- Run `dotnet build` ensuring 0 errors and 0 warnings.
- Run `dotnet test` ensuring all tests pass.
- Write full handoff report to `c:\Users\jdwil\source\repos\Codex\1bal\.agents\worker_m2\handoff.md`.
- Send message to sub-orchestrator when complete.
