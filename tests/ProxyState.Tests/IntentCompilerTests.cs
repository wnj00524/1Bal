using System.Text.Json;
using System.Text.Json.Nodes;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class IntentCompilerTests
{
    [Fact]
    public void CatalogAssignsDenseIndexesWhilePreservingStableHashes()
    {
        var catalog = LoadCatalog();

        Assert.Equal(Enumerable.Range(0, catalog.Intents.Count),
            catalog.Intents.All.Select(intent => (int)intent.RuntimeIndex));
        foreach (var authored in catalog.Actions)
        {
            Assert.True(catalog.Intents.TryGetByHash(authored.Hash, out var compiled));
            Assert.Equal(authored.Hash, compiled!.Hash);
        }
    }

    [Fact]
    public void CatalogCompilesExactlyOneSafeFallback()
    {
        var fallback = LoadCatalog().Intents.Fallback;

        Assert.True(fallback.Fallback);
        Assert.Equal(TargetKind.None, fallback.Target.Kind);
        Assert.Equal(ExecutorKind.Wait, fallback.Executor);
    }

    [Fact]
    public void CompilerDerivesDependenciesWithoutAuthoringDuplication()
    {
        var catalog = LoadCatalog();
        var work = catalog.Intents.All.Single(intent => intent.Id == "work");
        var socialize = catalog.Intents.All.Single(intent => intent.Id == "socialize");
        var fatigue = catalog.AgentAttributes.GetIndex("fatigue");

        Assert.True(work.Dependencies.Intersects(new(FactDependencyCategory.Time)));
        Assert.True(work.Dependencies.Intersects(new(FactDependencyCategory.Attributes, 1UL << fatigue)));
        Assert.True(socialize.Dependencies.Intersects(new(FactDependencyCategory.SocialTargets)));
        Assert.True(socialize.Dependencies.Intersects(new(FactDependencyCategory.TargetAffinity)));
    }

    [Fact]
    public void CatalogBuildsDenseCandidateIndexesAtStartup()
    {
        var intents = LoadCatalog().Intents;
        var work = intents.All.Single(intent => intent.Id == "work");
        var rest = intents.All.Single(intent => intent.Id == "rest");
        var socialize = intents.All.Single(intent => intent.Id == "socialize");

        Assert.Equal(8, intents.Candidates.Global.Count);
        Assert.False(intents.Candidates.Global.Contains(intents.Fallback.RuntimeIndex));
        var noRelations = intents.Candidates.GetCandidates(new(true, true, true, false, false));
        Assert.True(noRelations.Contains(work.RuntimeIndex));
        Assert.True(noRelations.Contains(rest.RuntimeIndex));
        Assert.False(noRelations.Contains(socialize.RuntimeIndex));
        var withNetworks = intents.Candidates.GetCandidates(new(true, true, true, false, true));
        Assert.True(withNetworks.Contains(socialize.RuntimeIndex));
        var noWorkplace = intents.Candidates.GetCandidates(new(true, true, false, true, true));
        Assert.False(noWorkplace.Contains(work.RuntimeIndex));
    }

    [Fact]
    public void CandidateBitsetsCanBeIntersectedWithoutStringOrHashLookups()
    {
        var candidates = LoadCatalog().Intents.Candidates;
        var intersection = candidates.Global.Intersect(candidates.AvailableWithoutSocialRelations)
            .Intersect(candidates.AvailableWithoutNetworkRelations);

        Assert.Equal(candidates.GetCandidates(new(true, true, true, false, false)).EnumerateSetBits(),
            intersection.EnumerateSetBits());
        Assert.Throws<ArgumentException>(() => intersection.Intersect(
            IntentBitSet.FromIndexes(intersection.Capacity + 1, Array.Empty<int>())));
    }

    [Fact]
    public void MissingFallbackFailsWithAPathAwareMessage()
    {
        using var content = MutableContent.Create();
        content.Actions.Single(action => action!["fallback"]?.GetValue<bool>() == true)!["fallback"] = false;
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("actions.json:actions", exception.Message);
        Assert.Contains("exactly one fallback", exception.Message);
    }

    [Fact]
    public void UnknownTraitReferenceFailsAtItsJsonPath()
    {
        using var content = MutableContent.Create();
        content.Actions[0]!["traitModifiers"]![0]!["trait"] = "missing-trait";
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("actions.json:actions[0].traitModifiers[0].trait", exception.Message);
    }

    [Fact]
    public void UnknownNetworkTypeFailsAtItsJsonPath()
    {
        using var content = MutableContent.Create();
        var socialize = content.Actions.Single(action => action!["id"]!.GetValue<string>() == "socialize")!;
        socialize["target"]!["query"]!["networkType"] = "missing-network";
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("target.query.networkType", exception.Message);
        Assert.Contains("missing-network", exception.Message);
    }

    [Fact]
    public void ParticipantEffectWithoutMutualParticipationFailsAtItsJsonPath()
    {
        using var content = MutableContent.Create();
        var rest = content.Actions.Single(action => action!["id"]!.GetValue<string>() == "rest")!;
        rest["effects"]![0]!["subject"] = "participant";
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("actions.json:actions", exception.Message);
        Assert.Contains("participant effects require mutual participation", exception.Message);
    }

    [Fact]
    public void InvalidMutualDurationFailsAtItsJsonPath()
    {
        using var content = MutableContent.Create();
        var socialize = content.Actions.Single(action => action!["id"]!.GetValue<string>() == "socialize")!;
        socialize["participation"]!["minimumDurationMinutes"] = 0;
        content.Save();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("participation", exception.Message);
        Assert.Contains("invalid duration", exception.Message);
    }

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private sealed class MutableContent : IDisposable
    {
        private readonly string _actionsPath;
        private MutableContent(string directory, string actionsPath, JsonArray actions)
        { Directory = directory; _actionsPath = actionsPath; Actions = actions; }
        public string Directory { get; }
        public JsonArray Actions { get; }

        public static MutableContent Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"proxystate-intents-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            foreach (var source in System.IO.Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
            var path = Path.Combine(directory, "actions.json");
            return new(directory, path, JsonNode.Parse(File.ReadAllText(path))!.AsArray());
        }

        public void Save() => File.WriteAllText(_actionsPath,
            Actions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
