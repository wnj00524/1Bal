# Handoff Report — Issue #4 (Material Penetration System) & R3 (Architectural Decoupling)

## 1. Observation

### 1.1 Codebase & Project State
- **Solution & Projects**:
  - `TacticalSim.slnx`: Contains `TacticalSim.Core` and `TacticalSim.Tests`.
  - `TacticalSim.Core/TacticalSim.Core.csproj`: Targets `net8.0`, has `<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.11" />`.
  - `TacticalSim.Tests/TacticalSim.Tests.csproj`: References `TacticalSim.Core`, `xunit` (2.5.3), `Microsoft.NET.Test.Sdk` (17.8.0), `coverlet.collector` (6.0.0).
- **Existing Ballistics & Physiology Systems**:
  - `TacticalSim.Core/BallisticSolver.cs` (`TacticalSim.Core.Ballistics`):
    - `ProjectileState`: `Vector3 Position`, `Vector3 Velocity`, `float Time`.
    - `BallisticProfile`: `float Mass` (kg), `float CrossSectionalArea` ($m^2$), `IDragModel DragModel`.
    - `BallisticSolver.StepRK4(...)`: 4th-order Runge-Kutta numerical integrator with aerodynamic drag $F_d = 0.5 \cdot \rho_{air} \cdot C_d \cdot A \cdot v^2$.
  - `TacticalSim.Core/DragModels.cs`: `IDragModel`, `StandardDragCurve` (transonic Mach drag rise).
  - `TacticalSim.Core/Environment.cs`: `IEnvironmentModel`, `EnvironmentState`, `ICAOStandardAtmosphere`.
  - `TacticalSim.Core/Physiology/PhysiologicalVoxel.cs`:
    - Lines 92-123 (`ProcessPenetration`): Calculates ray-AABB intersection distance $d$; computes initial kinetic energy $E_0 = \frac{1}{2} m v^2$; computes tissue drag force $F_d = 0.5 \cdot \rho_{tissue} \cdot v^2 \cdot C_d \cdot A$; computes energy lost $E_{lost} = \min(F_d \cdot d, E_0)$; updates projectile exit velocity $v_{exit} = \sqrt{\frac{2(E_0 - E_{lost})}{m}}$ and advances position.
  - `TacticalSim.Core/Physiology/TissueRegistry.cs`: Predefined tissues (`Muscle`, `Bone`, `Lung`, `Liver`, `Brain`) with `Density`, `Elasticity`, `ShearStrength`.
  - `TacticalSim.Core/Simulation/TurnResolution.cs`: Defines abstract `TacticalAction` and interface `ITurnResolver`.
- **Existing Tests**:
  - `TacticalSim.Tests/BallisticSolverTests.cs`: 2 unit tests (`VacuumTrajectory_FollowsParabolicKinematics`, `AtmosphericTrajectory_ExhibitsNonLinearDrag`).
  - Executing `dotnet test` yields **2 passed, 0 failed, duration ~15ms**.

### 1.2 Requirements from ORIGINAL_REQUEST.md & agents.md
- **R2 (Issue #4 Material Penetration System)**:
  - Terminal ballistics system for environmental cover materials (Wood, Concrete, Steel, etc.).
  - Calculate velocity loss and kinetic energy transfer based on material density and penetration thickness.
  - Test verification: Projectile loses velocity proportionally to target material's density and thickness, and exits with mathematically correct reduced kinetic energy.
- **R3 (Architectural Decoupling)**:
  - All implementations must remain strictly isolated within `TacticalSim.Core`.
  - Service registration must rely on `Microsoft.Extensions.DependencyInjection`.
  - Mathematical simulation decoupled from UI/rendering engines.
  - Use `System.Numerics.Vector3` for $R^3$ vector math.

---

## 2. Logic Chain

1. **Terminal Ballistics Separation**:
   - External ballistics (`BallisticSolver`) models atmospheric flight over time steps $dt$.
   - Physiological trauma (`PhysiologicalVoxel`) models human anatomy cavitation and hemorrhage.
   - Environmental cover penetration (`IMaterialPenetrationSystem`) requires a dedicated terminal ballistics domain model capable of handling non-biological physical barriers, variable angles of incidence, ricochet conditions, and structured penetration outcomes (`Perforated` vs `Stopped`).

2. **Mathematical Model for Penetration**:
   - **Effective Thickness ($T_{eff}$)**:
     For a planar barrier with nominal thickness $T_0$, surface normal $\hat{n}$, and incident trajectory $\hat{d} = \frac{\vec{v}}{|\vec{v}|}$:
     $$\cos \theta = |-\hat{d} \cdot \hat{n}| = |\hat{d} \cdot \hat{n}|$$
     $$T_{eff} = \frac{T_0}{\cos \theta}$$
     For a 3D geometry / bounding volume (AABB/OBB), $T_{eff} = \|\vec{p}_{exit} - \vec{p}_{entry}\|$.
   - **Work-Energy & Drag Retardation**:
     In a dense solid medium of density $\rho_{mat}$ with material resistance factor $C_{mat}$, the retarding force on a projectile of cross-sectional area $A$ and speed $v$ is:
     $$F_d = \frac{1}{2} \rho_{mat} C_{mat} A v^2$$
     Work done (energy transferred to material) over distance $T_{eff}$:
     $$\Delta E_k = \min\left( F_d \cdot T_{eff}, E_{k0} \right) \quad \text{where } E_{k0} = \frac{1}{2} m v^2$$
     Exit kinetic energy:
     $$E_{exit} = E_{k0} - \Delta E_k$$
     Exit speed:
     $$v_{exit} = \begin{cases} \sqrt{\frac{2 E_{exit}}{m}} & \text{if } E_{exit} > 0 \\ 0 & \text{if } E_{exit} \le 0 \end{cases}$$
   - **Alternative Exponential Closed-Form Integration**:
     $$\frac{dv}{dx} = -\frac{\rho_{mat} C_{mat} A}{2m} v \implies v_{exit} = v_0 \exp\left(-\frac{\rho_{mat} C_{mat} A T_{eff}}{2m}\right)$$
     $$E_{exit} = E_{k0} \exp\left(-\frac{\rho_{mat} C_{mat} A T_{eff}}{m}\right), \quad E_{transferred} = E_{k0} - E_{exit}$$
   - **Conservation of Energy**:
     $$E_{k0} = E_{exit} + E_{transferred}$$
     This holds strictly across all test cases.

3. **Outcome Resolution**:
   - If incident angle $\theta \ge \theta_{ricochet\_critical}$ and kinetic energy is insufficient to bite into the surface $\implies$ `PenetrationOutcome.Ricochet`.
   - If $E_{exit} > E_{threshold}$ (or $v_{exit} > v_{min}$) $\implies$ `PenetrationOutcome.Perforated`.
   - If $E_{exit} \le E_{threshold}$ $\implies$ `PenetrationOutcome.Stopped` ($v_{exit} = 0$, all initial energy transferred to barrier).

4. **Dependency Injection & Architectural Decoupling**:
   - Add service collection extensions in `TacticalSim.Core.DependencyInjection` (e.g. `AddTacticalSimCore(...)`, `AddMaterialPenetration(...)`, `AddSimulationServices(...)`).
   - Register singleton registries (`IMaterialRegistry`) and transient/singleton solvers (`IMaterialPenetrationSystem`, `ITurnResolver`).
   - No references to UI, game engines, or non-deterministic APIs.

---

## 3. Caveats

1. **Isotropic Material Assumption**: Materials are modeled with homogeneous density and structural resistance. Heterogeneous or layered composites (e.g., steel-reinforced concrete or ceramic composite armor) should be represented as sequential multi-layer penetrations.
2. **Deformation / Yawing**: Standard kinetic projectile geometry ($A, m$) is held constant during penetration unless a dynamic projectile deformation factor or armor-piercing multiplier is provided.
3. **Ricochet Physics**: Ricochet is modeled via a critical angle of incidence threshold ($\theta_{crit}$, typically $70^\circ-80^\circ$ for hard surfaces) with partial energy dissipation along the reflected vector $\vec{r} = \vec{d} - 2(\vec{d}\cdot\hat{n})\hat{n}$.

---

## 4. Conclusion & Detailed Domain Specification

### 4.1 Domain Data Structures (`TacticalSim.Core.Materials` / `TacticalSim.Core.Ballistics`)

```csharp
namespace TacticalSim.Core.Materials
{
    public enum MaterialType
    {
        Wood,
        Concrete,
        Steel,
        Glass,
        Drywall,
        Sand,
        Kevlar,
        Custom
    }

    public struct MaterialProperties
    {
        public string Name { get; set; }
        public MaterialType Type { get; set; }
        public float Density { get; set; }               // kg/m^3 (e.g., Wood ~600, Concrete ~2400, Steel ~7850)
        public float ResistanceCoefficient { get; set; }  // Dimensionless medium drag / resistance multiplier (e.g., 1.0 - 2.5)
        public float RicochetAngleThreshold { get; set; } // Critical angle in radians (e.g., 1.3 rad ~ 75 degrees)
        public float YieldEnergyThreshold { get; set; }   // Minimum energy in Joules required to initiate penetration
    }

    public interface IMaterialRegistry
    {
        MaterialProperties GetMaterial(MaterialType type);
        MaterialProperties GetMaterial(string name);
        bool TryGetMaterial(string name, out MaterialProperties material);
        void RegisterMaterial(MaterialProperties material);
    }
}
```

#### Predefined Material Constants:
- **Wood (Pine/Hardwood)**: Density = $600.0\text{ kg/m}^3$, Resistance = $1.0$, RicochetThreshold = $1.48\text{ rad } (85^\circ)$.
- **Concrete (Reinforced)**: Density = $2400.0\text{ kg/m}^3$, Resistance = $1.8$, RicochetThreshold = $1.31\text{ rad } (75^\circ)$.
- **Steel (Structural/Armor)**: Density = $7850.0\text{ kg/m}^3$, Resistance = $2.5$, RicochetThreshold = $1.22\text{ rad } (70^\circ)$.
- **Glass**: Density = $2500.0\text{ kg/m}^3$, Resistance = $0.5$, RicochetThreshold = $1.48\text{ rad } (85^\circ)$.
- **Drywall / Plaster**: Density = $800.0\text{ kg/m}^3$, Resistance = $0.4$, RicochetThreshold = $1.52\text{ rad } (87^\circ)$.
- **Sand / Dirt**: Density = $1600.0\text{ kg/m}^3$, Resistance = $1.5$, RicochetThreshold = $1.55\text{ rad } (89^\circ)$.
- **Kevlar / Ballistic Fiber**: Density = $1440.0\text{ kg/m}^3$, Resistance = $3.2$, RicochetThreshold = $1.48\text{ rad } (85^\circ)$.

---

### 4.2 Penetration Result & System Interface

```csharp
namespace TacticalSim.Core.Materials
{
    public enum PenetrationOutcome
    {
        Perforated, // Passed through barrier with exit velocity > 0
        Stopped,    // Projectile embedded/stopped inside barrier (velocity = 0)
        Ricochet,   // Deflected off surface due to high obliquity
        Miss        // Ray did not intersect barrier
    }

    public struct PenetrationResult
    {
        public PenetrationOutcome Outcome { get; set; }
        public Vector3 EntryPoint { get; set; }
        public Vector3 ExitPoint { get; set; }
        public float EffectiveThickness { get; set; }       // meters
        public float AngleOfIncidence { get; set; }        // radians
        public float InitialVelocity { get; set; }         // m/s
        public float ExitVelocity { get; set; }            // m/s
        public float InitialKineticEnergy { get; set; }    // Joules
        public float RemainingKineticEnergy { get; set; }  // Joules
        public float TransferredKineticEnergy { get; set; }// Joules
        public Vector3 ExitVelocityVector { get; set; }    // Vector3 (m/s)
        public ProjectileState ExitState { get; set; }     // Updated projectile state
    }

    public interface IMaterialPenetrationSystem
    {
        /// <summary>
        /// Calculates projectile penetration through a planar material slab with nominal thickness and surface normal.
        /// </summary>
        PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            float nominalThickness,
            Vector3 surfaceNormal);

        /// <summary>
        /// Calculates projectile penetration between explicit geometric entry and exit points.
        /// </summary>
        PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            Vector3 entryPoint,
            Vector3 exitPoint,
            Vector3 surfaceNormal);
    }
}
```

---

### 4.3 Exact Mathematical Formulations

1. **Obliquity Angle**:
   Given projectile trajectory unit direction $\hat{d} = \frac{\vec{v}}{\|\vec{v}\|}$ and outward barrier surface normal $\hat{n}$:
   $$\cos \theta = |-\hat{d} \cdot \hat{n}| = |\hat{d} \cdot \hat{n}| \quad \implies \quad \theta = \arccos\left(\text{clamp}(|\hat{d} \cdot \hat{n}|, 0, 1)\right)$$
2. **Effective Thickness**:
   $$T_{eff} = \frac{T_0}{\max(\cos \theta, 10^{-4})}$$
   Or from explicit coordinates:
   $$T_{eff} = \|\vec{p}_{exit} - \vec{p}_{entry}\|$$
3. **Kinetic Energy & Drag Loss**:
   $$E_{k0} = \frac{1}{2} \cdot m \cdot v_0^2$$
   $$F_{drag} = \frac{1}{2} \cdot \rho_{material} \cdot C_{resistance} \cdot A \cdot v_0^2$$
   $$E_{loss} = \min\left(F_{drag} \cdot T_{eff}, E_{k0}\right)$$
   $$E_{remaining} = E_{k0} - E_{loss}$$
   $$E_{transferred} = E_{loss}$$
4. **Exit Kinematics**:
   If $E_{remaining} > 0.001\text{ J}$ and $\theta < \theta_{ricochet}$:
   $$v_{exit} = \sqrt{\frac{2 \cdot E_{remaining}}{m}}$$
   $$\vec{v}_{exit} = \hat{d} \cdot v_{exit}$$
   $$\text{Outcome} = \text{PenetrationOutcome.Perforated}$$
   $$\vec{p}_{exit} = \vec{p}_{entry} + \hat{d} \cdot T_{eff}$$
   Else:
   $$v_{exit} = 0$$
   $$\vec{v}_{exit} = \vec{0}$$
   $$\text{Outcome} = \text{PenetrationOutcome.Stopped}$$
   $$E_{transferred} = E_{k0}, \quad E_{remaining} = 0$$
5. **Ricochet Outcome**:
   If $\theta \ge \theta_{ricochet}$:
   $$\vec{d}_{ricochet} = \hat{d} - 2(\hat{d} \cdot \hat{n})\hat{n}$$
   $$E_{loss} = E_{k0} \cdot (1 - \sin \theta) \cdot 0.3$$
   $$E_{remaining} = E_{k0} - E_{loss}$$
   $$v_{exit} = \sqrt{\frac{2 E_{remaining}}{m}}$$
   $$\vec{v}_{exit} = \vec{d}_{ricochet} \cdot v_{exit}$$
   $$\text{Outcome} = \text{PenetrationOutcome.Ricochet}$$

---

### 4.4 Dependency Injection Architecture (`R3`)

```csharp
namespace TacticalSim.Core.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTacticalSimCore(this IServiceCollection services)
        {
            // Material & Penetration Services
            services.AddSingleton<IMaterialRegistry, MaterialRegistry>();
            services.AddTransient<IMaterialPenetrationSystem, MaterialPenetrationSystem>();

            // External Ballistics & Environment
            services.AddSingleton<IEnvironmentModel>(sp => new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0)));
            services.AddSingleton<IDragModel>(sp => new StandardDragCurve(0.3f));

            // Turn Resolution Services (Issue #3)
            services.AddTransient<ITurnResolver, FractionatedTurnResolver>();

            return services;
        }

        public static IServiceCollection AddMaterialPenetration(this IServiceCollection services)
        {
            services.AddSingleton<IMaterialRegistry, MaterialRegistry>();
            services.AddTransient<IMaterialPenetrationSystem, MaterialPenetrationSystem>();
            return services;
        }

        public static IServiceCollection AddSimulationServices(this IServiceCollection services)
        {
            services.AddTransient<ITurnResolver, FractionatedTurnResolver>();
            return services;
        }
    }
}
```

---

### 4.5 Testing Matrix for `TacticalSim.Tests`

| Test Category | Test Case Name | Input Parameters | Expected Invariant / Outcome |
|---|---|---|---|
| **Density Monotonicity** | `Penetration_VelocityLoss_MonotonicWithDensity` | $m=0.004\text{kg}, v=900\text{m/s}, T_0=0.05\text{m}$ across Wood ($\rho=600$), Concrete ($\rho=2400$), Steel ($\rho=7850$) | $v_{exit,Wood} > v_{exit,Concrete} > v_{exit,Steel}$, $E_{loss,Wood} < E_{loss,Concrete} < E_{loss,Steel}$ |
| **Thickness Monotonicity** | `Penetration_VelocityLoss_MonotonicWithThickness` | Identical projectile across Wood of $T_0=0.02\text{m}, 0.05\text{m}, 0.10\text{m}$ | $v_{exit}(0.02) > v_{exit}(0.05) > v_{exit}(0.10)$ |
| **Obliquity / Angle** | `Penetration_AngledImpact_IncreasesEffectiveThickness` | Impact at $\theta=0^\circ$ vs $\theta=45^\circ$ vs $\theta=60^\circ$ through $0.05\text{m}$ barrier | $T_{eff}(60^\circ) = 2 \cdot T_0$, $E_{loss}(60^\circ) > E_{loss}(45^\circ) > E_{loss}(0^\circ)$ |
| **Energy Conservation** | `Penetration_ConservesTotalKineticEnergy` | Random valid test profiles across all materials and thicknesses | $E_{transferred} + E_{remaining} == E_{k0} \pm 10^{-4}\text{ J}$ |
| **Complete Perforation** | `Penetration_ThinBarrier_PerforatesWithCorrectExitEnergy` | $0.01\text{m}$ Wood barrier, high energy round | Outcome is `Perforated`, $v_{exit} > 0$, $E_{remaining} > 0$ |
| **Complete Absorption** | `Penetration_ThickBarrier_StopsProjectile` | $0.50\text{m}$ Steel barrier, pistol round | Outcome is `Stopped`, $v_{exit} == 0$, $E_{transferred} == E_{k0}$, $E_{remaining} == 0$ |
| **Ricochet Grazing Angle** | `Penetration_HighObliquity_TriggersRicochet` | Incident angle $\theta = 82^\circ$ ($> 70^\circ$ Steel threshold) | Outcome is `Ricochet`, trajectory is reflected across normal |
| **DI Resolution** | `ServiceCollection_AddTacticalSimCore_ResolvesAllServices` | Build `ServiceCollection`, call `.AddTacticalSimCore()` | `IMaterialPenetrationSystem`, `IMaterialRegistry`, `ITurnResolver`, `IEnvironmentModel` resolve successfully |

---

## 5. Verification Method

To independently verify these findings and all subsequent implementation deliverables:
1. **Compilation Check**:
   ```pwsh
   dotnet build TacticalSim.slnx --configuration Debug
   ```
   *Expectation*: Zero errors, zero warnings.
2. **Automated Test Run**:
   ```pwsh
   dotnet test TacticalSim.slnx --logger "console;verbosity=detailed"
   ```
   *Expectation*: All unit tests in `TacticalSim.Tests` pass with 100% success rate.
3. **DI Integrity Test**:
   Execute programmatic test ensuring `new ServiceCollection().AddTacticalSimCore().BuildServiceProvider()` can resolve `IMaterialPenetrationSystem`, `IMaterialRegistry`, and `ITurnResolver`.
4. **Invalidation Conditions**:
   - If velocity loss fails to increase monotonically with material density or thickness.
   - If kinetic energy is not strictly conserved ($E_{k0} \ne E_{remaining} + E_{transferred}$).
   - If DI services fail to register or require manual instantiation bypassing `IServiceProvider`.
