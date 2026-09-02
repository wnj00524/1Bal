using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentSocialIndexesTests
{
    [Fact]
    public void EmptyAndSingleAgentPopulationsHaveSafeEmptyRanges()
    {
        var emptyStore = new EntityStore();
        var empty = new AgentSocialIndexes();
        empty.Rebuild(emptyStore);

        Assert.Equal(0, empty.AgentCount);
        Assert.Equal(0, empty.DirectedEdgeCount);
        Assert.False(empty.TryGetAgent(123, out _));
        Assert.Empty(empty.GetOutgoingEdges(123).ToArray());

        var singleStore = new EntityStore();
        var agent = singleStore.CreateEntity(new Identity());
        var single = new AgentSocialIndexes();
        single.Rebuild(singleStore);

        Assert.Equal(1, single.AgentCount);
        Assert.True(single.TryGetAgent(agent.Id, out var found));
        Assert.Equal(agent.Id, found.Id);
        Assert.Equal(0, single.GetOutgoingRelationshipCount(agent.Id));
        Assert.False(single.TryGetDirectedEdge(agent.Id, 999, out _));
    }

    [Fact]
    public void SeededPopulationProducesDeterministicPackedLayout()
    {
        var catalog = LoadCatalog();
        var firstSpawner = new AgentSpawner(catalog);
        var secondSpawner = new AgentSpawner(catalog);
        var firstStore = new EntityStore();
        var secondStore = new EntityStore();

        firstSpawner.Spawn(firstStore, 64, 17_202);
        secondSpawner.Spawn(secondStore, 64, 17_202);

        Assert.Equal(Capture(firstStore, firstSpawner.Indexes), Capture(secondStore, secondSpawner.Indexes));
    }

    [Fact]
    public void EveryEdgeEntityAppearsOnceInItsSortedSourceRange()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        var spawner = new AgentSpawner(catalog);
        spawner.Spawn(store, 80, 17_203);

        var visited = new HashSet<int>();
        foreach (var agent in store.Query<Identity>().Entities)
        {
            var outgoing = spawner.Indexes.GetOutgoingEdges(agent.Id);
            var previousTarget = int.MinValue;
            foreach (var entry in outgoing)
            {
                Assert.True(entry.TargetAgentId >= previousTarget);
                Assert.True(visited.Add(entry.EdgeEntityId));
                Assert.True(spawner.Indexes.TryGetDirectedEdge(agent.Id, entry.TargetAgentId, out var found));
                Assert.Equal(entry, found);
                previousTarget = entry.TargetAgentId;
            }
        }

        Assert.Equal(store.Query<EdgeData>().Count, visited.Count);
        Assert.Equal(spawner.Indexes.DirectedEdgeCount, visited.Count);
    }

    [Fact]
    public void LookupMissesDoNotAllocateAndMutationNotificationsRequireRebuild()
    {
        var store = new EntityStore();
        var source = store.CreateEntity(new Identity());
        var target = store.CreateEntity(new Identity());
        store.CreateEntity(new EdgeData { Source = source, Target = target });
        var indexes = new AgentSocialIndexes();
        indexes.Rebuild(store);

        // Warm up JIT paths before measuring steady-state misses.
        Assert.False(indexes.TryGetAgent(int.MaxValue, out _));
        Assert.False(indexes.TryGetDirectedEdge(source.Id, int.MaxValue, out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            indexes.TryGetAgent(int.MaxValue, out _);
            indexes.TryGetDirectedEdge(source.Id, int.MaxValue, out _);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());

        indexes.NotifySocialGraphChanged();
        Assert.Throws<InvalidOperationException>(() => indexes.GetOutgoingRelationshipCount(source.Id));
        indexes.Rebuild(store);
        Assert.Equal(1, indexes.GetOutgoingRelationshipCount(source.Id));

        indexes.NotifyPopulationChanged();
        Assert.Throws<InvalidOperationException>(() => indexes.TryGetAgent(source.Id, out _));
    }

    private static string[] Capture(EntityStore store, AgentSocialIndexes indexes)
        => store.Query<Identity>().Entities.OrderBy(agent => agent.Id)
            .Select(agent => $"{agent.Id}:{string.Join(',', indexes.GetOutgoingEdges(agent.Id).ToArray()
                .Select(edge => $"{edge.TargetAgentId}/{edge.EdgeEntityId}"))}")
            .ToArray();

    private static ContentCatalog LoadCatalog()
        => ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
}
