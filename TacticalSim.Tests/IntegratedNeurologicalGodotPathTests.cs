using System.Numerics;
using System.Text.Json;
using TacticalSim.Core;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Damage.Validation;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class IntegratedNeurologicalGodotPathTests
{
    [Fact]
    public void TwelveGaugeSlugHeadImpact_ImmediatelyDrivesAuthoritativeUnconsciousState()
    {
        (IntegratedActorPhysiology target, ProjectileInteractionResult result) = FireHeadImpact(
            seed: 42UL,
            projectileName: "12 Gauge Slug",
            velocity: 480f,
            mass: .0283f,
            area: .00025f,
            drag: .5f);

        Lesion brain = Assert.Single(target.LesionRepository.Lesions,
            lesion => lesion.StructureId == "organ.brain");
        Assert.Equal(LesionKind.BrainOrSpinalInjury, brain.Kind);
        Assert.True(brain.Severity >= new NeurologicalModelParameters().UnconsciousSeverity);
        Assert.Equal(DamageModelVersion.IntegratedV3, result.WoundTrack.ModelVersion);
        Assert.Equal(CasualtyState.Unconscious, target.MedicalState.CasualtyState);
        Assert.Equal(0f, target.ConsciousnessLevel);
        Assert.All(target.MedicalState.Capability.Capacity.Values, value => Assert.Equal(0f, value));

        target.TickPhysiology(5f); // Godot's normal post-shot scenario advance.
        Assert.Equal(CasualtyState.Unconscious, target.MedicalState.CasualtyState);
        Assert.False(target.IsDead);

        MedicalReport report = MedicalAssessor.AssessTrauma(target);
        Assert.Equal(CasualtyState.Unconscious, report.AuthoritativeCasualtyState);
        Assert.Contains("AUTHORITATIVE CASUALTY STATE: UNCONSCIOUS", report.AssessmentText);
        Assert.Contains("Already unconscious", report.AssessmentText);
        Assert.DoesNotContain("31.0 minutes", report.AssessmentText);
    }

    [Fact]
    public void LowerSeverityBrainLesion_ImpairsCognitionWithoutForcingUnconsciousness()
    {
        IntegratedActorPhysiology target = CreateTarget(7UL);
        target.ApplyImpact("low-energy-brain", [BrainLesion("low-energy-brain", .10f)]);

        Assert.Equal(CasualtyState.Effective, target.MedicalState.CasualtyState);
        Assert.Equal(.75f, target.MedicalState.Neurological.CognitiveCapacity, 3);
        Assert.True(target.ConsciousnessLevel > 0f);
        Assert.True(target.MedicalState.Capability[TacticalCapability.Firing] < 1f);
    }

    [Fact]
    public void NonNeurologicalLesion_DoesNotChangeDirectNeurologicalCasualtyState()
    {
        IntegratedActorPhysiology target = CreateTarget(8UL);
        var lesion = new TissueLesion(
            "lesion/soft-tissue/0000",
            "boundary.skin-torso",
            "soft-tissue",
            LesionKind.OpenSoftTissueWound,
            .9f,
            new(Vector3.Zero, Vector3.UnitZ, Distance.FromMeters(.02f), Distance.FromMeters(.003f)),
            LesionTreatmentState.Untreated,
            DateTimeOffset.UnixEpoch);

        target.ApplyImpact("soft-tissue", [lesion]);

        Assert.Equal(CasualtyState.Effective, target.MedicalState.Neurological.DirectCasualtyState);
        Assert.Equal(1f, target.MedicalState.Neurological.CognitiveCapacity);
        Assert.Equal(1f, target.MedicalState.Neurological.BrainstemFunction);
    }

    [Theory]
    [InlineData(.149f, CasualtyState.Effective)]
    [InlineData(.15f, CasualtyState.Incapacitated)]
    [InlineData(.299f, CasualtyState.Incapacitated)]
    [InlineData(.30f, CasualtyState.Unconscious)]
    [InlineData(.849f, CasualtyState.Unconscious)]
    [InlineData(.85f, CasualtyState.Dead)]
    public void BrainLesionSeverity_UsesDocumentedDeterministicBoundaries(
        float severity,
        CasualtyState expected)
    {
        IntegratedActorPhysiology target = CreateTarget(12UL);
        target.ApplyImpact("boundary", [BrainLesion("boundary", severity)]);
        Assert.Equal(expected, target.MedicalState.CasualtyState);
    }

    [Fact]
    public void DestroyedBrainVoxelsWithoutLesions_AreNotIntegratedMedicalAuthority()
    {
        IntegratedActorPhysiology target = CreateTarget(13UL);
        foreach (PhysiologicalVoxel voxel in EnumerateVoxels(target.RootBodyPart)
                     .Where(x => x.Organ == OrganType.Brain))
        {
            voxel.ApplyKineticEnergy(1_000f, voxel.Center, .001f);
        }

        target.TickPhysiology(5f);

        Assert.Empty(target.LesionRepository.Lesions);
        Assert.Equal(CasualtyState.Effective, target.MedicalState.CasualtyState);
        Assert.Equal(1f, target.MedicalState.Neurological.CognitiveCapacity);
    }

    [Fact]
    public void TwelveGaugeSlugHeadImpact_ReplaysExactlyAndBlocksMovementActions()
    {
        (IntegratedActorPhysiology first, ProjectileInteractionResult firstResult) = FireHeadImpact(
            99UL, "12 Gauge Slug", 480f, .0283f, .00025f, .5f);
        (IntegratedActorPhysiology replay, ProjectileInteractionResult replayResult) = FireHeadImpact(
            99UL, "12 Gauge Slug", 480f, .0283f, .00025f, .5f);

        Assert.Equal(
            JsonSerializer.Serialize(firstResult.DebugTrace, DamageModelJson.CreateOptions()),
            JsonSerializer.Serialize(replayResult.DebugTrace, DamageModelJson.CreateOptions()));
        Assert.Equal(
            JsonSerializer.Serialize(first.MedicalState.CaptureSnapshot(), DamageModelJson.CreateOptions()),
            JsonSerializer.Serialize(replay.MedicalState.CaptureSnapshot(), DamageModelJson.CreateOptions()));

        var entity = new TacticalEntity(Guid.NewGuid(), Vector3.Zero, first);
        Assert.Throws<InvalidOperationException>(() =>
            new MoveTacticalAction(entity, Vector3.Zero, Vector3.UnitX, 1f));
    }

    [Fact]
    public void IntegratedVersion_RejectsLegacyActorMutationBoundary()
    {
        var service = CreateService(1UL);
        IActorPhysiology legacy = AnatomicalDummyBuilder.BuildDummy();

        Assert.Throws<InvalidOperationException>(() => service.Resolve(new ProjectileInteractionRequest(
            "wrong-actor",
            "12 Gauge Slug",
            legacy,
            new ProjectileState { Position = new(0f, .76f, -.5f), Velocity = Vector3.UnitZ * 480f },
            Profile(.0283f, .00025f, .5f),
            Distance.FromMeters(4f),
            DamageModelVersion.IntegratedV3)));
    }

    [Fact]
    public void NeurologicalOutcomeParameters_HaveCompleteProvenance()
    {
        ParameterProvenanceRegistry registry = IntegratedNeurologicalParameterProvenance.CreateRegistry();
        registry.ValidateCoverage(IntegratedNeurologicalParameterProvenance.RequiredParameterIds);
        Assert.Equal(IntegratedNeurologicalParameterProvenance.RequiredParameterIds.Count, registry.Entries.Count);
    }

    private static (IntegratedActorPhysiology Target, ProjectileInteractionResult Result) FireHeadImpact(
        ulong seed,
        string projectileName,
        float velocity,
        float mass,
        float area,
        float drag)
    {
        IntegratedActorPhysiology target = CreateTarget(seed);
        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService(seed).Resolve(
            new ProjectileInteractionRequest(
                "head-impact-0001",
                projectileName,
                target,
                new ProjectileState
                {
                    Position = new Vector3(0f, .76f, -.5f),
                    Velocity = Vector3.UnitZ * velocity,
                    Time = 0f
                },
                Profile(mass, area, drag),
                Distance.FromMeters(4f),
                DamageModelVersion.IntegratedV3)));
        return (target, result);
    }

    private static IntegratedActorPhysiology CreateTarget(ulong seed)
    {
        var random = new DeterministicRandomStreamProvider(new FixedRootSeedProvider(seed));
        return AnatomicalDummyBuilder.BuildIntegratedDummy(
            "dummy-0001",
            random,
            uncertainty: new() { Enabled = false });
    }

    private static ProjectileInteractionService CreateService(ulong seed) => new(
        new DamageModelOptions(DamageModelVersion.IntegratedV3),
        new DeterministicRandomStreamProvider(new FixedRootSeedProvider(seed)));

    private static BallisticProfile Profile(float mass, float area, float drag) => new()
    {
        Mass = mass,
        CrossSectionalArea = area,
        DragModel = new StandardDragCurve(drag)
    };

    private static Lesion BrainLesion(string impactId, float severity) => new TissueLesion(
        $"lesion/{impactId}/organ.brain",
        "organ.brain",
        impactId,
        LesionKind.BrainOrSpinalInjury,
        severity,
        new(new Vector3(0f, .76f, -.03f), Vector3.UnitZ,
            Distance.FromMeters(.04f), Distance.FromMeters(.003f)),
        LesionTreatmentState.Untreated,
        DateTimeOffset.UnixEpoch);

    private static IEnumerable<PhysiologicalVoxel> EnumerateVoxels(BodyPart part)
    {
        foreach (PhysiologicalVoxel voxel in part.Voxels)
            yield return voxel;
        foreach (BodyPart child in part.Children)
        foreach (PhysiologicalVoxel voxel in EnumerateVoxels(child))
            yield return voxel;
    }
}
