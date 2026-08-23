using System.Numerics;
using System.Text.Json;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Persistence;
using TacticalSim.Core.Damage.Validation;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class M12HardeningTests
{
    [Fact] public void BaselineReferenceSuite_CoversEveryRequiredCase() =>
        Assert.Equal(Enum.GetValues<ReferenceInjuryKind>().Length, ReferenceInjurySuite.CreateBaseline().Cases.Count);

    [Fact] public void ProvenanceRegistry_RejectsDuplicatesAndReportsCoverage()
    {
        var registry = new ParameterProvenanceRegistry();
        var entry = new ParameterProvenance("bleed.factor", "hemorrhage", "factor", "1 ratio",
            ParameterClassification.Provisional, "M7 design", "1", "damage-model", ["HemorrhageTests"]);
        registry.Register(entry);
        Assert.Equal(entry, registry.GetRequired("bleed.factor"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(entry));
        Assert.Throws<InvalidOperationException>(() => registry.ValidateCoverage(["bleed.factor", "missing"]));
    }

    [Fact] public void ReferenceSuite_EvaluatesBandsAndQualitativeExpectations()
    {
        var suite = new ReferenceInjurySuite([new("arterial", ReferenceInjuryKind.MajorArterial, "arterial injury",
            [new("collapse-seconds", 20, 300, "s")], ["faster-than-venous"])]);
        Assert.True(suite.Evaluate(new("arterial", new Dictionary<string,double>{{"collapse-seconds", 60}}, ["faster-than-venous"])).Accepted);
        var failed = suite.Evaluate(new("arterial", new Dictionary<string,double>{{"collapse-seconds", 500}}, []));
        Assert.False(failed.Accepted); Assert.Equal(2, failed.Deviations.Count);
    }

    [Fact] public void Calibration_IsRepeatableAndRequiresMultipleScenarios()
    {
        var result = CalibrationRunner.Analyze(new("rate", 2, 1, 3, .5), x => x * 10);
        Assert.Equal(1, result.NormalizedSensitivity, 10);
        Assert.Throws<ArgumentException>(() => CalibrationRunner.Compare("bad", new Dictionary<string,double>{{"one", 1}}, new Dictionary<string,double>()));
        var candidate = CalibrationRunner.Compare("candidate", new Dictionary<string,double>{{"a", 1},{"b", 4}}, new Dictionary<string,double>{{"a", 2},{"b", 2}});
        Assert.Equal(1.5, candidate.MeanError); Assert.Contains("calibration-report-v1", CalibrationRunner.Export(new(CalibrationRunner.SchemaVersion, "v2", [result], [candidate])));
    }

    [Fact] public void Save_RoundTripsPolymorphicLesionsWithExplicitVersions()
    {
        var lesion = new TissueLesion("l1", "soft", "impact", LesionKind.OpenSoftTissueWound, .2f,
            new(Vector3.Zero, Vector3.UnitX, Distance.FromMeters(.1f), Distance.FromMeters(.01f)),
            LesionTreatmentState.Untreated, DateTimeOffset.UnixEpoch);
        var persistence = new DamageModelPersistence();
        var save = new DamageModelSave(DamageModelPersistence.CurrentSaveSchema, "m5-foundations-v2", "anatomy-m6-v1", DamageModelPersistence.CurrentLesionSchema, [lesion]);
        var restored = persistence.DeserializeSave(persistence.SerializeSave(save));
        Assert.IsType<TissueLesion>(Assert.Single(restored.Lesions));
        Assert.Throws<NotSupportedException>(() => persistence.DeserializeSave("{\"SchemaVersion\":\"old\"}"));
    }

    [Fact] public void Replay_RecordsSeedsAndOrderedActions()
    {
        var persistence = new DamageModelPersistence(); Guid actor = Guid.NewGuid();
        var replay = new DamageModelReplay(DamageModelPersistence.CurrentReplaySchema, "m5-foundations-v2", "anatomy-m6-v1", 42,
            new Dictionary<string,ulong>{{"physiology", 7}}, [new(0, actor, "move", "{}"), new(1, actor, "shoot", "{}")]);
        var restored = persistence.DeserializeReplay(persistence.SerializeReplay(replay));
        Assert.Equal(42UL, restored.RootSeed); Assert.Equal(2, restored.Actions.Count);
        Assert.Throws<ArgumentException>(() => persistence.SerializeReplay(replay with { Actions = [new(2, actor, "x", ""), new(1, actor, "y", "")] }));
    }

    [Fact] public void BenchmarkRunner_ReportsBudgetAndSerializableResult()
    {
        int count = 0;
        var report = DamageBenchmarkRunner.Run([new("lesion-update", 10, TimeSpan.FromSeconds(1), () => count++)]);
        Assert.True(Assert.Single(report.Measurements).WithinBudget); Assert.Equal(11, count);
        Assert.Equal(DamageBenchmarkRunner.SchemaVersion, JsonDocument.Parse(DamageBenchmarkRunner.Export(report)).RootElement.GetProperty("SchemaVersion").GetString());
    }
}
