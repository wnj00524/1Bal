using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ProxyState.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace ProxyState.Tests;

public sealed class Milestone20AcceptanceTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void CompleteLodPipelineBenchmark(int population)
    {
        if (population > 1_000 && Environment.GetEnvironmentVariable("PROXYSTATE_RUN_LARGE_BENCHMARKS") != "1")
        {
            output.WriteLine($"population={population}; notRun=true; set PROXYSTATE_RUN_LARGE_BENCHMARKS=1");
            return;
        }

        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var store = new EntityStore();
        var spawner = new AgentSpawner(catalog);
        var generation = Measure(() => spawner.Spawn(store, population, 20_003));
        var lod = spawner.LodService!;
        var tier1 = store.Query<Identity>().AllTags(Tags.Get<Tier1LodTag>()).Count;
        var tier2 = store.Query<Identity>().AllTags(Tags.Get<Tier2LodTag>()).Count;
        var tier3 = store.Query<Identity>().AllTags(Tags.Get<Tier3LodTag>()).Count;
        output.WriteLine($"phase=generation; population={population}; elapsedMs={generation.ElapsedMs:F3}; allocatedBytes={generation.AllocatedBytes}; workingSetBytes={generation.WorkingSetBytes}; edges={store.Query<EdgeData>().Count}; tier1={tier1}; tier2={tier2}; tier3={tier3}");

        Assert.Equal(population, tier1 + tier2 + tier3);
        Assert.Equal(tier1 + tier2, store.Query<Identity>().AllTags(Tags.Get<DetailedSimulationTag>()).Count);
        Assert.Equal(0, store.Query<DecisionState>().AllTags(Tags.Get<Tier3LodTag>()).Count);
        Assert.Equal(0, store.Query<AgentTravel>().AllTags(Tags.Get<Tier3LodTag>()).Count);

        PlayerIntelligenceDB? intelligence = null;
        var projection = Measure(() => intelligence = PlayerIntelligenceDB.Create(store, catalog));
        DebugInspectionProjection? debug = null;
        var debugInitialization = Measure(() => debug = DebugInspectionProjection.Create(store, catalog));
        var visibleVisits = VisibleRowRange.Visit(intelligence!.Agents.Count, population / 2, population / 2 + 20, _ => { });
        output.WriteLine($"phase=projection-ui; population={population}; elapsedMs={projection.ElapsedMs:F3}; allocatedBytes={projection.AllocatedBytes}; agentVisits={intelligence.Diagnostics.InitializationAgentVisits}; edgeVisits={intelligence.Diagnostics.InitializationEdgeVisits}; debugElapsedMs={debugInitialization.ElapsedMs:F3}; debugAllocatedBytes={debugInitialization.AllocatedBytes}; visibleRowVisits={visibleVisits}");
        Assert.Equal(population, intelligence.Diagnostics.InitializationAgentVisits);
        Assert.Equal(20, visibleVisits);
        Assert.NotNull(debug);

        var clock = store.CreateEntity(new WorldTime());
        var work = new SimulationWorkDiagnostics();
        var decisions = new SystemRoot(store)
        {
            new AgentDecisionSystem(store, catalog, clock, workDiagnostics: work,
                socialIndexes: spawner.Indexes, lodService: lod)
        };
        var week = Measure(() =>
        {
            for (var hour = 1; hour <= 7 * 24; hour++)
            {
                ref var time = ref clock.GetComponent<WorldTime>();
                time.ElapsedSimulationSeconds = hour * 60 * SimulationDefaults.SimulationSecondsPerMinute;
                time.DeltaSimulationSeconds = 60 * SimulationDefaults.SimulationSecondsPerMinute;
                lod.UpdateCoarse(hour * 60L);
                decisions.Update(default);
            }
        });
        var snapshot = work.Snapshot();
        output.WriteLine($"phase=simulated-week; population={population}; elapsedMs={week.ElapsedMs:F3}; allocatedBytes={week.AllocatedBytes}; workingSetBytes={week.WorkingSetBytes}; decisionPasses={snapshot.DecisionPasses}; candidateEvaluations={snapshot.CandidateEvaluations}; targetVisits={snapshot.TargetPopulationVisits}; edgeVisits={snapshot.EdgeVisits}; coarseVisits={lod.CoarseAgentVisits}");
        Assert.Equal(0, snapshot.TargetPopulationVisits);
        Assert.Equal(0, snapshot.EdgeVisits);
        Assert.True(snapshot.DecisionPasses <= (long)(tier1 + tier2) * 169);
        Assert.True(lod.CoarseAgentVisits > 0);

        var promoted = store.Query<Identity>().AllTags(Tags.Get<Tier3LodTag>()).Entities.First();
        var originalDetailed = store.Query<Identity>().AllTags(Tags.Get<DetailedSimulationTag>()).Count;
        lod.SetInvestigation(promoted.Id, true);
        Assert.True(promoted.Tags.Has<Tier1LodTag>() && promoted.HasComponent<DecisionState>());
        lod.SetInvestigation(promoted.Id, false);
        var boundary = AgentLodService.NextDayBoundaryMinute(7 * SimulationDefaults.SimulationMinutesPerDay);
        ref var finalTime = ref clock.GetComponent<WorldTime>();
        finalTime.ElapsedSimulationSeconds = boundary * SimulationDefaults.SimulationSecondsPerMinute;
        lod.UpdateCoarse(boundary);
        Assert.True(promoted.Tags.Has<Tier3LodTag>() && !promoted.HasComponent<DecisionState>());
        Assert.Equal(originalDetailed, store.Query<Identity>().AllTags(Tags.Get<DetailedSimulationTag>()).Count);
    }

    private static Measurement Measure(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        using var process = Process.GetCurrentProcess();
        return new Measurement(stopwatch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before, process.WorkingSet64);
    }

    private readonly record struct Measurement(double ElapsedMs, long AllocatedBytes, long WorkingSetBytes);
}
