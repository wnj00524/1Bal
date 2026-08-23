# TacticalSim

A highly decoupled, strictly deterministic tactical simulation engine written in C# (.NET 10.0).

## Overview

TacticalSim is designed with a core focus on separation between simulation math and presentation. The authoritative simulation pipeline resides entirely in `TacticalSim.Core`.

### Key Features
- **Strictly Deterministic:** Given the same initial state and input sequence, the outcome is guaranteed to be identical.
- **Fractionated Turn Resolver:** A simultaneous timeline manager (`ITurnResolver`) that interleaves actions across multiple entities.
- **Physiological Integration:** A robust damage and trauma model that tracks hemorrhage, ischemia, and tactical capability without rendering dependencies.
- **Bounded Spatial World:** Entities and static cover geometries are bounded in `ITacticalWorld`.
- **Reference Impact Telemetry:** Deterministic terminal ballistics modeling including penetration, energy ledgering, and lesion formation.

## Projects

- **`TacticalSim.Core`**: The authoritative deterministic engine. All mathematical, spatial, temporal, and physiological logic resides here.
- **`TacticalSim.Tests`**: Comprehensive xUnit tests validating turn resolution, sub-tick progression, failure isolation, bounds, and medical fidelity.
- **`TacticalSim.ConsoleApp`**: Command-line application to run and evaluate deterministic scenario impacts and telemetry.
- **`TacticalSim.GodotClient`**: Reference client demonstrating presentation integration while fully trusting the authoritative `.Core`.

## Building and Testing

Ensure you have the .NET 10.0 SDK installed.

```bash
# Build the entire solution
dotnet build

# Run the comprehensive test suite
dotnet test
```

## Architectural Guidelines

The project operates under strict architectural constraints. See [`agents.md`](agents.md) and the `docs/` folder for exhaustive details, including the damage model roadmap (`docs/TacticalSim_Damage_Model_Roadmap.md`).

Key constraints:
- `System.Numerics.Vector3` is used for 3D coordinate math.
- `Microsoft.Extensions.DependencyInjection` forms the backbone of service composition.
- The UI layer MUST NOT maintain competing physiological state or simulation timelines.
- Any feature flagged changes must ensure backwards deterministic compatibility.
