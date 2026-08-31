using System.Text.Json;
using System.Text.Json.Nodes;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class Milestone15Tests
{
    [Fact]
    public void HeadlessValidationReportsSuccessForRepositoryContent()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ContentValidation.Run(
            new[] { ContentValidation.Command, Path.Combine(AppContext.BaseDirectory, "data") }, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Validated 4 intents", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ValidationErrorNamesFileIntentAndPath()
    {
        using var content = MutableContent.Create();
        content.Actions[0]!["execution"]!["executor"] = "unknown";
        content.Save();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ContentValidation.Run(new[] { ContentValidation.Command, content.Directory }, output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("actions.json", error.ToString());
        Assert.Contains("intent 'work'", error.ToString());
        Assert.Contains("actions[0].execution.executor", error.ToString());
    }

    [Fact]
    public void DataOnlyEatIntentLoadsSelectsExecutesAndAppliesEffects()
    {
        using var content = MutableContent.Create();
        var eat = JsonNode.Parse("""
        {
          "id":"eat", "name":"Eat", "hash":1099,
          "activity":{"id":"eat","name":"Eat","hash":4099},
          "baseUtility":1000,
          "eligibility":{"op":"constant","value":true},
          "utilityInputs":[], "traitModifiers":[],
          "controls":{"minimumCommitmentMinutes":0,"switchingThreshold":0,"cooldownMinutes":0,"urgentPreemptionThreshold":900,"cooldownOnExit":false},
          "effects":[{"attribute":"stress","perMinute":-1}],
          "target":{"kind":"location","value":"agent.location.home"},
          "execution":{"executor":"performAtLocation","destination":"intent.target"}
        }
        """)!;
        content.Actions.Insert(content.Actions.Count - 1, eat);
        content.Save();
        var catalog = ContentCatalog.Load(content.Directory);
        var store = new EntityStore();
        new AgentSpawner(catalog).Spawn(store, 1, new Random(17));
        var agent = store.Query<Identity>().Entities.Single();
        var stressIndex = catalog.AgentAttributes.GetIndex("stress");
        var before = agent.GetComponent<AgentAttributes>().Values[stressIndex];
        var clock = new WorldClockSystem(store);
        var root = new SystemRoot(store)
        {
            clock,
            new AgentDecisionSystem(store, catalog, clock.ClockEntity),
            new IntentExecutionSystem(store, catalog, clock.ClockEntity),
            new ActivityEffectsSystem(catalog, clock.ClockEntity)
        };

        clock.Advance(60); // A positive simulation delta drives generic effects.
        root.Update(default);

        Assert.Equal(1099, agent.GetComponent<IntentionState>().ActionHash);
        Assert.Equal(4099, agent.GetComponent<ActivityState>().ActivityTypeHash);
        Assert.Equal(ActivityPhase.Performing, agent.GetComponent<ActivityState>().Phase);
        Assert.True(agent.GetComponent<AgentAttributes>().Values[stressIndex] < before);
    }

    private sealed class MutableContent : IDisposable
    {
        private readonly string _actionsPath;
        private MutableContent(string directory, string path, JsonArray actions)
        { Directory = directory; _actionsPath = path; Actions = actions; }
        public string Directory { get; }
        public JsonArray Actions { get; }

        public static MutableContent Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"proxystate-m15-{Guid.NewGuid():N}");
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
