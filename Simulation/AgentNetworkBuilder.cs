using Friflo.Engine.ECS;

namespace ProxyState.Simulation;

/// <summary>
/// Deterministically partitions fully assigned agents and creates the runtime
/// network entities and memberships through <see cref="AgentNetworkService"/>.
/// </summary>
public sealed class AgentNetworkBuilder
{
    private readonly AgentNetworkCatalog _catalog;

    public AgentNetworkBuilder(AgentNetworkCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public void Populate(AgentNetworkService service, IReadOnlyList<Entity> agents, Random random)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(random);

        foreach (var generator in _catalog.Generators)
        {
            var ordinal = 0;
            foreach (var bucket in Partition(agents, generator.PartitionStrategy))
            {
                Shuffle(bucket.Agents, random);
                foreach (var group in Divide(bucket.Agents, generator, random))
                {
                    var network = service.CreateNetwork(generator.NetworkTypeHash, bucket.LocationId, ordinal++);
                    if (_catalog.GetType(generator.NetworkTypeHash).HierarchyMode == NetworkHierarchyMode.Flat)
                        AddFlat(service, network, group, generator.MemberRoleHash);
                    else
                        AddHierarchy(service, network, group, generator);
                }
            }
        }
    }

    private static IEnumerable<(int LocationId, Entity[] Agents)> Partition(
        IReadOnlyList<Entity> agents, NetworkPartitionStrategy strategy) =>
        agents.GroupBy(agent => strategy == NetworkPartitionStrategy.HomeLocation
                ? agent.GetComponent<AgentLocation>().HomeLocationId
                : agent.GetComponent<AgentLocation>().WorkLocationId)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.OrderBy(agent => agent.Id).ToArray()));

    private static IEnumerable<Entity[]> Divide(Entity[] agents, NetworkGeneratorDefinition generator, Random random)
    {
        var offset = 0;
        while (offset < agents.Length)
        {
            var remaining = agents.Length - offset;
            var size = Math.Min(SampleSize(generator.SizeWeights, random), remaining);

            if (generator.RemainderHandling == NetworkRemainderHandling.MergeIntoPrevious &&
                remaining - size > 0 && remaining - size < generator.MinimumSize)
            {
                size = Math.Min(remaining, generator.MaximumSize);
            }

            yield return agents.AsSpan(offset, size).ToArray();
            offset += size;
        }
    }

    private static int SampleSize(IReadOnlyList<NetworkSizeWeight> weights, Random random)
    {
        var total = weights.Sum(item => item.Weight);
        var roll = random.Next(total);
        foreach (var item in weights)
        {
            if (roll < item.Weight) return item.Size;
            roll -= item.Weight;
        }
        throw new InvalidOperationException("A validated size distribution must contain positive weight.");
    }

    private static void AddFlat(AgentNetworkService service, Entity network, Entity[] members, int roleHash)
    {
        foreach (var member in members) service.AddMembership(member, network, roleHash);
    }

    private static void AddHierarchy(
        AgentNetworkService service, Entity network, Entity[] members, NetworkGeneratorDefinition generator)
    {
        var supervisors = new Entity[members.Length];
        var hasReports = new bool[members.Length];
        var level = new List<int> { 0 };
        var nextMember = 1;

        // Build one breadth-first level at a time and distribute its children
        // evenly. This keeps spans balanced while never exceeding the target.
        for (var depth = 0; nextMember < members.Length && depth < generator.MaximumDepth; depth++)
        {
            var childCount = Math.Min(members.Length - nextMember, level.Count * generator.TargetSpanOfControl);
            var nextLevel = new List<int>(childCount);
            for (var child = 0; child < childCount; child++)
            {
                var parentIndex = level[child % level.Count];
                var childIndex = nextMember++;
                supervisors[childIndex] = members[parentIndex];
                hasReports[parentIndex] = true;
                nextLevel.Add(childIndex);
            }
            level = nextLevel;
        }

        if (nextMember != members.Length)
            throw new InvalidOperationException("Generated company exceeds its validated hierarchy capacity.");

        service.AddMembership(members[0], network, generator.RootRoleHash);
        for (var index = 1; index < members.Length; index++)
            service.AddMembership(members[index], network, generator.LeafRoleHash, supervisors[index]);
        for (var index = 1; index < members.Length; index++)
            if (hasReports[index]) service.ChangeRole(members[index], network, generator.ManagerRoleHash);
    }

    private static void Shuffle(Entity[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}
