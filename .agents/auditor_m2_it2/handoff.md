# Forensic Audit Report — Milestone 2: Material Penetration System (Iteration 2)

**Work Product**: Milestone 2 Material Penetration & Deflection Deliverables  
**Profile**: General Project  
**Integrity Mode**: Development (with Demo/Benchmark Strict Invariant Checks)  
**Verdict**: `CLEAN`

---

## 1. Observation

A complete forensic inspection and empirical execution was performed on all source files created and updated for Milestone 2:

### Inspected Files and Implementations:
1. `TacticalSim.Core/Materials/MaterialType.cs` (lines 1–18):
   - Defines standard barrier types: `Wood`, `Concrete`, `Steel`, `Glass`, `Drywall`, `Sand`, `Kevlar`, `Custom`.
2. `TacticalSim.Core/Materials/MaterialProperties.cs` (lines 1–55):
   - Declares `Name`, `Type`, `Density` ($\text{kg/m}^3$), `ResistanceCoefficient`, `RicochetAngleThreshold` ($\text{rad}$), `YieldEnergyThreshold` ($\text{J}$) with full constructor.
3. `TacticalSim.Core/Materials/IMaterialRegistry.cs` (lines 1–29):
   - Contract for `GetMaterial(MaterialType)`, `GetMaterial(string)`, `TryGetMaterial(string, out MaterialProperties)`, and `RegisterMaterial(MaterialProperties)`.
4. `TacticalSim.Core/Materials/MaterialRegistry.cs` (lines 1–146):
   - Implements thread-safe standard and custom registry via `ConcurrentDictionary<string, MaterialProperties>` (`OrdinalIgnoreCase`) and `ConcurrentDictionary<MaterialType, MaterialProperties>`.
   - Pre-populates validated material properties:
     - Wood: $\rho = 600\text{ kg/m}^3, C_{res} = 1.0, \theta_{ric} = 1.48\text{ rad}, E_{yield} = 50\text{ J}$.
     - Concrete: $\rho = 2400\text{ kg/m}^3, C_{res} = 1.8, \theta_{ric} = 1.31\text{ rad}, E_{yield} = 200\text{ J}$.
     - Steel: $\rho = 7850\text{ kg/m}^3, C_{res} = 2.5, \theta_{ric} = 1.22\text{ rad}, E_{yield} = 500\text{ J}$.
     - Glass: $\rho = 2500\text{ kg/m}^3, C_{res} = 0.5, \theta_{ric} = 1.48\text{ rad}, E_{yield} = 20\text{ J}$.
     - Drywall: $\rho = 800\text{ kg/m}^3, C_{res} = 0.4, \theta_{ric} = 1.52\text{ rad}, E_{yield} = 10\text{ J}$.
     - Sand: $\rho = 1600\text{ kg/m}^3, C_{res} = 1.5, \theta_{ric} = 1.55\text{ rad}, E_{yield} = 30\text{ J}$.
     - Kevlar: $\rho = 1440\text{ kg/m}^3, C_{res} = 3.2, \theta_{ric} = 1.48\text{ rad}, E_{yield} = 100\text{ J}$.
5. `TacticalSim.Core/Materials/PenetrationOutcome.cs` (lines 1–29):
   - Enum with `Perforated`, `Stopped`, `Ricochet`, `Miss`.
6. `TacticalSim.Core/Materials/PenetrationResult.cs` (lines 1–72):
   - Struct containing full kinematics and energy breakdown: `Outcome`, `EntryPoint`, `ExitPoint`, `EffectiveThickness`, `AngleOfIncidence`, `InitialVelocity`, `ExitVelocity`, `InitialKineticEnergy`, `RemainingKineticEnergy`, `TransferredKineticEnergy`, `ExitVelocityVector`, `ExitState`.
7. `TacticalSim.Core/Materials/IMaterialPenetrationSystem.cs` (lines 1–46):
   - Declares Overload 1 (planar slab: `nominalThickness`, `surfaceNormal`) and Overload 2 (explicit coordinates: `entryPoint`, `exitPoint`, `surfaceNormal`).
8. `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs` (lines 1–298):
   - Genuine analytical terminal ballistics equations:
     - Obliquity & Effective Thickness: $\cos\theta = |\hat{d} \cdot \hat{n}|$, $\theta = \arccos(\text{clamp}(\cos\theta, 0, 1))$, $T_{eff} = T_0 / \max(\cos\theta, 10^{-4})$.
     - Non-positive thickness handling (Iteration 2 fix): $T \le 0$ immediately returns `PenetrationOutcome.Perforated` with $v_{exit} = v_0, \vec{v}_{exit} = \vec{v}_0, E_{rem} = E_{k0}, \Delta E = 0, T_{eff} = 0$.
     - Stationary projectile handling: $v_0 < 10^{-6}\text{ m/s}$ returns `PenetrationOutcome.Stopped` with $v_{exit} = 0$.
     - Ricochet branch ($\theta \ge \theta_{ric}$): Specular reflection $\hat{d}_{refl} = \hat{d} - 2(\hat{d} \cdot \hat{n}_{outward})\hat{n}_{outward}$, energy damping $E_{loss} = E_{k0}(1 - \sin\theta) \times 0.3$, $E_{rem} = E_{k0} - E_{loss}, E_{trans} = E_{loss}, v_{exit} = \sqrt{2 E_{rem} / m}$.
     - Penetration & Retardation branch: Medium drag force $F_{drag} = \frac{1}{2} \rho C_{res} A v_0^2$, work-energy loss $\Delta E = \min(F_{drag} \cdot T_{eff}, E_{k0})$, $E_{rem} = E_{k0} - \Delta E, E_{trans} = \Delta E$. Perforation condition: $E_{rem} > 0.001\text{ J} \land E_{k0} \ge E_{yield}$. Arrested condition: depth $d = \min(E_{k0}/F_{drag}, T_{eff})$, stopped position $\vec{p}_{entry} + \hat{d} \cdot d$, $v_{exit} = 0, E_{trans} = E_{k0}$.
9. `TacticalSim.Tests/MaterialPenetrationTests.cs` (lines 1–960):
   - 20 unit tests with non-tautological physics assertions, hand-calculated analytical benchmarks, 10,000-trial invariant fuzz testing, continuous sweeps (150 density steps, 200 thickness steps), degenerate vector handling, and dedicated zero/negative thickness tests.

### Empirical Tool Execution Results:
```pwsh
# 1. Clean Build Execution
dotnet build --no-incremental
# Output: TacticalSim.Core and TacticalSim.Tests built successfully.
# M2 Files: 0 Warnings, 0 Errors.

# 2. Test Suite Execution
dotnet test
# Output: Total tests: 173. Passed: 173. Failed: 0. Skipped: 0. Duration: 252 ms.
```

---

## 2. Logic Chain

1. **Zero Hardcoding and Zero Facades**:
   - Inspection of `MaterialPenetrationSystem.cs` confirms that all branches compute exit velocities, directions, and kinetic energies via general-purpose physics equations ($E_k = \frac{1}{2} m v^2$, $F_{drag} = \frac{1}{2} \rho C_{res} A v^2$, $\vec{d}_{refl} = \vec{d} - 2(\vec{d} \cdot \vec{n})\vec{n}$, $v_{exit} = \sqrt{2 E_{rem} / m}$).
   - No conditional branches or magic constants target specific test inputs.
   - All methods in `MaterialRegistry` and `MaterialPenetrationSystem` are genuine implementations with input validation, concurrency safety, and full state propagation.

2. **Iteration 2 Zero/Negative Thickness Verification**:
   - In both Overload 1 (planar slab) and Overload 2 (explicit 3D points), active projectiles traversing $T \le 0$ medium undergo zero drag work ($\Delta E = 0$).
   - The system preserves initial velocity ($\vec{v}_{exit} = \vec{v}_0$), preserves initial kinetic energy ($E_{rem} = E_{k0}$), records zero transferred energy ($E_{trans} = 0$), and returns `PenetrationOutcome.Perforated`.
   - Stationary projectiles ($v_0 < 10^{-6}\text{ m/s}$) are cleanly decoupled and return `PenetrationOutcome.Stopped`.
   - Verified across `MaterialPenetrationTests.cs`, `MaterialPenetrationAdversarialTests.cs`, `MaterialPenetrationEmpiricalChallengerTests.cs`, and `MaterialPenetrationChallenger2Tests.cs`.

3. **Strict Conservation of Energy**:
   - Under all terminal outcomes (`Perforated`, `Stopped`, `Ricochet`), total kinetic energy satisfies $E_{k0} \equiv E_{remaining} + E_{transferred}$.
   - Verified empirically across 10,000 randomized combinatorial fuzzing trials with zero NaNs, zero Infinities, and zero energy leaks.

4. **Non-Tautological Assertions**:
   - Test suites assert outcomes against independently derived analytical values (e.g., $E_{k0}=1280\text{ J}$, $F_d=9600\text{ N}$, $\Delta E=96\text{ J}$, $E_{rem}=1184\text{ J}$, $v_{exit}=769.415\text{ m/s}$), mathematical monotonicity relations, and specular reflection invariants.

---

## 3. Caveats

- **Kinetic Penetrator Assumption**: The terminal ballistics model assumes non-deforming kinetic projectiles (constant cross-sectional area and constant mass through the medium).
- **Single-Layer Slab Formulation**: Multi-layered composite barriers are evaluated by sequential calls per layer.
- **Pre-existing Warning**: The single build warning `CS8618` in `ActorPhysiology.cs` is pre-existing legacy code slated for cleanup in Milestone 3 (Feature F11). No warnings exist in Milestone 2 code.

---

## 4. Conclusion

**Verdict: `CLEAN`**

The Milestone 2 Material Penetration System implementation strictly complies with all forensic integrity standards, architecture specifications, and physics invariants. All 173 solution tests pass with zero failures and zero integrity violations.

---

## 5. Verification Method

To independently reproduce and verify the audit findings:

```pwsh
# 1. Compile solution
dotnet build

# 2. Run all Material Penetration test suites
dotnet test --filter "FullyQualifiedName~MaterialPenetration"

# 3. Run full project test suite
dotnet test
```

### Invalidation Conditions:
- If any material penetration calculation for $T \le 0$ produces $E_{transferred} > 0$ or changes projectile velocity for non-stationary projectiles, the implementation is INVALID.
- If total energy $E_{remaining} + E_{transferred} \ne E_{initial}$ for any test case, the implementation is INVALID.
- If any method in `TacticalSim.Core.Materials` contains hardcoded test constants or facade returns, the implementation is INVALID.
- If `dotnet test` fails any test case, the implementation is INVALID.
