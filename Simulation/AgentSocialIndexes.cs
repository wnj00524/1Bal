using Friflo.Engine.ECS;

namespace ProxyState.Simulation;

/// <summary>
/// Compact immutable description of one directed relationship. Entity IDs are
/// retained instead of Entity values so the packed adjacency storage contains
/// only stable integer identifiers.
/// </summary>
public readonly record struct SocialEdgeIndexEntry(int TargetAgentId, int EdgeEntityId);

/// <summary>
/// Persistent lookup snapshot built after population and social graph creation.
/// Callers must explicitly invalidate and rebuild it if either graph changes.
/// </summary>
public sealed class AgentSocialIndexes
{
    private Entity[] _agentsById = [];
    private bool[] _agentExists = [];
    private SourceRange[] _sourceRanges = [];
    private SocialEdgeIndexEntry[] _outgoingEdges = [];
    private bool _populationChanged;
    private bool _socialGraphChanged;

    public int AgentCount { get; private set; }
    public int DirectedEdgeCount => _outgoingEdges.Length;

    /// <summary>Marks all lookups stale after agents are added or removed.</summary>
    public void NotifyPopulationChanged()
    {
        _populationChanged = true;
        _socialGraphChanged = true;
    }

    /// <summary>Marks relationship lookups stale after an EdgeData mutation.</summary>
    public void NotifySocialGraphChanged() => _socialGraphChanged = true;

    /// <summary>
    /// Rebuilds both sorted snapshots. Milestone 17.2 treats the ECS population
    /// and EdgeData graph as immutable between this call and a notification.
    /// </summary>
    public void Rebuild(EntityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var agents = store.Query<Identity>().Entities.ToArray();
        var maximumAgentId = agents.Length == 0 ? -1 : agents.Max(entity => entity.Id);
        var agentsById = new Entity[maximumAgentId + 1];
        var agentExists = new bool[maximumAgentId + 1];
        foreach (var agent in agents)
        {
            agentsById[agent.Id] = agent;
            agentExists[agent.Id] = true;
        }

        var edges = store.Query<EdgeData>().Entities
            .Select(entity =>
            {
                var edge = entity.GetComponent<EdgeData>();
                return new PendingEdge(edge.Source.Id, edge.Target.Id, entity.Id);
            })
            .ToArray();
        RadixSort(edges);

        var packedEdges = new SocialEdgeIndexEntry[edges.Length];
        var ranges = new SourceRange[maximumAgentId + 1];
        var edgeIndex = 0;
        while (edgeIndex < edges.Length)
        {
            var sourceId = edges[edgeIndex].SourceAgentId;
            var start = edgeIndex;
            while (edgeIndex < edges.Length && edges[edgeIndex].SourceAgentId == sourceId)
            {
                var edge = edges[edgeIndex];
                packedEdges[edgeIndex] = new SocialEdgeIndexEntry(edge.TargetAgentId, edge.EdgeEntityId);
                edgeIndex++;
            }
            if ((uint)sourceId < (uint)ranges.Length)
                ranges[sourceId] = new SourceRange(start, edgeIndex - start);
        }

        _agentsById = agentsById;
        _agentExists = agentExists;
        AgentCount = agents.Length;
        _sourceRanges = ranges;
        _outgoingEdges = packedEdges;
        _populationChanged = false;
        _socialGraphChanged = false;
    }

    public bool TryGetAgent(int agentId, out Entity entity)
    {
        EnsurePopulationCurrent();
        if ((uint)agentId < (uint)_agentsById.Length && _agentExists[agentId])
        {
            entity = _agentsById[agentId];
            return true;
        }

        entity = default;
        return false;
    }

    public int GetOutgoingRelationshipCount(int sourceAgentId)
        => GetOutgoingEdges(sourceAgentId).Length;

    /// <summary>
    /// Returns a non-allocating view over only the requested source's packed
    /// range. The view remains valid until the next rebuild.
    /// </summary>
    public ReadOnlySpan<SocialEdgeIndexEntry> GetOutgoingEdges(int sourceAgentId)
    {
        EnsureSocialGraphCurrent();
        if ((uint)sourceAgentId >= (uint)_sourceRanges.Length)
            return ReadOnlySpan<SocialEdgeIndexEntry>.Empty;
        var range = _sourceRanges[sourceAgentId];
        return _outgoingEdges.AsSpan(range.Start, range.Count);
    }

    public bool TryGetDirectedEdge(int sourceAgentId, int targetAgentId, out SocialEdgeIndexEntry edge)
    {
        var outgoing = GetOutgoingEdges(sourceAgentId);
        var low = 0;
        var high = outgoing.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = outgoing[middle].TargetAgentId.CompareTo(targetAgentId);
            if (comparison == 0)
            {
                edge = outgoing[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        edge = default;
        return false;
    }

    // Stable least-significant-byte radix passes order by source, target, then
    // edge ID in linear time without comparison-sort or per-edge objects.
    private static void RadixSort(PendingEdge[] values)
    {
        if (values.Length < 2) return;
        var scratch = new PendingEdge[values.Length];
        var source = values;
        var destination = scratch;
        Span<int> counts = stackalloc int[256];
        for (var pass = 0; pass < 12; pass++)
        {
            counts.Clear();
            var field = pass / 4;
            var shift = (pass % 4) * 8;
            foreach (var value in source) counts[GetByte(value, field, shift)]++;
            var offset = 0;
            for (var index = 0; index < counts.Length; index++)
            {
                var count = counts[index];
                counts[index] = offset;
                offset += count;
            }
            foreach (var value in source)
                destination[counts[GetByte(value, field, shift)]++] = value;
            (source, destination) = (destination, source);
        }
    }

    private static int GetByte(PendingEdge edge, int field, int shift)
    {
        // XOR handles the sign bit while preserving ordinary positive entity IDs.
        var value = field switch
        {
            0 => edge.EdgeEntityId,
            1 => edge.TargetAgentId,
            _ => edge.SourceAgentId
        };
        return (int)(((uint)(value ^ int.MinValue) >> shift) & 0xff);
    }

    private void EnsurePopulationCurrent()
    {
        if (_populationChanged)
            throw new InvalidOperationException("Agent indexes are stale. Call Rebuild after changing the population.");
    }

    private void EnsureSocialGraphCurrent()
    {
        EnsurePopulationCurrent();
        if (_socialGraphChanged)
            throw new InvalidOperationException("Social indexes are stale. Call Rebuild after changing EdgeData entities.");
    }

    private readonly record struct PendingEdge(int SourceAgentId, int TargetAgentId, int EdgeEntityId);
    private readonly record struct SourceRange(int Start, int Count);
}
