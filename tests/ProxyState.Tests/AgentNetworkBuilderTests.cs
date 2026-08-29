using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentNetworkBuilderTests
{
    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public void GenerationIsRepeatableAndCoversEveryAgentOncePerType()
    {
        var first = Spawn(160, 7441);
        var second = Spawn(160, 7441);

        Assert.Equal(Capture(first.Store, first.Catalog), Capture(second.Store, second.Catalog));
        Assert.All(first.Agents, agent =>
        {
            var memberships = new List<AgentNetworkMembership>();
            foreach (var membership in agent.GetRelations<AgentNetworkMembership>()) memberships.Add(membership);
            Assert.Equal(2, memberships.Count);
            Assert.Equal(2, memberships.Select(membership =>
                first.Catalog.Networks.GetType(membership.Network.GetComponent<AgentNetworkData>().TypeHash).Id)
                .Distinct().Count());
        });
    }

    [Fact]
    public void NetworksAreBoundedAnchoredAndRespectHierarchyRoles()
    {
        var result = Spawn(300, 9082);
        var family = result.Catalog.Networks.GetType("family");
        var company = result.Catalog.Networks.GetType("company");
        var familyGenerator = result.Catalog.Networks.GetGenerator("families-by-home");
        var companyGenerator = result.Catalog.Networks.GetGenerator("companies-by-work");

        foreach (var network in result.Store.Query<AgentNetworkData>().Entities)
        {
            var data = network.GetComponent<AgentNetworkData>();
            var members = GetMembers(network);
            var generator = data.TypeHash == family.Hash ? familyGenerator : companyGenerator;

            Assert.InRange(members.Length, 1, generator.MaximumSize);
            Assert.All(members, member => Assert.Equal(data.AnchorLocationId,
                data.TypeHash == family.Hash
                    ? member.Agent.GetComponent<AgentLocation>().HomeLocationId
                    : member.Agent.GetComponent<AgentLocation>().WorkLocationId));

            if (data.TypeHash == family.Hash)
            {
                Assert.All(members, member =>
                {
                    Assert.Equal(familyGenerator.MemberRoleHash, member.Membership.RoleHash);
                    Assert.True(member.Membership.Supervisor.IsNull);
                });
                continue;
            }

            var roots = members.Where(member => member.Membership.Supervisor.IsNull).ToArray();
            Assert.Single(roots);
            Assert.Equal(companyGenerator.RootRoleHash, roots[0].Membership.RoleHash);
            foreach (var member in members)
            {
                var reports = members.Count(candidate => candidate.Membership.Supervisor == member.Agent);
                Assert.InRange(reports, 0, companyGenerator.MaximumSpanOfControl);
                if (member.Agent != roots[0].Agent)
                    Assert.Equal(reports > 0 ? companyGenerator.ManagerRoleHash : companyGenerator.LeafRoleHash,
                        member.Membership.RoleHash);

                var depth = 0;
                var cursor = member.Membership.Supervisor;
                var visited = new HashSet<Entity> { member.Agent };
                while (!cursor.IsNull)
                {
                    Assert.True(visited.Add(cursor), "Generated hierarchy contains a cycle.");
                    depth++;
                    cursor = members.Single(candidate => candidate.Agent == cursor).Membership.Supervisor;
                }
                Assert.InRange(depth, 0, companyGenerator.MaximumDepth);
            }
        }
    }

    [Fact]
    public void NetworkGenerationDoesNotPerturbOperativesPopulationOrSocialEdges()
    {
        var catalog = LoadCatalog();
        var normalStore = new EntityStore();
        var noNetworkStore = new EntityStore();
        var normalSpawner = new AgentSpawner(catalog);
        var seed = 6119;

        normalSpawner.Spawn(normalStore, 80, seed);
        // Reproduce the independent streams around an intentionally skipped
        // network phase to prove its random draws cannot affect later phases.
        var populationOnly = new AgentSpawner(catalog, new SocialGraphBuilder());
        populationOnly.Spawn(noNetworkStore, 80, seed, generateNetworks: false);

        Assert.Equal(CaptureAgents(normalStore), CaptureAgents(noNetworkStore));
        Assert.Equal(CaptureEdges(normalStore), CaptureEdges(noNetworkStore));
    }

    private static (EntityStore Store, ContentCatalog Catalog, Entity[] Agents) Spawn(int count, int seed)
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, count, seed);
        return (store, catalog, store.Query<Identity>().Entities.ToArray());
    }

    private static string[] Capture(EntityStore store, ContentCatalog catalog) =>
        store.Query<AgentNetworkData>().Entities.Select(network =>
        {
            var data = network.GetComponent<AgentNetworkData>();
            var members = GetMembers(network).OrderBy(member => member.Agent.Id)
                .Select(member => $"{member.Agent.Id}:{member.Membership.RoleHash}:{member.Membership.Supervisor.Id}");
            return $"{data.TypeHash}:{data.AnchorLocationId}:{data.Ordinal}:{string.Join(',', members)}";
        }).ToArray();

    private static (Entity Agent, AgentNetworkMembership Membership)[] GetMembers(Entity network)
    {
        var members = new List<(Entity, AgentNetworkMembership)>();
        foreach (var link in network.GetIncomingLinks<AgentNetworkMembership>())
            members.Add((link.Entity, link.Entity.GetRelation<AgentNetworkMembership, Entity>(network)));
        return members.ToArray();
    }

    private static string[] CaptureAgents(EntityStore store) => store.Query<Identity>().Entities
        .Select(agent => $"{agent.GetComponent<Identity>().NameId}:{agent.GetComponent<AgentLocation>().HomeLocationId}:" +
            $"{agent.GetComponent<AgentLocation>().WorkLocationId}:{agent.Tags.Has<OperativeTag>()}")
        .ToArray();

    private static string[] CaptureEdges(EntityStore store) => store.Query<EdgeData>().Entities
        .Select(edge => edge.GetComponent<EdgeData>())
        .Select(edge => $"{edge.Source.Id}:{edge.Target.Id}")
        .ToArray();
}
