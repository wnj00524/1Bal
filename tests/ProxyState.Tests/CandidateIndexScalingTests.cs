using System.Diagnostics;
using ProxyState.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace ProxyState.Tests;

public sealed class CandidateIndexScalingTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(3)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(256)]
    public void DenseCandidateEnumerationScalesWithSetBits(int intentCount)
    {
        const int population = 1_000;
        const int repetitions = 100;
        // Model a static context index that rejects three quarters of a growing
        // catalogue. Runtime indexes are deliberately spread across all words.
        var candidates = IntentBitSet.FromIndexes(intentCount,
            Enumerable.Range(0, intentCount).Where(index => index % 4 == 0));
        var expectedVisits = (long)candidates.Count * population * repetitions;

        var checksum = 0L;
        var stopwatch = Stopwatch.StartNew();
        for (var repetition = 0; repetition < repetitions; repetition++)
            for (var agent = 0; agent < population; agent++)
                foreach (var runtimeIndex in candidates.EnumerateSetBits()) checksum += runtimeIndex + 1;
        stopwatch.Stop();

        output.WriteLine($"intents={intentCount}; population={population}; repetitions={repetitions}; candidates={candidates.Count}; visits={expectedVisits}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}; checksum={checksum}");
        Assert.Equal((long)candidates.EnumerateSetBits().Sum(index => index + 1) * population * repetitions, checksum);
        Assert.True(candidates.Count <= (intentCount + 3) / 4);
    }
}
