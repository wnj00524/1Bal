using System.Text.Json;
using System.Text.Json.Nodes;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class NumericExpressionTests
{
    [Fact]
    public void FactRegistryResolvesStableBuiltInAndAttributeHandles()
    {
        var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var facts = new FactRegistry(catalog.AgentAttributes);

        Assert.Equal(new FactId(FactKind.TimeMinuteOfDay), facts.Resolve("time.minuteOfDay"));
        Assert.Equal(FactKind.AgentAttribute, facts.Resolve("agent.attribute.fatigue").Kind);
        Assert.Equal(catalog.AgentAttributes.GetIndex("fatigue"), facts.Resolve("agent.attribute.fatigue").Index);
        Assert.Equal(FactKind.TargetAffinity, facts.Resolve("target.affinity").Kind);
    }

    [Fact]
    public void UnknownFactFailsDuringContentLoadWithActionContext()
    {
        using var content = MutableContent.Create();
        content.FirstFact()["fact"] = "agent.attribute.does-not-exist";
        content.SaveActions();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));

        Assert.Contains("actions.json:actions[0].utilityInputs[0].expression", exception.Message);
        Assert.Contains("Unknown numeric fact 'agent.attribute.does-not-exist'", exception.Message);
    }

    [Fact]
    public void LegacySourceSyntaxIsRejected()
    {
        using var content = MutableContent.Create();
        var input = content.FirstInput();
        input.Remove("expression");
        input["source"] = "schedulePressure";
        content.SaveActions();

        Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));
    }

    [Fact]
    public void ExpressionDepthIsBounded()
    {
        using var content = MutableContent.Create();
        JsonObject expression = new() { ["op"] = "constant", ["value"] = 1 };
        for (var index = 0; index <= CompiledNumericExpression.MaximumDepth; index++)
            expression = new JsonObject { ["op"] = "abs", ["input"] = expression };
        content.FirstInput()["expression"] = expression;
        content.SaveActions();

        var exception = Assert.Throws<InvalidDataException>(() => ContentCatalog.Load(content.Directory));
        Assert.Contains("maximum depth", exception.Message);
    }

    private sealed class MutableContent : IDisposable
    {
        private readonly string _actionsPath;
        private readonly JsonArray _actions;

        private MutableContent(string directory, string actionsPath, JsonArray actions)
        {
            Directory = directory;
            _actionsPath = actionsPath;
            _actions = actions;
        }

        public string Directory { get; }

        public static MutableContent Create()
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            var directory = Path.Combine(Path.GetTempPath(), $"proxystate-expressions-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            foreach (var file in System.IO.Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var actionsPath = Path.Combine(directory, "actions.json");
            var actions = JsonNode.Parse(File.ReadAllText(actionsPath))!.AsArray();
            return new MutableContent(directory, actionsPath, actions);
        }

        public JsonObject FirstInput() => _actions[0]!["utilityInputs"]![0]!.AsObject();
        public JsonObject FirstFact() => FindFact(FirstInput()["expression"]!)
            ?? throw new InvalidDataException("Fixture expression has no fact node.");

        private static JsonObject? FindFact(JsonNode node)
        {
            if (node is not JsonObject expression) return null;
            if (string.Equals((string?)expression["op"], "fact", StringComparison.OrdinalIgnoreCase)) return expression;
            foreach (var childName in new[] { "input", "left", "right" })
                if (expression[childName] is { } child && FindFact(child) is { } fact) return fact;
            return null;
        }

        public void SaveActions() => File.WriteAllText(_actionsPath,
            _actions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
