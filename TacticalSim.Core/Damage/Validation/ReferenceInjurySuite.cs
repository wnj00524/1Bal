namespace TacticalSim.Core.Damage.Validation;

public enum ReferenceInjuryKind
{
    SoftTissueLimb, MajorArterial, MajorVenous, JunctionalBleeding, ConcealedAbdominal,
    StableFracture, UnstableFracture, SpinalInjury, SimplePneumothorax, TensionPneumothorax,
    Hemothorax, CardiacInjury, MultipleHitTrauma, EffectiveTreatment, PartialTreatment,
    FailedTreatment, InterruptedTreatment
}

public sealed record OutcomeBand(string Metric, double Minimum, double Maximum, string Unit)
{
    public bool Contains(double value) => double.IsFinite(value) && value >= Minimum && value <= Maximum;
}

public sealed record ReferenceInjuryCase(string Id, ReferenceInjuryKind Kind, string Description,
    IReadOnlyList<OutcomeBand> ExpectedBands, IReadOnlyList<string> QualitativeExpectations);
public sealed record ReferenceObservation(string CaseId, IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyCollection<string> Observations);
public sealed record ReferenceCaseResult(string CaseId, bool Accepted, IReadOnlyList<string> Deviations);

/// <summary>Separates broad validation expectations from exact software-invariant unit tests.</summary>
public sealed class ReferenceInjurySuite
{
    public const string Version = "reference-injury-suite-v1";
    public ReferenceInjurySuite(IEnumerable<ReferenceInjuryCase> cases)
    {
        Cases = cases?.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray() ?? throw new ArgumentNullException(nameof(cases));
        if (Cases.Count == 0 || Cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
            throw new ArgumentException("Reference case IDs must be non-empty and unique.", nameof(cases));
    }
    public IReadOnlyList<ReferenceInjuryCase> Cases { get; }

    public static ReferenceInjurySuite CreateBaseline() => new(Enum.GetValues<ReferenceInjuryKind>().Select(kind =>
        new ReferenceInjuryCase(ToId(kind), kind, Describe(kind),
            [new OutcomeBand("simulation-seconds", 0, 3600, "s")], Expectations(kind))));

    public ReferenceCaseResult Evaluate(ReferenceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var reference = Cases.SingleOrDefault(x => x.Id == observation.CaseId)
            ?? throw new KeyNotFoundException($"Reference case '{observation.CaseId}' is not registered.");
        var deviations = new List<string>();
        foreach (var band in reference.ExpectedBands)
        {
            if (!observation.Metrics.TryGetValue(band.Metric, out double value)) deviations.Add($"Missing metric '{band.Metric}'.");
            else if (!band.Contains(value)) deviations.Add($"{band.Metric}={value} {band.Unit} is outside [{band.Minimum}, {band.Maximum}].");
        }
        foreach (string expected in reference.QualitativeExpectations)
            if (!observation.Observations.Contains(expected, StringComparer.Ordinal)) deviations.Add($"Missing expectation '{expected}'.");
        return new(reference.Id, deviations.Count == 0, deviations);
    }

    private static string ToId(ReferenceInjuryKind kind) => string.Concat(kind.ToString().SelectMany((c, i) =>
        char.IsUpper(c) && i > 0 ? new[] { '-', char.ToLowerInvariant(c) } : new[] { char.ToLowerInvariant(c) }));
    private static string Describe(ReferenceInjuryKind kind) => $"Broad validation case for {ToId(kind).Replace('-', ' ')}; not a clinical prediction.";
    private static string[] Expectations(ReferenceInjuryKind kind) => kind switch
    {
        ReferenceInjuryKind.MajorArterial => ["bleeding-present", "faster-than-venous"],
        ReferenceInjuryKind.MajorVenous => ["bleeding-present", "slower-than-arterial"],
        ReferenceInjuryKind.ConcealedAbdominal => ["bleeding-present", "concealed"],
        ReferenceInjuryKind.UnstableFracture => ["movement-more-limited-than-stable"],
        ReferenceInjuryKind.TensionPneumothorax => ["worse-than-simple-pneumothorax"],
        ReferenceInjuryKind.EffectiveTreatment => ["improves-target-mechanism"],
        ReferenceInjuryKind.PartialTreatment => ["partial-effect"],
        ReferenceInjuryKind.FailedTreatment => ["no-target-effect"],
        ReferenceInjuryKind.InterruptedTreatment => ["interruption-recorded"],
        _ => ["mechanism-present"]
    };
}
