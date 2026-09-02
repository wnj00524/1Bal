using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentLodLifecycleTests
{
    [Theory]
    [InlineData(0, 1440)]
    [InlineData(719, 1440)]
    [InlineData(1440, 2880)]
    public void InvestigationRemovalWaitsForNextDayBoundary(long minute, long expectedBoundary)
    {
        var fixture = CreateFixture(1, minute);
        using var service = fixture.Service;
        var agent = fixture.Agents[0];

        service.SetInvestigation(agent.Id, true);
        service.SetInvestigation(agent.Id, false);

        Assert.True(agent.Tags.Has<Tier1LodTag>());
        Assert.Equal(expectedBoundary, agent.GetComponent<AgentLodState>().ScheduledDemotionMinute);
        SetMinute(fixture.Clock, expectedBoundary - 1);
        service.ProcessScheduledDemotions();
        Assert.True(agent.Tags.Has<Tier1LodTag>());
        SetMinute(fixture.Clock, expectedBoundary);
        service.ProcessScheduledDemotions();
        Assert.True(agent.Tags.Has<Tier2LodTag>()); // Tier 3 rollout remains disabled.
    }

    [Fact]
    public void InteractionPinSurvivesBoundaryAndReleaseStartsFreshGrace()
    {
        var fixture = CreateFixture(1, 100);
        using var service = fixture.Service;
        var agent = fixture.Agents[0];
        service.SetInvestigation(agent.Id, true);
        service.AcquireInteractionPin(agent);
        service.SetInvestigation(agent.Id, false);

        SetMinute(fixture.Clock, 1440);
        service.ProcessScheduledDemotions();
        Assert.True(agent.Tags.Has<Tier1LodTag>());
        Assert.True(agent.GetComponent<AgentLodState>().InterestReasons.HasFlag(AgentInterestReason.ActiveInteraction));

        service.ReleaseInteractionPin(agent);
        Assert.Equal(2880, agent.GetComponent<AgentLodState>().ScheduledDemotionMinute);
        service.ProcessScheduledDemotions();
        Assert.True(agent.Tags.Has<Tier1LodTag>());
        SetMinute(fixture.Clock, 2880);
        service.ProcessScheduledDemotions();
        Assert.True(agent.Tags.Has<Tier2LodTag>());
    }

    [Fact]
    public void NetworkServiceNotifiesLodForSupervisorReassignmentAndRemoval()
    {
        var fixture = CreateFixture(3, 0);
        using var service = fixture.Service;
        var catalog = LoadCatalog();
        using var networks = new AgentNetworkService(fixture.Store, catalog.Networks, service);
        var companyType = catalog.Networks.GetType("company");
        var network = networks.CreateNetwork(companyType.Hash, 0, 0);
        var headRole = catalog.Networks.GetRole("company-head").Hash;
        var employeeRole = catalog.Networks.GetRole("company-employee").Hash;
        var poi = fixture.Agents[0];
        var manager = fixture.Agents[1];
        var report = fixture.Agents[2];
        service.SetInvestigation(poi.Id, true);

        networks.AddMembership(poi, network, headRole);
        networks.AddMembership(manager, network, employeeRole, poi);
        networks.AddMembership(report, network, employeeRole, manager);
        Assert.Equal(1, manager.GetComponent<AgentLodState>().DirectPoiReferenceCount);
        Assert.Equal(0, report.GetComponent<AgentLodState>().DirectPoiReferenceCount);

        networks.ChangeSupervisor(report, network, poi);
        Assert.Equal(1, report.GetComponent<AgentLodState>().DirectPoiReferenceCount);
        networks.RemoveMembership(report, network);
        Assert.Equal(0, report.GetComponent<AgentLodState>().DirectPoiReferenceCount);
        Assert.Equal(1440, report.GetComponent<AgentLodState>().ScheduledDemotionMinute);
    }

    [Fact]
    public void MemberDeletionReassignsReportAndRefreshesPoiReference()
    {
        var fixture = CreateFixture(3, 0);
        using var service = fixture.Service;
        var catalog = LoadCatalog();
        using var networks = new AgentNetworkService(fixture.Store, catalog.Networks, service);
        var company = catalog.Networks.GetType("company");
        var network = networks.CreateNetwork(company.Hash, 0, 0);
        var poi = fixture.Agents[0];
        var manager = fixture.Agents[1];
        var report = fixture.Agents[2];
        service.SetInvestigation(poi.Id, true);
        networks.AddMembership(poi, network, catalog.Networks.GetRole("company-head").Hash);
        networks.AddMembership(manager, network, catalog.Networks.GetRole("company-employee").Hash, poi);
        networks.AddMembership(report, network, catalog.Networks.GetRole("company-employee").Hash, manager);

        manager.DeleteEntity();

        Assert.Equal(poi, networks.GetMembership(report, network).Supervisor);
        Assert.Equal(1, report.GetComponent<AgentLodState>().DirectPoiReferenceCount);
        Assert.Equal(AgentLodTier.Tier2, report.GetComponent<AgentLodState>().DesiredTier);
    }

    private static (EntityStore Store, Entity Clock, Entity[] Agents, AgentLodService Service) CreateFixture(
        int count, long minute)
    {
        var store = new EntityStore();
        var clock = store.CreateEntity(new WorldTime());
        SetMinute(clock, minute);
        var agents = Enumerable.Range(0, count)
            .Select(index => store.CreateEntity(new Identity { NameId = index + 1 }))
            .ToArray();
        var service = new AgentLodService(store, LoadCatalog().Lod, new AgentSocialIndexes());
        service.InitializeClassification();
        return (store, clock, agents, service);
    }

    private static void SetMinute(Entity clock, long minute)
    {
        ref var time = ref clock.GetComponent<WorldTime>();
        time.ElapsedSimulationSeconds = minute * SimulationDefaults.SimulationSecondsPerMinute;
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
}
