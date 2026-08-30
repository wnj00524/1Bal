using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class DecisionBaselineTests
{
    private const int Work = 1001;
    private const int Rest = 1002;
    private const int Socialize = 1003;

    [Theory]
    [InlineData(600, Work)]
    [InlineData(1_020, Rest)]
    [InlineData(6 * 1_440 + 600, Rest)]
    public void WorkEligibilityIsLockedToTheConfiguredSchedule(int minute, int expected)
    {
        using var fixture = new DecisionFixture(minute);
        fixture.Set(fatigue: 0, stress: 0, wealth: 0);

        Assert.Equal(expected, fixture.Decide().IntentHash);
    }

    [Fact]
    public void HighFatigueMakesRestWin()
    {
        using var fixture = new DecisionFixture(600, Work, selectedAtMinute: 500);
        fixture.Set(fatigue: 100, stress: 100, wealth: 10_000);

        var trace = fixture.Decide();

        Assert.Equal(Rest, trace.IntentHash);
        Assert.Equal(95f, trace.Utility, precision: 3);
        Assert.Equal(3001, trace.TargetLocationId);
    }

    [Fact]
    public void SocializeRequiresAndTargetsAnAvailableColocatedPeer()
    {
        using var withoutPeer = new DecisionFixture(1_020);
        withoutPeer.Set(preference: 1, charisma: 100, fatigue: 0, stress: 0);
        Assert.Equal(Rest, withoutPeer.Decide().IntentHash);

        using var withPeer = new DecisionFixture(1_020);
        withPeer.Set(preference: 1, charisma: 100, fatigue: 0, stress: 0);
        var peer = withPeer.AddPeer(affinity: 100);
        var trace = withPeer.Decide();

        Assert.Equal(Socialize, trace.IntentHash);
        Assert.Equal(peer.Id, trace.TargetEntityId);
        Assert.Equal(3001, trace.TargetLocationId);
        Assert.Equal(79f, trace.Utility, precision: 3);
    }

    [Fact]
    public void SocialTargetRequirementsRankingAndTieBreakAreDataDefined()
    {
        using var fixture = new DecisionFixture(1_020);
        fixture.Set(preference: 1, charisma: 100, fatigue: 0, stress: 0);
        _ = fixture.AddPeer(affinity: 100, location: 3004); // excluded by the JSON requirement
        var first = fixture.AddPeer(affinity: 50);
        var second = fixture.AddPeer(affinity: 50);

        var trace = fixture.Decide();

        Assert.Equal(Socialize, trace.IntentHash);
        Assert.Equal(Math.Min(first.Id, second.Id), trace.TargetEntityId);
        Assert.Equal(3001, trace.TargetLocationId);
    }

    [Fact]
    public void MinimumCommitmentBlocksANonUrgentWinner()
    {
        using var fixture = new DecisionFixture(600, Work, selectedAtMinute: 595);
        fixture.Set(fatigue: 70, stress: 90, wealth: 4_000);

        Assert.Equal(Work, fixture.Decide().IntentHash);
    }

    [Fact]
    public void SwitchingThresholdBlocksSmallImprovementsThenAllowsLargerOnes()
    {
        using var fixture = new DecisionFixture(600, Work, selectedAtMinute: 500);
        fixture.Set(fatigue: 60, stress: 90, wealth: 4_000);
        Assert.Equal(Work, fixture.Decide().IntentHash);

        fixture.AdvanceTo(601);
        fixture.Set(fatigue: 70, stress: 90, wealth: 4_000);
        Assert.Equal(Rest, fixture.Decide().IntentHash);
    }

    [Fact]
    public void UrgentWinnerPreemptsMinimumCommitment()
    {
        using var fixture = new DecisionFixture(600, Work, selectedAtMinute: 599);
        fixture.Set(fatigue: 100, stress: 100, wealth: 10_000);

        Assert.Equal(Rest, fixture.Decide().IntentHash);
    }

    [Fact]
    public void ExitedIntentRemainsUnavailableUntilItsCooldownExpires()
    {
        using var fixture = new DecisionFixture(600, Work, selectedAtMinute: 500);
        fixture.Set(fatigue: 100, stress: 100, wealth: 10_000);
        var exited = fixture.Decide();
        Assert.Equal(615, exited.Cooldowns[Work]);

        fixture.AdvanceTo(601);
        fixture.Set(fatigue: 0, stress: 0, wealth: 0);
        Assert.Equal(Rest, fixture.Decide().IntentHash);

        fixture.AdvanceTo(620);
        Assert.Equal(Work, fixture.Decide().IntentHash);
    }

    [Fact]
    public void DecisionAndCommutingTraceLocksTravelAndActivityTransitions()
    {
        using var fixture = new DecisionFixture(600);
        fixture.Set(fatigue: 0, stress: 0, wealth: 0);

        var selected = fixture.Decide(runCommuting: true);

        Assert.Equal(Work, selected.IntentHash);
        Assert.Equal(3004, selected.TargetLocationId);
        Assert.Equal(AgentTravelMode.TravellingToWork, selected.TravelMode);
        Assert.Equal(ActivityKind.Commuting, selected.ActivityKind);
        Assert.Equal(Work, selected.ActivityActionHash);
    }

    [Fact]
    public void EligibilityNoLongerUsesNamedRuntimeGates()
    {
        var repository = FindRepositoryRoot();
        var runtimeFiles = Directory.GetFiles(Path.Combine(repository, "Simulation"), "*.cs");
        var source = string.Join('\n', runtimeFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("Eligibility.Gate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("workSchedule", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("homeReachable", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("availablePeer", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Action.Id.Equals(\"socialize\"", source);
        Assert.DoesNotContain("Action.Id.Equals(\"work\"", source);
        Assert.Contains("TargetResolver", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProxyState.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate ProxyState.sln.");
    }

    private sealed class DecisionFixture : IDisposable
    {
        private readonly ContentCatalog _catalog;
        private readonly EntityStore _store = new();
        private readonly Entity _clock;
        private readonly AgentDecisionSystem _decisions;
        private readonly CommutingSystem _commuting;
        private readonly SystemRoot _decisionRoot;
        private readonly SystemRoot _commutingRoot;

        public DecisionFixture(long minute, int currentAction = Rest, long selectedAtMinute = 0)
        {
            _catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
            _clock = _store.CreateEntity(new WorldTime());
            var values = _catalog.AgentAttributes.Definitions.Select(definition => definition.Average).ToArray();
            Agent = _store.CreateEntity(
                new Identity { NameId = 1, OccupationId = 2001 },
                new AgentAttributes { Values = values },
                new Psychology(),
                new AgentLocation { HomeLocationId = 3001, WorkLocationId = 3004, CurrentLocationId = 3001 },
                new AgentTravel { RouteLocationIds = new[] { 3001, 3003, 3004 }, Mode = AgentTravelMode.AtHome },
                new IntentionState { ActionHash = currentAction, SelectedAtMinute = selectedAtMinute },
                new ActivityState { CurrentActionHash = currentAction, Kind = ActivityKind.Resting },
                new DecisionState { Dirty = true, CooldownActionHashes = new int[3], CooldownUntilMinutes = new long[3] },
                Tags.Get<Tier1LodTag>());
            _decisions = new AgentDecisionSystem(_store, _catalog, _clock);
            _commuting = new CommutingSystem(_catalog, _clock);
            _decisionRoot = new SystemRoot(_store) { _decisions };
            _commutingRoot = new SystemRoot(_store) { _commuting };
            AdvanceTo(minute);
        }

        public Entity Agent { get; }

        public void Set(float? fatigue = null, float? stress = null, float? wealth = null,
            float? preference = null, float? charisma = null)
        {
            var values = Agent.GetComponent<AgentAttributes>().Values;
            SetValue("fatigue", fatigue); SetValue("stress", stress); SetValue("wealth", wealth);
            SetValue("preference", preference); SetValue("charisma", charisma);
            Agent.GetComponent<DecisionState>().Dirty = true;
            void SetValue(string id, float? value)
            {
                if (value.HasValue) values[_catalog.AgentAttributes.GetIndex(id)] = value.Value;
            }
        }

        public Entity AddPeer(float affinity, int location = 3001)
        {
            var peer = _store.CreateEntity(new AgentLocation { CurrentLocationId = location });
            _store.CreateEntity(new EdgeData { Source = Agent, Target = peer, Affinity = affinity });
            Agent.GetComponent<DecisionState>().Dirty = true;
            return peer;
        }

        public void AdvanceTo(long minute)
        {
            ref var time = ref _clock.GetComponent<WorldTime>();
            time.ElapsedSimulationSeconds = minute * SimulationDefaults.SimulationSecondsPerMinute;
            time.DeltaSimulationSeconds = SimulationDefaults.SimulationSecondsPerMinute;
            Agent.GetComponent<DecisionState>().Dirty = true;
        }

        public DecisionTrace Decide(bool runCommuting = false)
        {
            _decisionRoot.Update(default);
            if (runCommuting) _commutingRoot.Update(default);
            return DecisionTrace.Capture(Agent);
        }

        public void Dispose()
        {
            // Friflo stores and system roots are managed objects and expose no
            // disposal contract; the fixture exists only to scope test state.
        }
    }
}
