using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public class ProjectileInteractionServiceTests
{
    [Fact]
    public void PassageProducesConservedLedgerAndOrderedWoundTrack()
    {
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(new Vector3(0f, 0f, 0f), 0.01f, TissueRegistry.Muscle, OrganType.Muscle));
        ProjectileInteractionService service = CreateService();

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(target, new Vector3(0f, 0f, -0.02f), new Vector3(0f, 0f, 100f))));

        Assert.Equal(ProjectileDisposition.Exited, result.WoundTrack.Disposition);
        Assert.Single(result.WoundTrack.Segments);
        Assert.True(result.FinalProjectileState.Velocity.Length() > 0f);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
        Assert.Equal(
            result.EnergyLedger.IncomingEnergy.Joules,
            result.EnergyLedger.OutgoingEnergy.Joules
                + result.EnergyLedger.TotalStructureDepositedEnergy.Joules,
            4);
        Assert.Same(result.WoundTrack, result.DebugTrace.WoundTrack);
        Assert.Contains(
            result.DebugTrace.RandomMetadata.Streams,
            stream => stream.StreamName == "damage.projectile-interaction" && stream.DrawCount == 0UL);
    }

    [Fact]
    public void LowEnergyProjectileStopsAtCalculatedPointAndIsRetained()
    {
        var resistantTissue = TissueRegistry.Muscle;
        resistantTissue.Density = 2_000f;
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(Vector3.Zero, 0.02f, resistantTissue, OrganType.Muscle));
        var profile = new BallisticProfile
        {
            Mass = 0.001f,
            CrossSectionalArea = 0.01f,
            DragModel = new StandardDragCurve(10f)
        };

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(
                target,
                new Vector3(0f, 0f, -0.03f),
                new Vector3(0f, 0f, 10f),
                profile)));

        Assert.Equal(ProjectileDisposition.Retained, result.WoundTrack.Disposition);
        Assert.NotNull(result.WoundTrack.RetainedPoint);
        Assert.Null(result.WoundTrack.ExitPoint);
        Assert.Equal(Vector3.Zero, result.FinalProjectileState.Velocity);
        Assert.Equal(0f, result.EnergyLedger.OutgoingEnergy.Joules);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
        Assert.Equal(
            ProjectileStateChangeKind.Retained,
            result.WoundTrack.Segments[^1].ProjectileStateChange.Kind);
        Assert.InRange(result.WoundTrack.Segments[0].PathLength.Meters, 0f, 0.02f);
    }

    [Fact]
    public void UltraLowEnergyProjectilePassesThroughZeroDragTissueWithoutArtificialStop()
    {
        var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle);
        TacticalActorPhysiology target = BuildTarget(voxel);
        var profile = new BallisticProfile
        {
            Mass = 0.001f,
            CrossSectionalArea = 0.00001f,
            DragModel = new StandardDragCurve(0f)
        };
        const float incomingEnergyJoules = 0.00005f;
        float speed = MathF.Sqrt(2f * incomingEnergyJoules / profile.Mass);

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(
                target,
                new Vector3(0f, 0f, -0.02f),
                Vector3.UnitZ * speed,
                profile)));

        Assert.Equal(ProjectileDisposition.Exited, result.WoundTrack.Disposition);
        Assert.True(result.FinalProjectileState.Velocity.Length() > 0f);
        Assert.Equal(result.EnergyLedger.IncomingEnergy, result.EnergyLedger.OutgoingEnergy);
        Assert.Equal(0f, result.EnergyLedger.TotalStructureDepositedEnergy.Joules);
        Assert.Equal(0f, voxel.DepositedEnergy);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
    }

    [Fact]
    public void BoneRicochetConservesEnergyAndRecordsDirectionChange()
    {
        TissueProperties corticalBone = TissueRegistry.Bone;
        corticalBone.ShearStrength = 100f;
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(Vector3.Zero, 0.01f, corticalBone, OrganType.Bone));
        var profile = new BallisticProfile
        {
            Mass = 0.008f,
            CrossSectionalArea = 0.00005f,
            DragModel = new StandardDragCurve(0.3f)
        };

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(
                target,
                new Vector3(-0.02f, 0.003f, 0f),
                Vector3.Normalize(new Vector3(1f, -0.1f, 0f)) * 100f,
                profile)));

        Assert.Equal(ProjectileDisposition.Exited, result.WoundTrack.Disposition);
        Assert.Equal(
            ProjectileStateChangeKind.Ricocheted,
            result.WoundTrack.Segments[0].ProjectileStateChange.Kind);
        Assert.True(result.FinalProjectileState.Velocity.X < 0f);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
        Assert.True(result.EnergyLedger.TotalStructureDepositedEnergy.Joules > 0f);
    }

    [Fact]
    public void ExactThresholdBoneStopIsRetainedRatherThanExitedAtZeroVelocity()
    {
        TissueProperties corticalBone = TissueRegistry.Bone;
        corticalBone.Density = 1_900f;
        corticalBone.ShearStrength = 50f;
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(Vector3.Zero, 0.01f, corticalBone, OrganType.Bone));
        var profile = new BallisticProfile
        {
            // 0.5 * 0.01 kg * (100 m/s)^2 = 50 J, matching the 50 J
            // normal-incidence shatter threshold for this 1 cm bone path.
            Mass = 0.01f,
            CrossSectionalArea = 0.0001f,
            DragModel = new StandardDragCurve(0.3f)
        };

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(
                target,
                new Vector3(0f, 0f, -0.02f),
                Vector3.UnitZ * 100f,
                profile)));

        Assert.Equal(ProjectileDisposition.Retained, result.WoundTrack.Disposition);
        Assert.Equal(ProjectileStateChangeKind.Retained, result.WoundTrack.Segments[0].ProjectileStateChange.Kind);
        Assert.Equal(Vector3.Zero, result.FinalProjectileState.Velocity);
        Assert.Equal(0f, result.EnergyLedger.OutgoingEnergy.Joules);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
    }

    [Fact]
    public void MultiStructureTraversalOrdersSegmentsAndNeverDuplicatesIncomingEnergy()
    {
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(new Vector3(0f, 0f, -0.01f), 0.01f, TissueRegistry.Muscle, OrganType.Muscle),
            new PhysiologicalVoxel(new Vector3(0f, 0f, 0.01f), 0.01f, TissueRegistry.Liver, OrganType.Liver));

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(target, new Vector3(0f, 0f, -0.03f), new Vector3(0f, 0f, 200f))));

        Assert.Equal(2, result.WoundTrack.Segments.Count);
        Assert.Equal(0, result.WoundTrack.Segments[0].Sequence);
        Assert.Equal(1, result.WoundTrack.Segments[1].Sequence);
        Assert.True(result.WoundTrack.Segments[0].EntryPoint.Z < result.WoundTrack.Segments[1].EntryPoint.Z);
        Assert.Equal(
            result.WoundTrack.Segments[0].OutgoingEnergy,
            result.WoundTrack.Segments[1].IncomingEnergy);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
        Assert.True(
            result.EnergyLedger.TotalStructureDepositedEnergy.Joules
            <= result.EnergyLedger.IncomingEnergy.Joules);
    }

    [Fact]
    public void EqualGeometryUsesSemanticStructureIdsIndependentOfChildListOrder()
    {
        TacticalActorPhysiology firstTarget = BuildOverlappingHierarchy(reverseChildren: false);
        TacticalActorPhysiology reorderedTarget = BuildOverlappingHierarchy(reverseChildren: true);
        ProjectileInteractionService service = CreateService();

        ProjectileInteractionResult first = Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(firstTarget, new Vector3(0f, 0f, -0.02f), Vector3.UnitZ * 100f)));
        ProjectileInteractionResult reordered = Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(reorderedTarget, new Vector3(0f, 0f, -0.02f), Vector3.UnitZ * 100f)));
        JsonSerializerOptions json = DamageModelJson.CreateOptions();

        Assert.Equal(
            JsonSerializer.Serialize(first.WoundTrack, json),
            JsonSerializer.Serialize(reordered.WoundTrack, json));
        Assert.Equal(
            first.WoundTrack.Segments.Count,
            first.WoundTrack.Segments.Select(segment => segment.StructureId).Distinct().Count());
        Assert.All(first.WoundTrack.Segments, segment => Assert.DoesNotContain("child-", segment.StructureId));
    }

    [Fact]
    public void ProjectileStoppedUpstreamDoesNotDepositFullEnergyIntoDownstreamVoxel()
    {
        TissueProperties resistantTissue = TissueRegistry.Muscle;
        resistantTissue.Density = 2_000f;
        var upstream = new PhysiologicalVoxel(
            new Vector3(0f, 0f, -0.01f), 0.01f, resistantTissue, OrganType.Muscle);
        var downstream = new PhysiologicalVoxel(
            new Vector3(0f, 0f, 0.01f), 0.01f, resistantTissue, OrganType.Muscle);
        TacticalActorPhysiology target = BuildTarget(upstream, downstream);
        var profile = new BallisticProfile
        {
            Mass = 0.001f,
            CrossSectionalArea = 0.01f,
            DragModel = new StandardDragCurve(10f)
        };

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(
            Request(
                target,
                new Vector3(0f, 0f, -0.03f),
                new Vector3(0f, 0f, 10f),
                profile)));

        Assert.Equal(ProjectileDisposition.Retained, result.WoundTrack.Disposition);
        Assert.Single(result.EnergyLedger.StructureDeposits);
        Assert.True(upstream.DepositedEnergy > 0f);
        Assert.Equal(0f, downstream.DepositedEnergy);
        Assert.True(result.EnergyLedger.IsConserved, result.EnergyLedger.ConservationWarning);
    }

    [Fact]
    public void SameInputsAndSeedProduceIdenticalSerializedTracks()
    {
        ProjectileInteractionResult first = RunDeterministicReplay(seed: 1234UL);
        ProjectileInteractionResult replay = RunDeterministicReplay(seed: 1234UL);
        JsonSerializerOptions json = DamageModelJson.CreateOptions();

        Assert.Equal(
            JsonSerializer.Serialize(first.WoundTrack, json),
            JsonSerializer.Serialize(replay.WoundTrack, json));
        Assert.Equal(
            JsonSerializer.Serialize(first.DebugTrace.RandomMetadata, json),
            JsonSerializer.Serialize(replay.DebugTrace.RandomMetadata, json));
    }

    [Fact]
    public void MissReturnsNullWithoutMutatingTarget()
    {
        var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle);
        TacticalActorPhysiology target = BuildTarget(voxel);

        ProjectileInteractionResult? result = CreateService().Resolve(
            Request(target, new Vector3(1f, 0f, -0.02f), new Vector3(0f, 0f, 100f)));

        Assert.Null(result);
        Assert.Equal(0f, voxel.DepositedEnergy);
    }

    [Fact]
    public void LegacyComparisonIsExplicitAndReportsItsEnergyOverAllocation()
    {
        var first = new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle);
        var nearby = new PhysiologicalVoxel(new Vector3(0.009f, 0f, 0f), 0.01f, TissueRegistry.Muscle, OrganType.Muscle);
        TacticalActorPhysiology target = BuildTarget(first, nearby);
        ProjectileInteractionRequest request = Request(
            target,
            new Vector3(0f, 0f, -0.02f),
            new Vector3(0f, 0f, 100f),
            modelVersion: DamageModelVersion.LegacyV1);

        ProjectileInteractionResult result = Assert.IsType<ProjectileInteractionResult>(CreateService().Resolve(request));

        Assert.Equal(DamageModelVersion.LegacyV1, result.WoundTrack.ModelVersion);
        Assert.Equal(2, result.EnergyLedger.StructureDeposits.Count);
        Assert.False(result.EnergyLedger.IsConserved);
        Assert.NotNull(result.EnergyLedger.ConservationWarning);
        Assert.Contains(result.DebugTrace.NumericalWarnings, warning => warning.Contains("non-authoritative"));
    }

    [Fact]
    public void ConfiguredMigrationFlagSelectsLegacyWhilePerImpactOverrideSelectsFoundations()
    {
        var randomStreams = new DeterministicRandomStreamProvider(new FixedRootSeedProvider(42UL));
        var service = new ProjectileInteractionService(
            new DamageModelOptions(DamageModelVersion.LegacyV1),
            randomStreams);
        TacticalActorPhysiology legacyTarget = BuildTarget(
            new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle));
        TacticalActorPhysiology foundationsTarget = BuildTarget(
            new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle));

        ProjectileInteractionResult legacy = Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(legacyTarget, new Vector3(0f, 0f, -0.02f), new Vector3(0f, 0f, 100f))));
        ProjectileInteractionResult foundations = Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(
                foundationsTarget,
                new Vector3(0f, 0f, -0.02f),
                new Vector3(0f, 0f, 100f),
                modelVersion: DamageModelVersion.FoundationsV2)));

        Assert.Equal(DamageModelVersion.LegacyV1, legacy.WoundTrack.ModelVersion);
        Assert.Equal(DamageModelVersion.FoundationsV2, foundations.WoundTrack.ModelVersion);
        Assert.True(foundations.EnergyLedger.IsConserved, foundations.EnergyLedger.ConservationWarning);
    }

    [Fact]
    public void PointTraumaCannotBeInvokedWithAuthoritativeVersion()
    {
        var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Muscle, OrganType.Muscle);
        TacticalActorPhysiology target = BuildTarget(voxel);

        Assert.Throws<ArgumentException>(() => target.ProcessLegacyImpact(
            Vector3.UnitZ,
            Energy.FromJoules(100f),
            Vector3.Zero,
            DamageModelVersion.FoundationsV2));
        Assert.Equal(0f, voxel.DepositedEnergy);
    }

    [Theory]
    [InlineData("ProcessPenetration")]
    [InlineData("ProcessPenetrationStep")]
    [InlineData("ApplyKineticEnergy")]
    public void VoxelDamageMutationCannotBypassTheCoreService(string methodName)
    {
        Assert.DoesNotContain(
            typeof(PhysiologicalVoxel).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public),
            method => string.Equals(method.Name, methodName, StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyInjectionUsesFoundationsModelByDefault()
    {
        var services = new ServiceCollection();
        services.AddTacticalSimCore();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<ProjectileInteractionService>(provider.GetRequiredService<IProjectileInteractionService>());
        Assert.Equal(
            DamageModelVersion.FoundationsV2,
            provider.GetRequiredService<DamageModelOptions>().DefaultVersion);
    }

    private static ProjectileInteractionResult RunDeterministicReplay(ulong seed)
    {
        TacticalActorPhysiology target = BuildTarget(
            new PhysiologicalVoxel(new Vector3(0f, 0f, -0.01f), 0.01f, TissueRegistry.Muscle, OrganType.Muscle),
            new PhysiologicalVoxel(new Vector3(0f, 0f, 0.01f), 0.01f, TissueRegistry.Liver, OrganType.Liver));
        ProjectileInteractionService service = CreateService(seed);
        return Assert.IsType<ProjectileInteractionResult>(service.Resolve(
            Request(target, new Vector3(0f, 0f, -0.03f), new Vector3(0f, 0f, 200f))));
    }

    private static ProjectileInteractionRequest Request(
        IActorPhysiology target,
        Vector3 position,
        Vector3 velocity,
        BallisticProfile? profile = null,
        DamageModelVersion? modelVersion = null) => new(
            impactId: "impact-0001",
            projectileProfileId: "test-projectile",
            target,
            new ProjectileState { Position = position, Velocity = velocity, Time = 0f },
            profile ?? DefaultProfile(),
            Distance.FromMeters(1f),
            modelVersion);

    private static BallisticProfile DefaultProfile() => new()
    {
        Mass = 0.01f,
        CrossSectionalArea = 0.00001f,
        DragModel = new StandardDragCurve(0.1f)
    };

    private static ProjectileInteractionService CreateService(ulong seed = 42UL) => new(
        new DamageModelOptions(),
        new DeterministicRandomStreamProvider(new FixedRootSeedProvider(seed)));

    private static TacticalActorPhysiology BuildTarget(params PhysiologicalVoxel[] voxels)
    {
        var root = new BodyPart { Type = BodyPartType.Thorax };
        root.Voxels.AddRange(voxels);
        var physiology = new TacticalActorPhysiology();
        physiology.SetRoot(root);
        return physiology;
    }

    private static TacticalActorPhysiology BuildOverlappingHierarchy(bool reverseChildren)
    {
        var root = new BodyPart { Type = BodyPartType.Thorax };
        var left = new BodyPart { Type = BodyPartType.LeftArm };
        var right = new BodyPart { Type = BodyPartType.RightArm };
        left.Voxels.Add(new PhysiologicalVoxel(
            Vector3.Zero,
            0.01f,
            TissueRegistry.Muscle,
            OrganType.Muscle));
        right.Voxels.Add(new PhysiologicalVoxel(
            Vector3.Zero,
            0.01f,
            TissueRegistry.Muscle,
            OrganType.Muscle));
        if (reverseChildren)
        {
            root.Children.Add(right);
            root.Children.Add(left);
        }
        else
        {
            root.Children.Add(left);
            root.Children.Add(right);
        }

        var physiology = new TacticalActorPhysiology();
        physiology.SetRoot(root);
        return physiology;
    }
}
