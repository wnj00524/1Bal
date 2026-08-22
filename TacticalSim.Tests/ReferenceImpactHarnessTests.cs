using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage.Scenarios;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Tests;

public sealed class ReferenceImpactHarnessTests
{
    [Fact]
    public void Run_UsesAuthoritativeServiceAndEmitsPersistentM6Lesions()
    {
        var recordingService = new RecordingInteractionService(CreateInteractionService(ambientSeed: 999UL));
        var runner = new ReferenceImpactRunner(recordingService, CreateCatalog());

        ReferenceImpactResult result = runner.Run(new ReferenceImpactRunRequest(
            "synthetic-pass",
            DamageModelVersion.FoundationsV2,
            1234UL));

        ProjectileInteractionRequest request = Assert.Single(recordingService.Requests);
        IDeterministicRandomStreamProvider requestStreams =
            Assert.IsType<DeterministicRandomStreamProvider>(request.RandomStreams);
        DeterministicRandomMetadataSnapshot requestMetadata = requestStreams.CaptureSnapshot();
        Assert.Equal(1234UL, requestMetadata.RootSeed);
        DeterministicRandomStreamMetadata stream = Assert.Single(requestMetadata.Streams);
        Assert.Equal("damage.projectile-interaction", stream.StreamName);
        Assert.Equal(0UL, stream.DrawCount);
        Assert.Equal(requestMetadata.AlgorithmVersion, result.RandomMetadata.AlgorithmVersion);
        Assert.Equal(requestMetadata.RootSeed, result.RandomMetadata.RootSeed);
        Assert.Equal(requestMetadata.Streams.ToArray(), result.RandomMetadata.Streams.ToArray());
        Assert.Equal("reference-impact-result-v1", result.OutputSchemaVersion);
        Assert.Equal("m5-foundations-v2", result.ModelIdentifier);
        Assert.Equal("reference-impact-v1/synthetic-pass/seed-1234", result.ComparisonKey);
        Assert.Same(result.WoundTrack.EnergyLedger, result.EnergyLedger);
        Assert.NotEmpty(result.WoundTrack.Segments);
        Assert.True(result.FinalProjectileState.Elapsed.Seconds > 0f);
        Assert.False(result.Lesions.IsDeferred);
        Assert.Equal("none", result.Lesions.DeferredTo);
        Assert.NotEmpty(result.Lesions.Items);
        Assert.Collection(
            result.PhysiologyTimeline.Take(2),
            point => Assert.Equal("before-impact", point.Phase),
            point => Assert.Equal("after-impact", point.Phase));
        Assert.Equal(4, result.PhysiologyTimeline.Count);
        Assert.Equal(4, result.CapabilityTimeline.Count);
        Assert.Matches("^[0-9a-f]{64}$", result.DeterministicHash);
    }

    [Fact]
    public void SameScenarioModelAndSeed_ProduceIdenticalHashesAndJson()
    {
        var runner = new ReferenceImpactRunner(CreateInteractionService(), CreateCatalog());
        var request = new ReferenceImpactRunRequest(
            "synthetic-pass",
            DamageModelVersion.FoundationsV2,
            77UL);

        ReferenceImpactResult first = runner.Run(request);
        ReferenceImpactResult replay = runner.Run(request);

        Assert.Equal(first.DeterministicHash, replay.DeterministicHash);
        Assert.Equal(
            ReferenceImpactFormatter.ToJson(first, writeIndented: false),
            ReferenceImpactFormatter.ToJson(replay, writeIndented: false));
    }

    [Fact]
    public void Compare_RunsEachModelAgainstAFreshTarget()
    {
        int targetsCreated = 0;
        ReferenceImpactScenario scenario = CreateScenario(() =>
        {
            targetsCreated++;
            return BuildTarget();
        });
        var catalog = new ReferenceImpactScenarioCatalog([scenario]);
        var recordingService = new RecordingInteractionService(CreateInteractionService());
        var runner = new ReferenceImpactRunner(recordingService, catalog);

        ReferenceImpactComparisonResult comparison = runner.Compare(
            new ReferenceImpactComparisonRequest(
                "synthetic-pass",
                DamageModelVersion.LegacyV1,
                DamageModelVersion.FoundationsV2,
                55UL));

        Assert.Equal(2, targetsCreated);
        Assert.Equal(2, recordingService.Requests.Count);
        Assert.NotSame(
            recordingService.Requests[0].TargetPhysiology,
            recordingService.Requests[1].TargetPhysiology);
        Assert.Equal(comparison.ComparisonKey, comparison.Baseline.ComparisonKey);
        Assert.Equal(comparison.ComparisonKey, comparison.Candidate.ComparisonKey);
        Assert.Equal(DamageModelVersion.LegacyV1, comparison.Baseline.ModelVersion);
        Assert.Equal(DamageModelVersion.FoundationsV2, comparison.Candidate.ModelVersion);
        Assert.Matches("^[0-9a-f]{64}$", comparison.DeterministicHash);
    }

    [Fact]
    public void DifferentSeed_ChangesRecordedMetadataAndHashWithoutChangingWoundTrack()
    {
        var runner = new ReferenceImpactRunner(CreateInteractionService(), CreateCatalog());

        ReferenceImpactResult first = runner.Run(new ReferenceImpactRunRequest(
            "synthetic-pass",
            DamageModelVersion.FoundationsV2,
            1UL));
        ReferenceImpactResult second = runner.Run(new ReferenceImpactRunRequest(
            "synthetic-pass",
            DamageModelVersion.FoundationsV2,
            2UL));

        Assert.Equal(1UL, first.RandomMetadata.RootSeed);
        Assert.Equal(2UL, second.RandomMetadata.RootSeed);
        Assert.NotEqual(first.DeterministicHash, second.DeterministicHash);
        Assert.Equal(first.WoundTrack.Disposition, second.WoundTrack.Disposition);
        Assert.Equal(
            JsonSerializer.Serialize(first.WoundTrack.Segments, DamageModelJson.CreateOptions()),
            JsonSerializer.Serialize(second.WoundTrack.Segments, DamageModelJson.CreateOptions()));
        Assert.Equal(
            JsonSerializer.Serialize(first.EnergyLedger, DamageModelJson.CreateOptions()),
            JsonSerializer.Serialize(second.EnergyLedger, DamageModelJson.CreateOptions()));
    }

    [Fact]
    public void VersionedScenarioAndProjectileInputs_RoundTripWithoutRuntimeDragObjects()
    {
        ReferenceImpactScenarioInput input = CreateScenario(BuildTarget).Input;
        JsonSerializerOptions options = DamageModelJson.CreateOptions();

        string json = JsonSerializer.Serialize(input, options);
        ReferenceImpactScenarioInput restored = JsonSerializer.Deserialize<ReferenceImpactScenarioInput>(json, options)!;

        Assert.Equal(input, restored);
        Assert.Contains("\"schemaVersion\":\"reference-impact-scenario-input-v1\"", json);
        Assert.Contains("\"dragModelId\":\"standard-drag-curve-v1\"", json);
        Assert.Contains("\"kilograms\"", json);
        Assert.Contains("\"squareMeters\"", json);
        Assert.DoesNotContain("dragModel\":", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DamageModelJson_RoundTripsEveryTypedHarnessQuantityWithCanonicalNames()
    {
        var input = new UnitEnvelope(
            Mass.FromKilograms(0.004f),
            Area.FromSquareMeters(0.000024f),
            SimulationTime.FromSeconds(0.1f),
            Volume.FromCubicMeters(0.005f),
            FlowRate.FromCubicMetersPerSecond(0.000001f),
            Density.FromKilogramsPerCubicMeter(1060f),
            Pressure.FromPascals(12_345f));
        JsonSerializerOptions options = DamageModelJson.CreateOptions();

        string json = JsonSerializer.Serialize(input, options);
        UnitEnvelope restored = JsonSerializer.Deserialize<UnitEnvelope>(json, options)!;

        Assert.Equal(input, restored);
        Assert.Contains("\"kilograms\"", json);
        Assert.Contains("\"squareMeters\"", json);
        Assert.Contains("\"seconds\"", json);
        Assert.Contains("\"cubicMeters\"", json);
        Assert.Contains("\"cubicMetersPerSecond\"", json);
        Assert.Contains("\"kilogramsPerCubicMeter\"", json);
        Assert.Contains("\"pascals\"", json);
    }

    [Fact]
    public void JsonAndTextFormattersExposeTraceIdentityAndHash()
    {
        ReferenceImpactResult result = new ReferenceImpactRunner(
            CreateInteractionService(),
            CreateCatalog()).Run(new ReferenceImpactRunRequest(
                "synthetic-pass",
                DamageModelVersion.FoundationsV2,
                42UL));

        string json = ReferenceImpactFormatter.ToJson(result, writeIndented: false);
        string text = ReferenceImpactFormatter.ToText(result);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(result.DeterministicHash, document.RootElement.GetProperty("deterministicHash").GetString());
        Assert.Equal(42UL, document.RootElement.GetProperty("randomMetadata").GetProperty("rootSeed").GetUInt64());
        Assert.Contains(result.ComparisonKey, text);
        Assert.Contains(result.DeterministicHash, text);
        Assert.Contains("available", text);
    }

    [Fact]
    public void BuiltInCatalogAndDependencyInjectionExposeRunnableHarness()
    {
        var services = new ServiceCollection();
        services.AddTacticalSimCore();
        using ServiceProvider provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IReferenceImpactScenarioCatalog>();
        var runner = provider.GetRequiredService<IReferenceImpactRunner>();

        Assert.Equal(["rifle-arm", "rifle-leg"], catalog.List().Select(input => input.ScenarioId).ToArray());
        Assert.IsType<ReferenceImpactRunner>(runner);
    }

    [Fact]
    public void InvalidScenarioAndSameModelComparison_FailClearly()
    {
        var runner = new ReferenceImpactRunner(CreateInteractionService(), CreateCatalog());

        Assert.Throws<KeyNotFoundException>(() => runner.Run(new ReferenceImpactRunRequest(
            "missing",
            DamageModelVersion.FoundationsV2,
            0UL)));
        Assert.Throws<ArgumentException>(() => runner.Compare(new ReferenceImpactComparisonRequest(
            "synthetic-pass",
            DamageModelVersion.FoundationsV2,
            DamageModelVersion.FoundationsV2,
            0UL)));
    }

    private static ReferenceImpactScenarioCatalog CreateCatalog() => new([CreateScenario(BuildTarget)]);

    private static ReferenceImpactScenario CreateScenario(Func<IActorPhysiology> targetFactory)
    {
        var projectile = new ReferenceProjectileInput(
            ReferenceProjectileInput.CurrentSchemaVersion,
            "synthetic-projectile-v1",
            "Synthetic projectile",
            "standard-drag-curve-v1",
            Mass.FromKilograms(0.01f),
            Area.FromSquareMeters(0.00001f),
            100f,
            0.1f);
        var input = new ReferenceImpactScenarioInput(
            ReferenceImpactScenarioInput.CurrentSchemaVersion,
            "synthetic-pass",
            "Synthetic passage",
            "One-voxel deterministic service integration fixture.",
            "synthetic-one-voxel-v1",
            projectile,
            new Vector3(0f, 0f, -0.02f),
            Vector3.UnitZ,
            Distance.FromMeters(0.1f),
            SimulationTime.FromSeconds(0.2f),
            SimulationTime.FromSeconds(0.1f));
        return new ReferenceImpactScenario(input, targetFactory);
    }

    private static TacticalActorPhysiology BuildTarget()
    {
        var root = new BodyPart { Type = BodyPartType.Thorax };
        root.Voxels.Add(new PhysiologicalVoxel(
            Vector3.Zero,
            0.01f,
            TissueRegistry.Muscle,
            OrganType.Muscle));
        var physiology = new TacticalActorPhysiology();
        physiology.SetRoot(root);
        return physiology;
    }

    private static ProjectileInteractionService CreateInteractionService(ulong ambientSeed = 0UL) => new(
        new DamageModelOptions(),
        new DeterministicRandomStreamProvider(new FixedRootSeedProvider(ambientSeed)));

    private sealed class RecordingInteractionService : IProjectileInteractionService
    {
        private readonly IProjectileInteractionService _inner;

        internal RecordingInteractionService(IProjectileInteractionService inner)
        {
            _inner = inner;
        }

        internal List<ProjectileInteractionRequest> Requests { get; } = [];

        public ProjectileInteractionResult? Resolve(ProjectileInteractionRequest request)
        {
            Requests.Add(request);
            return _inner.Resolve(request);
        }
    }

    private sealed record UnitEnvelope(
        Mass Mass,
        Area Area,
        SimulationTime Time,
        Volume Volume,
        FlowRate Flow,
        Density Density,
        Pressure Pressure);
}
