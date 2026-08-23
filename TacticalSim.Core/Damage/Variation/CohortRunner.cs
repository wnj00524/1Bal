using System.Diagnostics;

namespace TacticalSim.Core.Damage.Variation;

public sealed record CohortCase(int Index, ulong Seed);
public sealed record CohortFailure(int Index, ulong Seed, string Error);
public sealed record CohortRunResult<T>(string ModelVersion, TimeSpan Elapsed, IReadOnlyList<T> Outcomes,
    IReadOnlyList<CohortFailure> Failures);

/// <summary>Headless, order-stable batch execution and diagnostics for seeded cohorts.</summary>
public static class CohortRunner
{
    public static CohortRunResult<T> Run<T>(int count, ulong rootSeed, string modelVersion, Func<CohortCase, T> scenario)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        ArgumentNullException.ThrowIfNull(scenario);
        var outcomes = new List<T>(count);
        var failures = new List<CohortFailure>();
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            ulong seed = Mix(rootSeed + unchecked((ulong)i * 0x9E3779B97F4A7C15UL));
            try { outcomes.Add(scenario(new CohortCase(i, seed))); }
            catch (Exception ex) { failures.Add(new CohortFailure(i, seed, ex.Message)); }
        }
        stopwatch.Stop();
        return new(modelVersion, stopwatch.Elapsed, outcomes.AsReadOnly(), failures.AsReadOnly());
    }

    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
