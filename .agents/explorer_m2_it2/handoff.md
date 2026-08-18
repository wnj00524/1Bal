# Handoff Report — Explorer Milestone 2 (Iteration 2)

## 1. Observation

### 1.1 Bug Diagnosis in `MaterialPenetrationSystem.cs`
In `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs`:

**Overload 1: Planar Slab with Nominal Thickness (Lines 20–45)**:
```csharp
Vector3 entryPoint = projectile.Position;
float speed = projectile.Velocity.Length();

if (speed < 1e-6f || nominalThickness <= 0f)
{
    float eZero = 0.5f * profile.Mass * speed * speed;
    return new PenetrationResult
    {
        Outcome = PenetrationOutcome.Stopped,
        EntryPoint = entryPoint,
        ExitPoint = entryPoint,
        EffectiveThickness = MathF.Max(0f, nominalThickness),
        AngleOfIncidence = 0f,
        InitialVelocity = speed,
        ExitVelocity = 0f,
        InitialKineticEnergy = eZero,
        RemainingKineticEnergy = 0f,
        TransferredKineticEnergy = eZero,
        ExitVelocityVector = Vector3.Zero,
        ExitState = new ProjectileState
        {
            Position = entryPoint,
            Velocity = Vector3.Zero,
            Time = projectile.Time
        }
    };
}
```

**Overload 2: Explicit 3D Entry & Exit Points (Lines 77–103)**:
```csharp
float speed = projectile.Velocity.Length();
float effectiveThickness = Vector3.Distance(entryPoint, exitPoint);

if (speed < 1e-6f || effectiveThickness <= 0f)
{
    float eZero = 0.5f * profile.Mass * speed * speed;
    return new PenetrationResult
    {
        Outcome = PenetrationOutcome.Stopped,
        EntryPoint = entryPoint,
        ExitPoint = exitPoint,
        EffectiveThickness = effectiveThickness,
        AngleOfIncidence = 0f,
        InitialVelocity = speed,
        ExitVelocity = 0f,
        InitialKineticEnergy = eZero,
        RemainingKineticEnergy = 0f,
        TransferredKineticEnergy = eZero,
        ExitVelocityVector = Vector3.Zero,
        ExitState = new ProjectileState
        {
            Position = entryPoint,
            Velocity = Vector3.Zero,
            Time = projectile.Time
        }
    };
}
```

### 1.2 Test Suite State
- Current execution of `dotnet test`:
  `Passed! - Failed: 0, Passed: 143, Skipped: 0, Total: 143, Duration: 209 ms`
- In `TacticalSim.Tests/MaterialPenetrationTests.cs` (lines 763–772), `Penetration_SingularityAndNumericalStability_EdgeCases` tests zero and negative thicknesses (`0f`, `-0.01f`, `-100f`), but only asserts `!float.IsNaN(res.ExitVelocity)` and `res.EffectiveThickness >= 0f` without asserting `res.Outcome == PenetrationOutcome.Perforated` or `res.ExitVelocity == 800f`.

---

## 2. Logic Chain

1. **Energy and Work Mechanics**:
   - The work done on a projectile by medium drag over distance $T$ is $W = \int F_{drag} \, dx$.
   - When $T \le 0$, no medium is traversed, so $W = 0\text{ J}$.
   - Conservation of kinetic energy dictates $E_{remaining} = E_{k0} - W = E_{k0}$ and $E_{transferred} = W = 0\text{ J}$.
   - Consequently, exit speed must equal incident speed: $v_{exit} = v_0$, and trajectory vector must remain unperturbed: $\vec{v}_{exit} = \vec{v}_0$.
   - The outcome must be `PenetrationOutcome.Perforated`.

2. **Decoupling Stationary vs Zero-Thickness Guards**:
   - `speed < 1e-6f` represents a stationary or near-zero velocity projectile. Returning `PenetrationOutcome.Stopped` with $0$ exit velocity and $0$ remaining kinetic energy is correct for a stopped projectile.
   - `nominalThickness <= 0f` (or `effectiveThickness <= 0f`) with `speed >= 1e-6f` represents an active projectile passing through zero obstacle. Combining them with `||` erroneously stopped high-speed projectiles and absorbed 100% kinetic energy.
   - Decoupling these conditions into two ordered guard clauses cleanly handles:
     - Case A (`speed < 1e-6f`): Projectile is stationary $\rightarrow$ return `Stopped`.
     - Case B (`thickness <= 0f` and `speed >= 1e-6f`): Zero barrier resistance $\rightarrow$ return `Perforated` with zero energy transfer and unhindered velocity.

---

## 3. Caveats

- No caveats. The problem is localized entirely within `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs` and test verifications in `TacticalSim.Tests/MaterialPenetrationTests.cs`.

---

## 4. Conclusion & Actionable Fix Recommendation for Worker

### Recommended Changes in `TacticalSim.Core/Materials/MaterialPenetrationSystem.cs`

#### Change 1: In Overload 1 (lines 22–45)
Replace:
```csharp
            if (speed < 1e-6f || nominalThickness <= 0f)
            {
                float eZero = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = MathF.Max(0f, nominalThickness),
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }
```
With:
```csharp
            if (speed < 1e-6f)
            {
                float eZero = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = MathF.Max(0f, nominalThickness),
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }

            if (nominalThickness <= 0f)
            {
                float ek0 = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Perforated,
                    EntryPoint = entryPoint,
                    ExitPoint = entryPoint,
                    EffectiveThickness = 0f,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = speed,
                    InitialKineticEnergy = ek0,
                    RemainingKineticEnergy = ek0,
                    TransferredKineticEnergy = 0f,
                    ExitVelocityVector = projectile.Velocity,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = projectile.Velocity,
                        Time = projectile.Time
                    }
                };
            }
```

#### Change 2: In Overload 2 (lines 80–103)
Replace:
```csharp
            if (speed < 1e-6f || effectiveThickness <= 0f)
            {
                float eZero = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    EffectiveThickness = effectiveThickness,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }
```
With:
```csharp
            if (speed < 1e-6f)
            {
                float eZero = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Stopped,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    EffectiveThickness = effectiveThickness,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = 0f,
                    InitialKineticEnergy = eZero,
                    RemainingKineticEnergy = 0f,
                    TransferredKineticEnergy = eZero,
                    ExitVelocityVector = Vector3.Zero,
                    ExitState = new ProjectileState
                    {
                        Position = entryPoint,
                        Velocity = Vector3.Zero,
                        Time = projectile.Time
                    }
                };
            }

            if (effectiveThickness <= 0f)
            {
                float ek0 = 0.5f * profile.Mass * speed * speed;
                return new PenetrationResult
                {
                    Outcome = PenetrationOutcome.Perforated,
                    EntryPoint = entryPoint,
                    ExitPoint = exitPoint,
                    EffectiveThickness = 0f,
                    AngleOfIncidence = 0f,
                    InitialVelocity = speed,
                    ExitVelocity = speed,
                    InitialKineticEnergy = ek0,
                    RemainingKineticEnergy = ek0,
                    TransferredKineticEnergy = 0f,
                    ExitVelocityVector = projectile.Velocity,
                    ExitState = new ProjectileState
                    {
                        Position = exitPoint,
                        Velocity = projectile.Velocity,
                        Time = projectile.Time
                    }
                };
            }
```

### Recommended Test Additions in `TacticalSim.Tests/MaterialPenetrationTests.cs`

1. **Enhance existing edge case test in `Penetration_SingularityAndNumericalStability_EdgeCases`**:
```csharp
            // B. Zero and negative thicknesses
            float[] edgeThicknesses = { 0f, -0.01f, -100f, 1e-12f, 1e-6f };
            foreach (float t in edgeThicknesses)
            {
                var proj = new ProjectileState { Velocity = new Vector3(0, 0, 800f) };
                var res = system.CalculatePenetration(proj, profile, wood, t, new Vector3(0, 0, -1));
                Assert.False(float.IsNaN(res.ExitVelocity));
                Assert.False(float.IsNaN(res.EffectiveThickness));
                Assert.True(res.EffectiveThickness >= 0f);
                if (t <= 0f)
                {
                    Assert.Equal(PenetrationOutcome.Perforated, res.Outcome);
                    Assert.Equal(800f, res.ExitVelocity, 3);
                    Assert.Equal(0f, res.TransferredKineticEnergy, 3);
                }
            }
```

2. **Add a dedicated test `Penetration_ZeroOrNegativeThickness_PassesThroughUnimpeded`**:
```csharp
        [Fact]
        public void Penetration_ZeroOrNegativeThickness_PassesThroughUnimpeded()
        {
            var system = new MaterialPenetrationSystem();
            var wood = _registry.GetMaterial(MaterialType.Wood);
            var profile = new BallisticProfile
            {
                Mass = 0.004f,
                CrossSectionalArea = 0.000025f,
                DragModel = new StandardDragCurve(0.3f)
            };

            var proj = new ProjectileState
            {
                Position = new Vector3(10, 20, 30),
                Velocity = new Vector3(0, 0, 850.0f),
                Time = 1.0f
            };

            // 1. Slab overload with zero nominal thickness
            var resZero = system.CalculatePenetration(proj, profile, wood, 0f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resZero.Outcome);
            Assert.Equal(850.0f, resZero.ExitVelocity);
            Assert.Equal(resZero.InitialKineticEnergy, resZero.RemainingKineticEnergy);
            Assert.Equal(0f, resZero.TransferredKineticEnergy);
            Assert.Equal(proj.Velocity, resZero.ExitVelocityVector);
            Assert.Equal(proj.Position, resZero.ExitPoint);
            Assert.Equal(proj.Position, resZero.ExitState.Position);
            Assert.Equal(proj.Velocity, resZero.ExitState.Velocity);

            // 2. Slab overload with negative nominal thickness
            var resNeg = system.CalculatePenetration(proj, profile, wood, -0.05f, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resNeg.Outcome);
            Assert.Equal(850.0f, resNeg.ExitVelocity);
            Assert.Equal(resNeg.InitialKineticEnergy, resNeg.RemainingKineticEnergy);
            Assert.Equal(0f, resNeg.TransferredKineticEnergy);

            // 3. Explicit coordinates overload with coincident entry & exit points
            var resCoincident = system.CalculatePenetration(proj, profile, wood, proj.Position, proj.Position, new Vector3(0, 0, -1));
            Assert.Equal(PenetrationOutcome.Perforated, resCoincident.Outcome);
            Assert.Equal(850.0f, resCoincident.ExitVelocity);
            Assert.Equal(resCoincident.InitialKineticEnergy, resCoincident.RemainingKineticEnergy);
            Assert.Equal(0f, resCoincident.TransferredKineticEnergy);
            Assert.Equal(proj.Position, resCoincident.ExitPoint);
            Assert.Equal(proj.Position, resCoincident.ExitState.Position);
        }
```

---

## 5. Verification Method

To independently verify the recommended fix:
```pwsh
# 1. Build projects
dotnet build TacticalSim.Core/TacticalSim.Core.csproj
dotnet build TacticalSim.Tests/TacticalSim.Tests.csproj

# 2. Run MaterialPenetrationTests unit test suite
dotnet test --filter "FullyQualifiedName~MaterialPenetrationTests"

# 3. Run entire test suite
dotnet test
```

### Invalidation Conditions:
- If `CalculatePenetration` with `nominalThickness <= 0f` and `speed >= 1e-6f` returns `Outcome == PenetrationOutcome.Stopped` or `TransferredKineticEnergy > 0f`, the fix is invalid.
- If `CalculatePenetration` with `speed < 1e-6f` returns anything other than `Outcome == PenetrationOutcome.Stopped` with `ExitVelocity == 0f`, the fix is invalid.
