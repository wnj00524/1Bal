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
