using System.Text.Json;

namespace TacticalSim.Core.Damage.Validation;

public sealed record CalibrationParameter(string Id, double Baseline, double Minimum, double Maximum, double Step);
public sealed record SensitivityResult(string ParameterId, double LowOutcome, double BaselineOutcome, double HighOutcome, double NormalizedSensitivity);
public sealed record CalibrationCandidateResult(string CandidateId, double MeanError, IReadOnlyDictionary<string, double> ScenarioErrors);
public sealed record CalibrationReport(string SchemaVersion, string ModelVersion, IReadOnlyList<SensitivityResult> Sensitivities,
    IReadOnlyList<CalibrationCandidateResult> Candidates);

public static class CalibrationRunner
{
    public const string SchemaVersion = "calibration-report-v1";

    public static SensitivityResult Analyze(CalibrationParameter parameter, Func<double, double> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (parameter.Minimum > parameter.Baseline || parameter.Maximum < parameter.Baseline || parameter.Step <= 0)
            throw new ArgumentOutOfRangeException(nameof(parameter));
        double lowValue = Math.Max(parameter.Minimum, parameter.Baseline - parameter.Step);
        double highValue = Math.Min(parameter.Maximum, parameter.Baseline + parameter.Step);
        double low = evaluate(lowValue), baseline = evaluate(parameter.Baseline), high = evaluate(highValue);
        double span = highValue - lowValue;
        double sensitivity = span == 0 || baseline == 0 ? 0 : ((high - low) / span) * (parameter.Baseline / Math.Abs(baseline));
        return new(parameter.Id, low, baseline, high, sensitivity);
    }

    public static CalibrationCandidateResult Compare(string id, IReadOnlyDictionary<string, double> expected,
        IReadOnlyDictionary<string, double> actual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (expected.Count < 2) throw new ArgumentException("Calibration requires at least two scenarios to discourage single-case overfitting.", nameof(expected));
        var errors = expected.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key,
            x => Math.Abs(actual.TryGetValue(x.Key, out double value) ? value - x.Value : double.PositiveInfinity), StringComparer.Ordinal);
        return new(id, errors.Values.Average(), errors);
    }

    public static string Export(CalibrationReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
}
