using System.Diagnostics;
using System.Text.Json;

namespace TacticalSim.Core.Damage.Validation;

public sealed record BenchmarkDefinition(string Name, int Iterations, TimeSpan Budget, Action Operation);
public sealed record BenchmarkMeasurement(string Name, int Iterations, double ElapsedMilliseconds,
    double NanosecondsPerOperation, double BudgetMilliseconds, bool WithinBudget);
public sealed record BenchmarkReport(string SchemaVersion, string Runtime, IReadOnlyList<BenchmarkMeasurement> Measurements);

/// <summary>Dependency-free, CI-friendly performance smoke harness; use repeated external runs for optimization claims.</summary>
public static class DamageBenchmarkRunner
{
    public const string SchemaVersion = "damage-benchmark-v1";
    public static BenchmarkReport Run(IEnumerable<BenchmarkDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var results = new List<BenchmarkMeasurement>();
        foreach (var definition in definitions)
        {
            if (definition.Iterations <= 0 || definition.Budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(definitions));
            definition.Operation(); // warm-up
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < definition.Iterations; i++) definition.Operation();
            timer.Stop();
            results.Add(new(definition.Name, definition.Iterations, timer.Elapsed.TotalMilliseconds,
                timer.Elapsed.TotalNanoseconds / definition.Iterations, definition.Budget.TotalMilliseconds,
                timer.Elapsed <= definition.Budget));
        }
        return new(SchemaVersion, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, results);
    }
    public static string Export(BenchmarkReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
}
