using System.Text.Json;
using System.Text.Json.Nodes;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class PredicateExpressionTests
{
    [Fact]
    public void ExistingEligibilityDefinitionsCompileAtContentLoad()
    {
        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

        Assert.All(catalog.Intents.All, intent => Assert.NotNull(intent.Eligibility));
    }

    [Fact]
    public void NumericFactCannotBeUsedAsBooleanPredicate()
    {
        using var content = MutableContent.Create();
        content.SetEligibility(new JsonObject { ["op"] = "fact", ["fact"] = "time.minuteOfDay" });

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("actions.json:actions[0].eligibility", exception.Message);
        Assert.Contains("numeric and cannot be used as a boolean", exception.Message);
    }

    [Fact]
    public void BooleanFactCannotBeUsedInNumericComparison()
    {
        using var content = MutableContent.Create();
        content.SetEligibility(new JsonObject
        {
            ["op"] = "equal",
            ["left"] = new JsonObject { ["op"] = "fact", ["fact"] = "travel.reachable" },
            ["right"] = new JsonObject { ["op"] = "constant", ["value"] = 1 }
        });

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("boolean and cannot be used in a numeric expression", exception.Message);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void BooleanCombinatorsRequireAtLeastTwoInputs(string op)
    {
        using var content = MutableContent.Create();
        content.SetEligibility(new JsonObject
        {
            ["op"] = op,
            ["inputs"] = new JsonArray(new JsonObject { ["op"] = "constant", ["value"] = true })
        });

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));
        Assert.Contains("requires at least two", exception.Message);
    }

    private sealed class MutableContent : IDisposable
    {
        private readonly string _actionsPath;
        private readonly JsonArray _actions;

        private MutableContent(string directory, string actionsPath, JsonArray actions)
        {
            Directory = directory; _actionsPath = actionsPath; _actions = actions;
        }

        public string Directory { get; }

        public static MutableContent Create()
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            var directory = Path.Combine(Path.GetTempPath(), $"proxystate-predicates-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            foreach (var file in System.IO.Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var actionsPath = Path.Combine(directory, "actions.json");
            return new MutableContent(directory, actionsPath, JsonNode.Parse(File.ReadAllText(actionsPath))!.AsArray());
        }

        public void SetEligibility(JsonObject eligibility)
        {
            _actions[0]!["eligibility"] = eligibility;
            File.WriteAllText(_actionsPath, _actions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
