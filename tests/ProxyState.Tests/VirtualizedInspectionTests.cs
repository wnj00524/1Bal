using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class VirtualizedInspectionTests
{
    [Fact]
    public void SearchCacheRebuildsOnlyForSearchOrIdentityChanges()
    {
        var agents = Enumerable.Range(1, 10).Select(id =>
            new PlayerIntelligenceAgentSnapshot(id, id * 10, false, IntelligenceRole.None, 0, false)).ToArray();
        var cache = new AgentIdentitySearchIndex();

        var first = cache.Update(agents, "Agent 2", 1);
        var repeated = cache.Update(agents, "Agent 2", 1);

        Assert.Same(first, repeated);
        Assert.Equal(new[] { 1 }, first);
        Assert.Equal(1, cache.RebuildCount);
        cache.Update(agents, "Agent 3", 1);
        cache.Update(agents, "Agent 3", 2);
        Assert.Equal(3, cache.RebuildCount);
    }

    [Fact]
    public void VisibleRangeVisitsOnlyClippedRowsAtHundredThousandScale()
    {
        var visited = new List<int>();
        var count = VisibleRowRange.Visit(100_000, 49_995, 50_015, visited.Add);

        Assert.Equal(20, count);
        Assert.Equal(Enumerable.Range(49_995, 20), visited);
    }

    [Fact]
    public void InvestigationActionEmitsToggleCommandAndRejectsOperatives()
    {
        var target = new PlayerIntelligenceAgentSnapshot(42, 7, false, IntelligenceRole.None, 0, false);
        Assert.Equal(new InvestigationCommand(42, true), DossierInvestigationActions.Toggle(target));
        Assert.Equal(new InvestigationCommand(42, false), DossierInvestigationActions.Toggle(target with { IsUnderInvestigation = true }));
        Assert.Throws<InvalidOperationException>(() => DossierInvestigationActions.Toggle(target with { IsOperative = true }));
    }

    [Fact]
    public void DebugProjectionCopiesOnlyChangedSelectionAndHandlesDeletion()
    {
        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var store = new EntityStore();
        var spawner = new AgentSpawner(catalog);
        spawner.Spawn(store, 8, 42);
        var projection = DebugInspectionProjection.Create(store, catalog);
        var selected = store.Query<Identity>().Entities.First();
        var selectedId = selected.Id;

        Assert.Null(projection.View.SelectedAgent);
        projection.Select(selectedId);
        Assert.Equal(selectedId, projection.View.SelectedAgent!.EntityId);
        Assert.Equal(1, projection.SelectedDetailCaptureCount);
        projection.Select(selectedId);
        Assert.Equal(1, projection.SelectedDetailCaptureCount);
        selected.DeleteEntity();
        projection.Select(null);
        projection.Select(selectedId);
        Assert.Null(projection.View.SelectedAgent);
        Assert.Equal(2, projection.SelectedDetailCaptureCount);
    }

    [Fact]
    public void LodFieldsRemainDebugOnly()
    {
        var playerNames = typeof(PlayerIntelligenceAgentSnapshot).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(playerNames, name => name.Contains("Lod", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Coarse", StringComparison.OrdinalIgnoreCase) || name.Contains("Demotion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nameof(DebugAgentSnapshot.LodTier), typeof(DebugAgentSnapshot).GetProperties().Select(property => property.Name));
        Assert.Contains(nameof(DebugAgentSnapshot.CoarseProfileId), typeof(DebugAgentSnapshot).GetProperties().Select(property => property.Name));
    }
}
