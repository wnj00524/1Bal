namespace TacticalSim.Core.Damage.Validation;

public enum ParameterClassification
{
    ExternallySourced,
    EmpiricallyCalibrated,
    Inferred,
    Provisional,
    GameplayTuning
}

public sealed record ParameterProvenance(
    string Id,
    string Component,
    string Parameter,
    string ValueAndUnit,
    ParameterClassification Classification,
    string Source,
    string Version,
    string Owner,
    IReadOnlyList<string> AffectedTests);

/// <summary>Versioned, machine-readable inventory of constants that affect damage outcomes.</summary>
public sealed class ParameterProvenanceRegistry
{
    public const string SchemaVersion = "parameter-provenance-v1";
    private readonly Dictionary<string, ParameterProvenance> _entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ParameterProvenance> Entries => _entries.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

    public void Register(ParameterProvenance entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Owner);
        if (!_entries.TryAdd(entry.Id, entry)) throw new InvalidOperationException($"Parameter '{entry.Id}' is already registered.");
    }

    public ParameterProvenance GetRequired(string id) => _entries.TryGetValue(id, out var entry)
        ? entry : throw new KeyNotFoundException($"Parameter '{id}' is not registered.");

    public void ValidateCoverage(IEnumerable<string> parameterIds)
    {
        string[] missing = parameterIds.Distinct(StringComparer.Ordinal).Where(x => !_entries.ContainsKey(x)).Order().ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Missing parameter provenance: {string.Join(", ", missing)}.");
    }
}

/// <summary>Production provenance entries introduced by the integrated neurological model.</summary>
public static class IntegratedNeurologicalParameterProvenance
{
    public static readonly IReadOnlyList<string> RequiredParameterIds =
    [
        "neurology.brain.incapacitation-severity",
        "neurology.brain.unconscious-severity",
        "neurology.brain.fatal-severity",
        "neurology.brain.cognitive-loss-multiplier",
        "neurology.brain.brainstem-loss-multiplier"
    ];

    public static ParameterProvenanceRegistry CreateRegistry()
    {
        var registry = new ParameterProvenanceRegistry();
        const string source = "DM-802 provisional gameplay calibration; no external clinical cut-off asserted";
        const string version = "integrated-neurology-v1";
        const string owner = "damage-model";
        string[] tests = ["IntegratedNeurologicalGodotPathTests"];
        registry.Register(new(RequiredParameterIds[0], "neurology", "incapacitation severity", "0.15 ratio",
            ParameterClassification.Provisional, source, version, owner, tests));
        registry.Register(new(RequiredParameterIds[1], "neurology", "unconscious severity", "0.30 ratio",
            ParameterClassification.Provisional, source, version, owner, tests));
        registry.Register(new(RequiredParameterIds[2], "neurology", "fatal severity", "0.85 ratio",
            ParameterClassification.Provisional, source, version, owner, tests));
        registry.Register(new(RequiredParameterIds[3], "neurology", "cognitive loss multiplier", "2.5 ratio",
            ParameterClassification.GameplayTuning, source, version, owner, tests));
        registry.Register(new(RequiredParameterIds[4], "neurology", "brainstem loss multiplier", "0.5 ratio",
            ParameterClassification.GameplayTuning, source, version, owner, tests));
        return registry;
    }
}
