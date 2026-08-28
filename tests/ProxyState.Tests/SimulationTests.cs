using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class SimulationTests
{
    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public void CoreDataTypesUseFrifloComponentAndTagInterfaces()
    {
        var components = new[]
        {
            typeof(Identity),
            typeof(PoliticalAlignment),
            typeof(AgentAttributes),
            typeof(Psychology),
            typeof(AgentState)
        };
        var tags = new[] { typeof(Tier1LodTag), typeof(Tier2LodTag), typeof(Tier3LodTag) };

        Assert.All(components, type => Assert.True(typeof(IComponent).IsAssignableFrom(type), type.Name));
        Assert.All(tags, type => Assert.True(typeof(ITag).IsAssignableFrom(type), type.Name));
    }

    [Fact]
    public void SpawnerCreatesTheRequestedPopulationWithGeneralizedAttributesAndTierOneTag()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        var spawned = new AgentSpawner(catalog).Spawn(store, SimulationDefaults.AgentCount, new Random(1234));

        Assert.Equal(SimulationDefaults.AgentCount, spawned);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<Identity>().Count);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<PoliticalAlignment>().Count);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<AgentAttributes>().Count);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<Psychology>().Count);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<AgentState>().Count);
        Assert.Equal(SimulationDefaults.AgentCount, store.Query<AgentAttributes>().AllTags(Tags.Get<Tier1LodTag>()).Count);
    }

    [Fact]
    public void SpawnerGeneratesEverySchemaAttributeWithinItsConfiguredRange()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, SimulationDefaults.AgentCount, new Random(5678));

        foreach (var entity in store.Query<AgentAttributes>().Entities)
        {
            var values = entity.GetComponent<AgentAttributes>().Values;
            Assert.Equal(catalog.AgentAttributes.Count, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                var definition = catalog.AgentAttributes.Definitions[index];
                Assert.InRange(values[index], definition.Min, definition.Max);
            }

            var psychology = entity.GetComponent<Psychology>();
            Assert.Equal(0L, psychology.TraitMask & ~catalog.AllTraitBits);
            Assert.Contains(entity.GetComponent<AgentState>().CurrentActionHash,
                catalog.Actions.Select(action => action.Hash));
        }
    }

    [Fact]
    public void SchemaSupportsAdditionalAttributesWithoutSpawnerChanges()
    {
        using var directory = TestContent.CreateDirectory();
        TestContent.CopyCatalogFiles(directory.RootPath);
        File.WriteAllText(System.IO.Path.Combine(directory.RootPath, "agent-schema.json"),
            "{\"attributes\":[{\"id\":\"fatigue\",\"min\":0,\"max\":100,\"average\":20},{\"id\":\"stress\",\"min\":0,\"max\":100,\"average\":20},{\"id\":\"luck\",\"min\":-5,\"max\":5,\"average\":1}]}" );

        var catalog = ContentCatalog.Load(directory.RootPath);
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 10, new Random(7));

        Assert.Equal(3, catalog.AgentAttributes.Count);
        Assert.Equal(2, catalog.AgentAttributes.GetIndex("luck"));
        Assert.All(store.Query<AgentAttributes>().Entities,
            entity => Assert.Equal(3, entity.GetComponent<AgentAttributes>().Values.Length));
    }

    [Fact]
    public void PopulationMeansApproximateConfiguredAverages()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        const int count = 10_000;
        new AgentSpawner(catalog).Spawn(store, count, new Random(991));
        var totals = new double[catalog.AgentAttributes.Count];

        foreach (var entity in store.Query<AgentAttributes>().Entities)
        {
            var values = entity.GetComponent<AgentAttributes>().Values;
            for (var index = 0; index < values.Length; index++)
            {
                totals[index] += values[index];
            }
        }

        for (var index = 0; index < totals.Length; index++)
        {
            var definition = catalog.AgentAttributes.Definitions[index];
            var tolerance = Math.Max((definition.Max - definition.Min) * 0.03f, 0.03f);
            Assert.InRange((float)(totals[index] / count), definition.Average - tolerance, definition.Average + tolerance);
        }
    }

    [Fact]
    public void EqualBoundsAlwaysGenerateTheConfiguredValue()
    {
        using var directory = TestContent.CreateDirectory();
        TestContent.CopyCatalogFiles(directory.RootPath);
        File.WriteAllText(System.IO.Path.Combine(directory.RootPath, "agent-schema.json"),
            "{\"attributes\":[{\"id\":\"fixed\",\"min\":7,\"max\":7,\"average\":7},{\"id\":\"fatigue\",\"min\":0,\"max\":100,\"average\":20},{\"id\":\"stress\",\"min\":0,\"max\":100,\"average\":20}]}" );

        var catalog = ContentCatalog.Load(directory.RootPath);
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 100, new Random(3));

        Assert.All(store.Query<AgentAttributes>().Entities,
            entity => Assert.Equal(7f, entity.GetComponent<AgentAttributes>().Values[0]));
    }

    [Fact]
    public void TraitPrevalenceIsReflectedInGeneratedMasks()
    {
        var catalog = LoadCatalog();
        var store = new EntityStore();
        const int count = 10_000;
        new AgentSpawner(catalog).Spawn(store, count, new Random(456));

        foreach (var trait in catalog.Traits)
        {
            var present = store.Query<Psychology>().Entities.Count(entity =>
                (entity.GetComponent<Psychology>().TraitMask & trait.Bit) != 0);
            var observed = (double)present / count;
            Assert.InRange(observed, trait.Prevalence - 0.03, trait.Prevalence + 0.03);
        }
    }

    [Fact]
    public void TraitPrevalenceZeroAndOneAreDeterministic()
    {
        using var directory = TestContent.CreateDirectory();
        TestContent.CopyCatalogFiles(directory.RootPath);
        File.WriteAllText(System.IO.Path.Combine(directory.RootPath, "traits.json"),
            "[{\"id\":\"never\",\"name\":\"Never\",\"bit\":1,\"prevalence\":0},{\"id\":\"always\",\"name\":\"Always\",\"bit\":2,\"prevalence\":1}]" );

        var catalog = ContentCatalog.Load(directory.RootPath);
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 100, new Random(19));

        Assert.All(store.Query<Psychology>().Entities, entity =>
        {
            var mask = entity.GetComponent<Psychology>().TraitMask;
            Assert.Equal(0, mask & 1);
            Assert.Equal(2, mask & 2);
        });
    }

    [Fact]
    public void SeededRandomnessProducesTheSameFixture()
    {
        var catalog = LoadCatalog();
        var firstStore = new EntityStore();
        var secondStore = new EntityStore();
        new AgentSpawner(catalog).Spawn(firstStore, 1, new Random(42));
        new AgentSpawner(catalog).Spawn(secondStore, 1, new Random(42));

        var first = firstStore.Query<Identity>().Entities.First();
        var second = secondStore.Query<Identity>().Entities.First();
        Assert.Equal(first.GetComponent<Identity>(), second.GetComponent<Identity>());
        Assert.Equal(first.GetComponent<PoliticalAlignment>(), second.GetComponent<PoliticalAlignment>());
        Assert.Equal(first.GetComponent<AgentAttributes>().Values, second.GetComponent<AgentAttributes>().Values);
        Assert.Equal(first.GetComponent<Psychology>(), second.GetComponent<Psychology>());
        Assert.Equal(first.GetComponent<AgentState>(), second.GetComponent<AgentState>());
    }

    [Fact]
    public void CatalogRejectsInvalidNumericSchema()
    {
        using var directory = TestContent.CreateDirectory();
        TestContent.CopyCatalogFiles(directory.RootPath);
        File.WriteAllText(System.IO.Path.Combine(directory.RootPath, "agent-schema.json"),
            "{\"attributes\":[{\"id\":\"fatigue\",\"min\":10,\"max\":1,\"average\":5},{\"id\":\"stress\",\"min\":0,\"max\":100,\"average\":20}]}" );

        Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(directory.RootPath));
    }

    [Fact]
    public void CatalogRejectsInvalidTraitPrevalence()
    {
        using var directory = TestContent.CreateDirectory();
        TestContent.CopyCatalogFiles(directory.RootPath);
        File.WriteAllText(System.IO.Path.Combine(directory.RootPath, "traits.json"),
            "[{\"id\":\"greedy\",\"name\":\"Greedy\",\"bit\":1,\"prevalence\":1.5}]" );

        Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(directory.RootPath));
    }

    [Fact]
    public void FatigueStressSystemUpdatesSchemaAttributesForTierOneAgents()
    {
        var catalog = LoadCatalog();
        var values = catalog.AgentAttributes.Definitions.Select(definition => definition.Average).ToArray();
        values[catalog.AgentAttributes.GetIndex("fatigue")] = 10f;
        values[catalog.AgentAttributes.GetIndex("stress")] = 20f;
        var store = new EntityStore();
        var entity = store.CreateEntity(new AgentAttributes { Values = values }, Tags.Get<Tier1LodTag>());
        var root = new SystemRoot(store) { new FatigueStressSystem(catalog.AgentAttributes, 0.5f) };

        root.Update(default);

        var updated = entity.GetComponent<AgentAttributes>().Values;
        Assert.Equal(10.5f, updated[catalog.AgentAttributes.GetIndex("fatigue")]);
        Assert.Equal(20.5f, updated[catalog.AgentAttributes.GetIndex("stress")]);
    }

    [Fact]
    public void FatigueAndStressResetIndependentlyAtTheThreshold()
    {
        var catalog = LoadCatalog();
        var values = catalog.AgentAttributes.Definitions.Select(definition => definition.Average).ToArray();
        values[catalog.AgentAttributes.GetIndex("fatigue")] = 99.95f;
        values[catalog.AgentAttributes.GetIndex("stress")] = 10f;
        var store = new EntityStore();
        var entity = store.CreateEntity(new AgentAttributes { Values = values }, Tags.Get<Tier1LodTag>());
        var root = new SystemRoot(store) { new FatigueStressSystem(catalog.AgentAttributes) };

        root.Update(default);

        var updated = entity.GetComponent<AgentAttributes>().Values;
        Assert.Equal(0f, updated[catalog.AgentAttributes.GetIndex("fatigue")]);
        Assert.Equal(10.1f, updated[catalog.AgentAttributes.GetIndex("stress")]);
    }

    [Fact]
    public void NonTierOneAgentsAreNotUpdated()
    {
        var catalog = LoadCatalog();
        var values = catalog.AgentAttributes.Definitions.Select(definition => definition.Average).ToArray();
        values[catalog.AgentAttributes.GetIndex("fatigue")] = 10f;
        values[catalog.AgentAttributes.GetIndex("stress")] = 20f;
        var store = new EntityStore();
        var entity = store.CreateEntity(new AgentAttributes { Values = values });
        var root = new SystemRoot(store) { new FatigueStressSystem(catalog.AgentAttributes, 1f) };

        root.Update(default);

        var updated = entity.GetComponent<AgentAttributes>().Values;
        Assert.Equal(10f, updated[catalog.AgentAttributes.GetIndex("fatigue")]);
        Assert.Equal(20f, updated[catalog.AgentAttributes.GetIndex("stress")]);
    }

    private sealed class TestContent : IDisposable
    {
        private TestContent(string path) => RootPath = path;

        public string RootPath { get; }

        public static TestContent CreateDirectory() =>
            new(Directory.CreateTempSubdirectory("proxystate-tests-").FullName);

        public static void CopyCatalogFiles(string directory)
        {
            var source = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
            foreach (var fileName in new[] { "actions.json", "factions.json", "traits.json", "agent-schema.json" })
            {
                File.Copy(System.IO.Path.Combine(source, fileName), System.IO.Path.Combine(directory, fileName));
            }
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
