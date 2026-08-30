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
