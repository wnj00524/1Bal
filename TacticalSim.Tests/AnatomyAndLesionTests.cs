using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class AnatomyAndLesionTests
{
    [Fact]
    public void StandardCatalog_HasStableVersionedMajorVesselsBonesAndNerves()
    {
        IAnatomicalStructureCatalog anatomy = StandardAnatomy.CreateCatalog();

        Assert.Equal("anatomy-m6-v1", anatomy.DefinitionVersion);
        AnatomicalStructure aorta = anatomy.GetRequired("vessel.aorta");
        Assert.Equal(PressureRegime.Arterial, aorta.PressureRegime);
        Assert.True(aorta.Calibre.Meters > 0);
        Assert.Contains(anatomy.Structures, x => x.Id == "bone.femur-left" && x.FunctionalRole == FunctionalRole.WeightBearing);
        Assert.Contains(anatomy.Structures, x => x.Id == "nerve.spinal-cord" && x.FunctionalRole == FunctionalRole.SpinalCord);
        Assert.Contains(anatomy.Structures, x => x.Type == AnatomicalStructureType.Pleura && x.Laterality == "right");
    }

    [Fact]
    public void VesselQuery_DistinguishesNearMissFromIntersection()
    {
        var vessel = new AnatomicalStructure("vessel.test", "test artery", AnatomicalStructureType.Artery,
            BodyPartType.LeftLeg, new(0, 0, 0), new(0, 1, 0), Distance.FromMeters(.004f),
            calibre: Distance.FromMeters(.008f), pressureRegime: PressureRegime.Arterial);
        var anatomy = new AnatomicalStructureCatalog([vessel]);

        Assert.Empty(anatomy.QuerySegment(new(.02f, .5f, -.1f), new(.02f, .5f, .1f)));
        Assert.Single(anatomy.QuerySegment(new(.003f, .5f, -.1f), new(.003f, .5f, .1f)));
    }

    [Theory]
    [InlineData(.001f, LesionKind.VesselTransection, true)]
    [InlineData(.010f, LesionKind.VesselLaceration, false)]
    public void Generator_DistinguishesVesselLacerationAndTransection(float vesselDiameter, LesionKind expected, bool complete)
    {
        var vessel = new AnatomicalStructure("vessel.test", "test artery", AnatomicalStructureType.Artery,
            BodyPartType.LeftLeg, new(0, -.1f, 0), new(0, .1f, 0), Distance.FromMeters(vesselDiameter / 2),
            calibre: Distance.FromMeters(vesselDiameter), pressureRegime: PressureRegime.Arterial);
        Lesion lesion = Assert.Single(new LesionGenerator().Generate(CreateTrack("impact-a", 100f), new AnatomicalStructureCatalog([vessel])));

        var vascular = Assert.IsType<VesselLesion>(lesion);
        Assert.Equal(expected, vascular.Kind);
        Assert.Equal(complete, vascular.CompleteTransection);
    }

    [Fact]
    public void Repository_AccumulatesHitsAndTreatmentWithoutScanningVoxels()
    {
        IAnatomicalStructureCatalog anatomy = StandardAnatomy.CreateCatalog();
        var generator = new LesionGenerator();
        var repository = new LesionRepository();
        repository.AddRange(generator.Generate(CreateTrack("first", 80f), anatomy));
        repository.AddRange(generator.Generate(CreateTrack("second", 80f), anatomy));

        Assert.NotEmpty(repository.Lesions);
        Assert.Contains(repository.Lesions, x => x.OriginImpactId == "first");
        Assert.Contains(repository.Lesions, x => x.OriginImpactId == "second");
        Lesion first = repository.Lesions[0];
        Assert.True(repository.TrySetTreatmentState(first.Id, LesionTreatmentState.TemporarilyControlled));
        Assert.Equal(LesionTreatmentState.TemporarilyControlled, repository.Lesions[0].TreatmentState);
    }

    [Fact]
    public void Lesion_PolymorphicJsonRoundTripAndReadOnlyInspectorPreserveClinicalDetail()
    {
        var target = (TacticalActorPhysiology)TacticalSim.Core.AnatomicalDummyBuilder.BuildDummy();
        var fracture = new FractureLesion("lesion-1", "bone.femur-left", "impact-1", .8f,
            new(Vector3.Zero, Vector3.UnitX, Distance.FromMeters(.02f), Distance.FromMeters(.004f)),
            LesionTreatmentState.Untreated, DateTimeOffset.UnixEpoch, FractureStability.Unstable, true);
        target.LesionRepository.AddRange([fracture]);
        string json = JsonSerializer.Serialize<Lesion>(fracture, DamageModelJson.CreateOptions());

        Lesion restored = JsonSerializer.Deserialize<Lesion>(json, DamageModelJson.CreateOptions())!;
        Assert.Equal(fracture, Assert.IsType<FractureLesion>(restored));
        LesionDebugItem item = Assert.Single(LesionDebugInspector.Inspect(target));
        Assert.Contains("weightBearing=True", item.Details);
        Assert.Equal(LesionTreatmentState.Untreated, target.LesionRepository.Lesions[0].TreatmentState);
    }

    [Fact]
    public void DependencyInjection_RegistersLesionGenerator()
    {
        using ServiceProvider provider = new ServiceCollection().AddTacticalSimCore().BuildServiceProvider();
        Assert.IsType<LesionGenerator>(provider.GetRequiredService<ILesionGenerator>());
    }

    private static WoundTrack CreateTrack(string id, float energy)
    {
        Energy incoming = Energy.FromJoules(energy), outgoing = Energy.FromJoules(0);
        var change = new ProjectileStateChange(0, ProjectileStateChangeKind.Retained, new(0, 0, .05f), Vector3.UnitZ, Vector3.Zero, incoming, outgoing);
        var segment = new WoundTrackSegment(0, "voxel/soft-tissue", "Thorax", "Muscle", new(0, 0, -.05f), new(0, 0, .05f), Distance.FromMeters(.1f), incoming, incoming, outgoing, change);
        var ledger = new EnergyLedger(incoming, outgoing, [new EnergyDeposit(0, segment.StructureId, incoming)], Energy.FromJoules(0), Energy.FromJoules(0));
        return new WoundTrack(id, DamageModelVersion.FoundationsV2, WoundTrackCoordinateSpace.BodyLocalMeters,
            segment.EntryPoint, ProjectileDisposition.Retained, null, segment.EndPoint, [segment], [], ledger);
    }
}
