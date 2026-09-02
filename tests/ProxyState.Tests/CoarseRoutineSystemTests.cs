using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class CoarseRoutineSystemTests
{
    [Fact]
    public void CatchUpAppliesProfileEffectsAndUpdatesTheCoarseWatermark()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 1, 9123);
        var agent = store.Query<Identity>().Entities.Single();
        var system = new CoarseRoutineSystem(catalog);
        var fatigue = catalog.AgentAttributes.GetIndex("fatigue");
        var before = agent.GetComponent<AgentAttributes>().Values[fatigue];

        system.Add(agent, 0);
        system.CatchUp(agent, 60);

        ref var state = ref agent.GetComponent<AgentLodState>();
        Assert.Equal(60, state.LastCoarseSimulatedMinute);
        Assert.NotEqual(0, state.CoarseProfileId);
        Assert.NotEqual(before, agent.GetComponent<AgentAttributes>().Values[fatigue]);
    }

    [Fact]
    public void ShardsAreStableAndOnlyTheScheduledShardIsVisited()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 3, 8123);
        var system = new CoarseRoutineSystem(catalog);
        foreach (var agent in store.Query<Identity>().Entities) system.Add(agent, 0);

        system.UpdateHour(60);

        var advanced = store.Query<AgentLodState>().Entities.Count(entity => entity.GetComponent<AgentLodState>().LastCoarseSimulatedMinute == 60);
        Assert.InRange(advanced, 0, 1);
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
}
