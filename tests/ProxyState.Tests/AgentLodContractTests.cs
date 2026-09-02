using System.Text.Json.Nodes;
using Friflo.Engine.ECS;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentLodContractTests
{
    [Fact]
    public void ProductionLodSettingsLoadForTransitionalRollout()
    {
        var settings = LoadCatalog().Lod;

        Assert.True(settings.Enabled);
        Assert.False(settings.Tier3Enabled);
        Assert.Equal(60, settings.Tier2DecisionIntervalMinutes);
        Assert.Equal(new[]
        {
            AgentRelationKind.Social,
            AgentRelationKind.NetworkSupervisor,
            AgentRelationKind.NetworkDirectReport
        }, settings.RelatedBy);
        Assert.Equal(AgentDemotionPolicy.EndOfDay, settings.DemotionPolicy);
        Assert.Equal(24, settings.Tier3ShardCount);
    }

    [Theory]
    [InlineData("tier2.decisionIntervalMinutes", 0, "lod.json:tier2.decisionIntervalMinutes")]
    [InlineData("tier3.shardCount", 0, "lod.json:tier3.shardCount")]
    public void NonPositiveFixedValuesFailWithJsonPath(string property, int value, string expectedPath)
    {
        using var content = MutableLodContent.Create();
        content.SetInteger(property, value);

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains(expectedPath, exception.Message);
    }

    [Fact]
    public void UnknownRelationshipFailsWithArrayPath()
    {
        using var content = MutableLodContent.Create();
        content.Root["tier2"]!["relatedBy"]![1] = "sharedCompany";
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("lod.json:tier2.relatedBy[1]", exception.Message);
    }

    [Fact]
    public void UnknownDemotionPolicyFailsWithPropertyPath()
    {
        using var content = MutableLodContent.Create();
        content.Root["demotionPolicy"] = "immediate";
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("lod.json:demotionPolicy", exception.Message);
    }

    [Fact]
    public void ServiceMaintainsExclusiveTierAndDetailedTags()
    {
        var catalog = LoadCatalog();
        var service = new AgentLodService(catalog.Lod);
        var store = new EntityStore();
        var entity = store.CreateEntity();

        service.InitializeTierOne(entity);
        Assert.True(AgentLodService.HasExactlyOneTierTag(entity));
        Assert.True(entity.Tags.Has<Tier1LodTag>());
        Assert.True(entity.Tags.Has<DetailedSimulationTag>());

        service.SetDesiredTier(entity, AgentLodTier.Tier2);
        Assert.True(AgentLodService.HasExactlyOneTierTag(entity));
        Assert.True(entity.Tags.Has<Tier2LodTag>());
        Assert.True(entity.Tags.Has<DetailedSimulationTag>());

        service.SetDesiredTier(entity, AgentLodTier.Tier3);
        Assert.Equal(AgentLodTier.Tier3, entity.GetComponent<AgentLodState>().DesiredTier);
        Assert.True(entity.Tags.Has<Tier2LodTag>());
        Assert.True(entity.Tags.Has<DetailedSimulationTag>());

        entity.AddTag<Tier3LodTag>();
        Assert.False(AgentLodService.HasExactlyOneTierTag(entity));
    }

    [Fact]
    public void SpawnedAgentsReceivePoiClassificationAfterBootstrap()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();

        new AgentSpawner(catalog).Spawn(store, 8, 18_001);

        Assert.Equal(5, store.Query<AgentLodState>()
            .AllTags(Tags.Get<Tier1LodTag, DetailedSimulationTag>()).Count);
        Assert.Equal(8, store.Query<AgentLodState>().AllTags(Tags.Get<DetailedSimulationTag>()).Count);
        Assert.All(store.Query<AgentLodState>().Entities,
            entity => Assert.True(AgentLodService.HasExactlyOneTierTag(entity)));
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private sealed class MutableLodContent : IDisposable
    {
        private readonly string _path;

        private MutableLodContent(string directory, string path, JsonObject root)
        {
            Directory = directory;
            _path = path;
            Root = root;
        }

        public string Directory { get; }
        public JsonObject Root { get; }

        public static MutableLodContent Create()
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            var directory = Path.Combine(Path.GetTempPath(), $"proxystate-lod-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            foreach (var file in System.IO.Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var path = Path.Combine(directory, "lod.json");
            return new MutableLodContent(directory, path,
                JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        }

        public void SetInteger(string property, int value)
        {
            var segments = property.Split('.');
            Root[segments[0]]![segments[1]] = value;
            Save();
        }

        public void Save() => File.WriteAllText(_path, Root.ToJsonString());

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
