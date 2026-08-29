using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentNetworkServiceTests
{
    [Fact]
    public void FlatNetworksEnforceRoleCardinalityAndNoSupervisors()
    {
        var (store, service, catalog) = CreateService();
        using (service)
        {
            var family = service.CreateNetwork(catalog.GetType("family").Hash, 42, 0);
            var secondFamily = service.CreateNetwork(catalog.GetType("family").Hash, 42, 1);
            var agent = Agent(store);
            var other = Agent(store);

            service.AddMembership(agent, family, catalog.GetRole("family-member").Hash);

            Assert.Equal(42, family.GetComponent<AgentNetworkData>().AnchorLocationId);
            Assert.Equal(family, service.GetMembership(agent, family).Network);
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(agent, family,
                catalog.GetRole("family-member").Hash));
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(agent, secondFamily,
                catalog.GetRole("family-member").Hash));
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(other, family,
                catalog.GetRole("company-employee").Hash));
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(other, family,
                catalog.GetRole("family-member").Hash, agent));
        }
    }

    [Fact]
    public void HierarchicalNetworksRejectMissingForeignSelfAndCyclicSupervisors()
    {
        var (store, service, catalog) = CreateService();
        using (service)
        {
            var company = service.CreateNetwork(catalog.GetType("company").Hash, 7, 0);
            var otherCompany = service.CreateNetwork(catalog.GetType("company").Hash, 8, 1);
            var root = Agent(store);
            var manager = Agent(store);
            var employee = Agent(store);
            var outsider = Agent(store);
            service.AddMembership(root, company, catalog.GetRole("company-head").Hash);
            service.AddMembership(outsider, otherCompany, catalog.GetRole("company-head").Hash);

            Assert.Throws<InvalidOperationException>(() => service.AddMembership(manager, company,
                catalog.GetRole("company-manager").Hash));
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(manager, company,
                catalog.GetRole("company-manager").Hash, manager));
            Assert.Throws<InvalidOperationException>(() => service.AddMembership(manager, company,
                catalog.GetRole("company-manager").Hash, outsider));

            service.AddMembership(manager, company, catalog.GetRole("company-manager").Hash, root);
            service.AddMembership(employee, company, catalog.GetRole("company-employee").Hash, manager);
            Assert.Throws<InvalidOperationException>(() => service.ChangeSupervisor(root, company, employee));
        }
    }

    [Fact]
    public void ManagerRemovalReparentsReportsAndRootRemovalUsesExplicitSuccessor()
    {
        var (store, service, catalog) = CreateService();
        using (service)
        {
            var company = service.CreateNetwork(catalog.GetType("company").Hash, 7, 0);
            var root = Agent(store);
            var manager = Agent(store);
            var employee = Agent(store);
            service.AddMembership(root, company, catalog.GetRole("company-head").Hash);
            service.AddMembership(manager, company, catalog.GetRole("company-manager").Hash, root);
            service.AddMembership(employee, company, catalog.GetRole("company-employee").Hash, manager);

            service.RemoveMembership(manager, company);
            Assert.Equal(root, service.GetMembership(employee, company).Supervisor);
            Assert.Throws<InvalidOperationException>(() => service.RemoveMembership(root, company));

            service.RemoveMembership(root, company, employee);
            Assert.True(service.GetMembership(employee, company).Supervisor.IsNull);
        }
    }

    [Fact]
    public void ExternalAgentAndNetworkDeletionCleansSupervisorAndRelationLinks()
    {
        var (store, service, catalog) = CreateService();
        using (service)
        {
            var company = service.CreateNetwork(catalog.GetType("company").Hash, 7, 0);
            var root = Agent(store);
            var manager = Agent(store);
            var employee = Agent(store);
            service.AddMembership(root, company, catalog.GetRole("company-head").Hash);
            service.AddMembership(manager, company, catalog.GetRole("company-manager").Hash, root);
            service.AddMembership(employee, company, catalog.GetRole("company-employee").Hash, manager);

            manager.DeleteEntity();
            Assert.Equal(root, service.GetMembership(employee, company).Supervisor);

            company.DeleteEntity();
            Assert.Empty(service.GetMemberships(root));
            Assert.Empty(service.GetMemberships(employee));
        }
    }

    private static (EntityStore Store, AgentNetworkService Service, AgentNetworkCatalog Catalog) CreateService()
    {
        var store = new EntityStore();
        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data")).Networks;
        return (store, new AgentNetworkService(store, catalog), catalog);
    }

    private static Entity Agent(EntityStore store) => store.CreateEntity(new Identity());
}
