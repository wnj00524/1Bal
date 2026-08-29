using System.Text.Json.Nodes;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class AgentNetworkCatalogTests
{
    [Fact]
    public void CatalogResolvesEveryDefinitionByIdAndCachedHash()
    {
        var catalog = LoadCatalog().Networks;

        Assert.Equal(NetworkHierarchyMode.Flat, catalog.GetType("FAMILY").HierarchyMode);
        Assert.Equal(catalog.GetType("family"), catalog.GetType(catalog.GetType("family").Hash));
        Assert.Equal(catalog.GetRole("family-member"), catalog.GetRole(catalog.GetRole("family-member").Hash));
        Assert.Equal(catalog.GetGenerator("families-by-home"),
            catalog.GetGenerator(catalog.GetGenerator("families-by-home").Hash));
        Assert.Equal(NetworkPartitionStrategy.WorkLocation,
            catalog.GetGenerator("companies-by-work").PartitionStrategy);
        Assert.All(catalog.GetType("company").RoleHashes,
            hash => Assert.Equal(catalog.GetType("company").Hash, catalog.GetRole(hash).NetworkTypeHash));
    }

    public static IEnumerable<object[]> MalformedContentCases()
    {
        yield return Case("empty ID", root => root["roles"]![0]!["id"] = " ");
        yield return Case("duplicate ID", root => root["roles"]![1]!["id"] = "family-member");
        yield return Case("duplicate hash", root =>
        {
            // These distinct identifiers are a known FNV-1a 32-bit collision.
            root["roles"]![0]!["id"] = "zqmnn2aweb";
            root["roles"]![1]!["id"] = "1tx8jfjl2l";
        });
        yield return Case("missing role", root => root["networkTypes"]![0]!["roles"]![0] = "absent");
        yield return Case("cross-type role", root => root["networkTypes"]![0]!["roles"]![0] = "company-head");
        yield return Case("incompatible hierarchy", root => root["generators"]![0]!["maximumDepth"] = 1);
        yield return Case("invalid sizes", root => root["generators"]![0]!["minimumSize"] = 0);
        yield return Case("invalid weights", root => root["generators"]![0]!["sizeWeights"]![0]!["weight"] = 0);
        yield return Case("impossible remainder", root =>
        {
            root["generators"]![0]!["minimumSize"] = 1;
            root["generators"]![0]!["remainderHandling"] = "create-undersized";
        });
        yield return Case("invalid cardinality", root => root["networkTypes"]![0]!["maxNetworksPerAgent"] = 0);
        yield return Case("invalid span", root => root["generators"]![1]!["targetSpanOfControl"] = 8);
        yield return Case("invalid depth", root => root["generators"]![1]!["maximumDepth"] = 0);
        yield return Case("unknown partition", root => root["generators"]![0]!["partitionKey"] = "json-query");
        yield return Case("capacity exceeded", root =>
        {
            root["generators"]![1]!["maximumSize"] = 401;
            root["generators"]![1]!["sizeWeights"]![0]!["size"] = 401;
        });
    }

    [Theory]
    [MemberData(nameof(MalformedContentCases))]
    public void CatalogRejectsMalformedNetworkContent(string _, Action<JsonObject> mutate)
    {
        using var content = TemporaryContent.Create(mutate);
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));
    }

    private static object[] Case(string name, Action<JsonObject> mutation) => [name, mutation];

    private static ContentCatalog LoadCatalog() =>
        ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private sealed class TemporaryContent : IDisposable
    {
        private TemporaryContent(string directory) => Directory = directory;
        public string Directory { get; }

        public static TemporaryContent Create(Action<JsonObject> mutate)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            var destination = Path.Combine(Path.GetTempPath(), $"proxystate-networks-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(destination);
            foreach (var file in System.IO.Directory.EnumerateFiles(source, "*.json"))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

            var path = Path.Combine(destination, "networks.json");
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            mutate(root);
            File.WriteAllText(path, root.ToJsonString());
            return new TemporaryContent(destination);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
