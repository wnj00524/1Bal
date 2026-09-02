using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentLodClassificationTests
{
    [Fact]
    public void InitialClassificationStopsAtDirectPoiNeighbours()
    {
        var (store, agents, indexes) = CreatePopulation(7);
        agents[0].AddTag<OperativeTag>();
        AddSocialPair(store, agents[0], agents[1]);
        AddSocialPair(store, agents[1], agents[2]);

        // Agents 3 and 4 share an ordinary flat network with the POI. That is
        // intentionally not an interest relation. Agents 5 and 6 form a direct
        // supervisor/report pair with it.
        var flatNetwork = store.CreateEntity(new AgentNetworkData { TypeHash = 1 });
        agents[0].AddRelation(new AgentNetworkMembership { Network = flatNetwork });
        agents[3].AddRelation(new AgentNetworkMembership { Network = flatNetwork });
        agents[4].AddRelation(new AgentNetworkMembership { Network = flatNetwork });
        var hierarchy = store.CreateEntity(new AgentNetworkData { TypeHash = 2 });
        agents[5].AddRelation(new AgentNetworkMembership { Network = hierarchy });
        agents[0].AddRelation(new AgentNetworkMembership { Network = hierarchy, Supervisor = agents[5] });
        agents[6].AddRelation(new AgentNetworkMembership { Network = hierarchy, Supervisor = agents[0] });
        indexes.Rebuild(store);

        using var service = new AgentLodService(store, LoadCatalog().Lod, indexes);
        service.InitializeClassification();

        AssertTier(agents[0], AgentLodTier.Tier1, 0);
        AssertTier(agents[1], AgentLodTier.Tier2, 1);
        AssertTier(agents[5], AgentLodTier.Tier2, 1);
        AssertTier(agents[6], AgentLodTier.Tier2, 1);
        AssertTier(agents[2], AgentLodTier.Tier3, 0); // two hops away
        AssertTier(agents[3], AgentLodTier.Tier3, 0); // coworker only
        AssertTier(agents[4], AgentLodTier.Tier3, 0);

        Assert.True(agents[2].Tags.Has<Tier3LodTag>());
        Assert.False(agents[2].Tags.Has<DetailedSimulationTag>());
    }

    [Fact]
    public void InvestigationCommandsAreIdempotentAndReferenceCountOverlaps()
    {
        var (store, agents, indexes) = CreatePopulation(3);
        AddSocialPair(store, agents[0], agents[1]);
        AddSocialPair(store, agents[2], agents[1]);
        indexes.Rebuild(store);
        using var service = new AgentLodService(store, LoadCatalog().Lod, indexes);
        service.InitializeClassification();

        Assert.True(service.SetInvestigation(agents[0].Id, true));
        Assert.False(service.SetInvestigation(agents[0].Id, true));
        Assert.True(service.SetInvestigation(agents[2].Id, true));
        AssertTier(agents[1], AgentLodTier.Tier2, 2);

        Assert.True(service.SetInvestigation(agents[0].Id, false));
        AssertTier(agents[1], AgentLodTier.Tier2, 1);
        Assert.True(service.SetInvestigation(agents[2].Id, false));
        AssertTier(agents[1], AgentLodTier.Tier3, 0);

        Assert.Equal(new[]
        {
            new InvestigationChangedEvent(agents[0].Id, true),
            new InvestigationChangedEvent(agents[2].Id, true),
            new InvestigationChangedEvent(agents[0].Id, false),
            new InvestigationChangedEvent(agents[2].Id, false)
        }, service.DrainInvestigationEvents());
        Assert.Empty(service.DrainInvestigationEvents());
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetInvestigation(int.MaxValue, true));
    }

    [Fact]
    public void DeletingPoiReleasesItsNeighbourReferences()
    {
        var (store, agents, indexes) = CreatePopulation(2);
        agents[0].AddTag<OperativeTag>();
        AddSocialPair(store, agents[0], agents[1]);
        indexes.Rebuild(store);
        using var service = new AgentLodService(store, LoadCatalog().Lod, indexes);
        service.InitializeClassification();

        agents[0].DeleteEntity();

        AssertTier(agents[1], AgentLodTier.Tier3, 0);
    }

    private static (EntityStore Store, Entity[] Agents, AgentSocialIndexes Indexes) CreatePopulation(int count)
    {
        var store = new EntityStore();
        var agents = Enumerable.Range(0, count)
            .Select(index => store.CreateEntity(new Identity { NameId = index + 1 }))
            .ToArray();
        return (store, agents, new AgentSocialIndexes());
    }

    private static void AddSocialPair(EntityStore store, Entity left, Entity right)
    {
        store.CreateEntity(new EdgeData { Source = left, Target = right });
        store.CreateEntity(new EdgeData { Source = right, Target = left });
    }

    private static void AssertTier(Entity agent, AgentLodTier desired, int references)
    {
        var state = agent.GetComponent<AgentLodState>();
        Assert.Equal(desired, state.DesiredTier);
        Assert.Equal(references, state.DirectPoiReferenceCount);
        Assert.True(AgentLodService.HasExactlyOneTierTag(agent));
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
}
