using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class CoarseRoutineProfileTests
{
    [Fact]
    public void IdenticalMaterialInputsShareASevenDayProfileWithExactCoverage()
    {
        var catalog = LoadCatalog();
        var cache = new CoarseRoutineProfileCache(catalog);
        var job = catalog.Jobs.Single(job => job.Id == "office-worker");

        var first = cache.GetOrCreate(job.Hash, 0, 20);
        var second = cache.GetOrCreate(job.Hash, 0, 20);

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
        Assert.Equal(0, first.Intervals[0].StartMinute);
        Assert.Equal(SimulationDefaults.SimulationMinutesPerWeek, first.Intervals[^1].EndMinute);
        Assert.All(first.Intervals.Zip(first.Intervals.Skip(1)), pair => Assert.Equal(pair.First.EndMinute, pair.Second.StartMinute));
    }

    [Fact]
    public void MaterialDifferencesAndWeekWrapChooseTheCorrectSharedProfileSegment()
    {
        var catalog = LoadCatalog();
        var cache = new CoarseRoutineProfileCache(catalog);
        var job = catalog.Jobs.Single(job => job.Id == "office-worker");
        var baseProfile = cache.GetOrCreate(job.Hash, 0, 20);
        var changedCommute = cache.GetOrCreate(job.Hash, 0, 25);

        Assert.NotEqual(baseProfile.Id, changedCommute.Id);
        Assert.Equal(baseProfile.GetSegment(0), baseProfile.GetSegment(SimulationDefaults.SimulationMinutesPerWeek));
        Assert.Equal(catalog.Intents.All.Single(intent => intent.Id == "rest").Hash, baseProfile.GetSegment(460).IntentHash);
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
}
