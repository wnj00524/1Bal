using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Core.Damage.Scenarios;

/// <summary>Versioned, serializable projectile input for a reference impact.</summary>
public sealed record ReferenceProjectileInput
{
    public const string CurrentSchemaVersion = "reference-projectile-input-v1";

    public ReferenceProjectileInput(
        string schemaVersion,
        string profileId,
        string displayName,
        string dragModelId,
        Mass mass,
        Area crossSectionalArea,
        float muzzleVelocityMetersPerSecond,
        float dragCoefficient)
    {
        if (!string.Equals(schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported projectile-input schema '{schemaVersion}'.", nameof(schemaVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dragModelId);
        if (mass.Kilograms <= 0f)
            throw new ArgumentOutOfRangeException(nameof(mass), "Projectile mass must be positive.");
        if (crossSectionalArea.SquareMeters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(crossSectionalArea), "Projectile area must be positive.");
        if (!float.IsFinite(muzzleVelocityMetersPerSecond) || muzzleVelocityMetersPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(muzzleVelocityMetersPerSecond));
        if (!float.IsFinite(dragCoefficient) || dragCoefficient < 0f)
            throw new ArgumentOutOfRangeException(nameof(dragCoefficient));

        SchemaVersion = schemaVersion;
        ProfileId = profileId;
        DisplayName = displayName;
        DragModelId = dragModelId;
        Mass = mass;
        CrossSectionalArea = crossSectionalArea;
        MuzzleVelocityMetersPerSecond = muzzleVelocityMetersPerSecond;
        DragCoefficient = dragCoefficient;
    }

    public string SchemaVersion { get; }
    public string ProfileId { get; }
    public string DisplayName { get; }
    public string DragModelId { get; }
    public Mass Mass { get; }
    public Area CrossSectionalArea { get; }
    public float MuzzleVelocityMetersPerSecond { get; }
    public float DragCoefficient { get; }
}

/// <summary>Versioned, serializable geometry and observation input for a reference scenario.</summary>
public sealed record ReferenceImpactScenarioInput
{
    public const string CurrentSchemaVersion = "reference-impact-scenario-input-v1";

    public ReferenceImpactScenarioInput(
        string schemaVersion,
        string scenarioId,
        string displayName,
        string description,
        string targetProfileId,
        ReferenceProjectileInput projectile,
        Vector3 entryPointBodyLocalMeters,
        Vector3 direction,
        Distance maximumTraversalDistance,
        SimulationTime observationDuration,
        SimulationTime physiologyStep)
    {
        if (!string.Equals(schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported scenario-input schema '{schemaVersion}'.", nameof(schemaVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);
        ArgumentNullException.ThrowIfNull(projectile);
        ValidateFinite(entryPointBodyLocalMeters, nameof(entryPointBodyLocalMeters));
        ValidateFinite(direction, nameof(direction));
        if (direction.LengthSquared() <= 0f)
            throw new ArgumentOutOfRangeException(nameof(direction), "Projectile direction must be non-zero.");
        if (maximumTraversalDistance.Meters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumTraversalDistance));
        if (observationDuration.Seconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(observationDuration));
        if (physiologyStep.Seconds <= 0f || physiologyStep.Seconds > observationDuration.Seconds)
            throw new ArgumentOutOfRangeException(nameof(physiologyStep));

        SchemaVersion = schemaVersion;
        ScenarioId = scenarioId;
        DisplayName = displayName;
        Description = description;
        TargetProfileId = targetProfileId;
        Projectile = projectile;
        EntryPointBodyLocalMeters = entryPointBodyLocalMeters;
        Direction = Vector3.Normalize(direction);
        MaximumTraversalDistance = maximumTraversalDistance;
        ObservationDuration = observationDuration;
        PhysiologyStep = physiologyStep;
    }

    public string SchemaVersion { get; }
    public string ScenarioId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string TargetProfileId { get; }
    public ReferenceProjectileInput Projectile { get; }
    public Vector3 EntryPointBodyLocalMeters { get; }
    public Vector3 Direction { get; }
    public Distance MaximumTraversalDistance { get; }
    public SimulationTime ObservationDuration { get; }
    public SimulationTime PhysiologyStep { get; }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }
}

/// <summary>Pairs serializable inputs with a fresh-target factory kept out of JSON output.</summary>
public sealed class ReferenceImpactScenario
{
    private readonly Func<IActorPhysiology> _targetFactory;

    public ReferenceImpactScenario(
        ReferenceImpactScenarioInput input,
        Func<IActorPhysiology> targetFactory)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
    }

    public ReferenceImpactScenarioInput Input { get; }

    public IActorPhysiology CreateFreshTarget() =>
        _targetFactory() ?? throw new InvalidOperationException(
            $"Scenario '{Input.ScenarioId}' returned a null target physiology.");
}

public interface IReferenceImpactScenarioCatalog
{
    IReadOnlyList<ReferenceImpactScenarioInput> List();
    ReferenceImpactScenario GetRequired(string scenarioId);
}

/// <summary>Built-in, Godot-free M5 reference impacts that use fresh anatomical targets.</summary>
public sealed class ReferenceImpactScenarioCatalog : IReferenceImpactScenarioCatalog
{
    private readonly ReadOnlyCollection<ReferenceImpactScenario> _scenarios;

    public ReferenceImpactScenarioCatalog()
        : this(CreateBuiltInScenarios())
    {
    }

    public ReferenceImpactScenarioCatalog(IEnumerable<ReferenceImpactScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        ReferenceImpactScenario[] copy = scenarios.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("At least one reference impact scenario is required.", nameof(scenarios));
        if (copy.Select(scenario => scenario.Input.ScenarioId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException("Reference impact scenario identifiers must be unique.", nameof(scenarios));

        _scenarios = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<ReferenceImpactScenarioInput> List() =>
        Array.AsReadOnly(_scenarios.Select(scenario => scenario.Input).ToArray());

    public ReferenceImpactScenario GetRequired(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        return _scenarios.FirstOrDefault(
                scenario => string.Equals(scenario.Input.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown reference impact scenario '{scenarioId}'.");
    }

    private static IEnumerable<ReferenceImpactScenario> CreateBuiltInScenarios()
    {
        yield return Create(
            scenarioId: "rifle-arm",
            displayName: "5.56 mm arm impact",
            description: "A body-local lateral arm path used for deterministic M5 pipeline checks.",
            profileId: "5.56x45mm-reference-v1",
            projectileName: "5.56x45mm reference projectile",
            mass: Mass.FromKilograms(0.004f),
            area: Area.FromSquareMeters(0.000024f),
            muzzleVelocityMetersPerSecond: 900f,
            dragCoefficient: 0.3f,
            entryPoint: new Vector3(0.3f, 0.25f, -1f));

        yield return Create(
            scenarioId: "rifle-leg",
            displayName: ".308 leg impact",
            description: "A body-local leg path used for deterministic M5 pipeline checks.",
            profileId: "308-winchester-reference-v1",
            projectileName: ".308 Winchester reference projectile",
            mass: Mass.FromKilograms(0.0097f),
            area: Area.FromSquareMeters(0.000048f),
            muzzleVelocityMetersPerSecond: 800f,
            dragCoefficient: 0.4f,
            entryPoint: new Vector3(0.1f, -0.4f, -1f));
    }

    private static ReferenceImpactScenario Create(
        string scenarioId,
        string displayName,
        string description,
        string profileId,
        string projectileName,
        Mass mass,
        Area area,
        float muzzleVelocityMetersPerSecond,
        float dragCoefficient,
        Vector3 entryPoint)
    {
        var projectile = new ReferenceProjectileInput(
            ReferenceProjectileInput.CurrentSchemaVersion,
            profileId,
            projectileName,
            "standard-drag-curve-v1",
            mass,
            area,
            muzzleVelocityMetersPerSecond,
            dragCoefficient);
        var input = new ReferenceImpactScenarioInput(
            ReferenceImpactScenarioInput.CurrentSchemaVersion,
            scenarioId,
            displayName,
            description,
            "anatomical-dummy-v1",
            projectile,
            entryPoint,
            Vector3.UnitZ,
            Distance.FromMeters(2f),
            SimulationTime.FromSeconds(1f),
            SimulationTime.FromSeconds(0.1f));
        return new ReferenceImpactScenario(input, AnatomicalDummyBuilder.BuildDummy);
    }
}
