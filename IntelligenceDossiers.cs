using System.Collections.ObjectModel;
using System.Numerics;
using Friflo.Engine.ECS;
using ImGuiNET;
using ProxyState.Simulation;

namespace ProxyState;

/// <summary>
/// The UI-facing copy of one agent's identity and the intelligence known by
/// the Operative team. It intentionally contains no ECS Entity reference.
/// </summary>
public sealed record PlayerIntelligenceAgentSnapshot(
    int EntityId,
    int NameId,
    bool IsOperative,
    long KnownTraitMask)
{
    public string DisplayName => $"Agent {EntityId} (Name ID {NameId})";
}

/// <summary>
/// Read-only intelligence database captured at the ECS/UI boundary. Each
/// target mask is the union of the directional knowledge held by all current
/// Operatives, so the terminal represents the player's team rather than one
/// specific character.
/// </summary>
public sealed class PlayerIntelligenceDB
{
    private readonly IReadOnlyDictionary<int, PlayerIntelligenceAgentSnapshot> _agentsById;

    private PlayerIntelligenceDB(
        IReadOnlyList<int> operativeEntityIds,
        IReadOnlyList<PlayerIntelligenceAgentSnapshot> agents)
    {
        OperativeEntityIds = operativeEntityIds;
        Agents = agents;
        _agentsById = agents.ToDictionary(agent => agent.EntityId);
    }

    public IReadOnlyList<int> OperativeEntityIds { get; }

    public IReadOnlyList<PlayerIntelligenceAgentSnapshot> Agents { get; }

    /// <summary>
    /// Copies only identity, team membership, and known relationship masks out
    /// of the ECS store. Ground-truth Psychology is never placed in the copy.
    /// </summary>
    public static PlayerIntelligenceDB Capture(EntityStore store, ContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);

        var agents = store.Query<Identity>().Entities
            .OrderBy(entity => entity.Id)
            .ToArray();
        var operativeEntityIds = agents
            .Where(entity => entity.Tags.Has<OperativeTag>())
            .Select(entity => entity.Id)
            .ToHashSet();
        var knownMasksByTarget = new Dictionary<int, long>();

        foreach (var edgeEntity in store.Query<EdgeData>().Entities)
        {
            var edge = edgeEntity.GetComponent<EdgeData>();
            if (!operativeEntityIds.Contains(edge.Source.Id))
            {
                continue;
            }

            var targetId = edge.Target.Id;
            var knownMask = edge.KnownTraitMask & catalog.AllTraitBits;
            knownMasksByTarget[targetId] = knownMasksByTarget.GetValueOrDefault(targetId) | knownMask;
        }

        var operativeIds = new ReadOnlyCollection<int>(operativeEntityIds.OrderBy(id => id).ToList());
        var snapshots = agents
            .Select(entity =>
            {
                var identity = entity.GetComponent<Identity>();
                return new PlayerIntelligenceAgentSnapshot(
                    entity.Id,
                    identity.NameId,
                    operativeEntityIds.Contains(entity.Id),
                    knownMasksByTarget.GetValueOrDefault(entity.Id));
            })
            .ToList();

        return new PlayerIntelligenceDB(
            operativeIds,
            new ReadOnlyCollection<PlayerIntelligenceAgentSnapshot>(snapshots));
    }

    public bool TryGetAgent(int entityId, out PlayerIntelligenceAgentSnapshot? agent) =>
        _agentsById.TryGetValue(entityId, out agent);
}

/// <summary>
/// Formats a configured trait using only the team knowledge mask. The
/// bitwise check is kept in a pure helper so the security rule is testable
/// without creating an ImGui context.
/// </summary>
public static class DossierTraitFormatter
{
    public static string Format(TraitDefinition trait, long knownTraitMask)
    {
        ArgumentNullException.ThrowIfNull(trait);
        return (knownTraitMask & trait.Bit) != 0 ? trait.Name : "Trait: ???";
    }
}

public sealed class DossierWindow
{
    private int? _selectedAgentId;

    public void Draw(
        PlayerIntelligenceDB intelligence,
        IReadOnlyList<TraitDefinition> traits,
        ref bool isOpen)
    {
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentNullException.ThrowIfNull(traits);

        if (_selectedAgentId is not null &&
            !intelligence.Agents.Any(agent => agent.EntityId == _selectedAgentId.Value))
        {
            _selectedAgentId = null;
        }

        if (!ImGui.Begin(ApplicationShell.DossiersWindowTitle, ref isOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Operative Intelligence");
        ImGui.Text($"Operatives: {intelligence.OperativeEntityIds.Count}");
        ImGui.Text($"Agents: {intelligence.Agents.Count}");
        ImGui.Separator();

        ImGui.BeginChild("dossier-agent-list", new Vector2(300, 0), ImGuiChildFlags.Borders);
        foreach (var agent in intelligence.Agents)
        {
            var selected = agent.EntityId == _selectedAgentId;
            var label = agent.IsOperative
                ? $"[OPERATIVE] {agent.DisplayName}"
                : agent.DisplayName;
            if (ImGui.Selectable(label, selected))
            {
                _selectedAgentId = agent.EntityId;
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("dossier-agent-details", new Vector2(0, 0), ImGuiChildFlags.Borders);

        if (intelligence.TryGetAgent(_selectedAgentId ?? -1, out var selectedAgent) && selectedAgent is not null)
        {
            DrawDetails(selectedAgent, traits);
        }
        else
        {
            ImGui.Text("Select an agent to open its dossier.");
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private static void DrawDetails(
        PlayerIntelligenceAgentSnapshot agent,
        IReadOnlyList<TraitDefinition> traits)
    {
        ImGui.Text(agent.DisplayName);
        if (agent.IsOperative)
        {
            ImGui.Text("Operative team member");
        }

        ImGui.Separator();
        ImGui.Text("Known traits");
        foreach (var trait in traits)
        {
            ImGui.BulletText(DossierTraitFormatter.Format(trait, agent.KnownTraitMask));
        }
    }
}
