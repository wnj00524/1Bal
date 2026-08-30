using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ProxyState.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace ProxyState.Tests;

public sealed class DecisionPerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public void OneThousandAgentsOverSixtyDecisionMinutes()
    {
        const int population = 1_000;
        const int measuredMinutes = 60;
        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, population, new Random(6_001));
        var clock = store.CreateEntity(new WorldTime
        {
            ElapsedSimulationSeconds = 600 * SimulationDefaults.SimulationSecondsPerMinute
        });
        var root = new SystemRoot(store) { new AgentDecisionSystem(store, catalog, clock) };

        // Warm the query/JIT paths before measuring. Each measured update moves
        // to a new minute, which exercises a full deliberation for every agent.
        root.Update(default);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 1; index <= measuredMinutes; index++)
        {
            ref var time = ref clock.GetComponent<WorldTime>();
            time.ElapsedSimulationSeconds = (600 + index) * SimulationDefaults.SimulationSecondsPerMinute;
            time.DeltaSimulationSeconds = SimulationDefaults.SimulationSecondsPerMinute;
            root.Update(default);
        }
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine($"population={population}; minutes={measuredMinutes}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}; allocatedBytes={allocatedBytes}; nsPerAgentDecision={stopwatch.Elapsed.TotalNanoseconds / (population * measuredMinutes):F1}; bytesPerAgentDecision={(double)allocatedBytes / (population * measuredMinutes):F2}");
        Assert.Equal(population, store.Query<IntentionState>().Count);
    }
}
