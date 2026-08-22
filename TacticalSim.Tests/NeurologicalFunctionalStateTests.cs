using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class NeurologicalFunctionalStateTests
{
    private readonly IAnatomicalStructureCatalog _anatomy = StandardAnatomy.CreateCatalog();
    private readonly NeurologicalFunctionalResolver _resolver = new();

    [Fact]
    public void StandardAnatomy_HasAddressableSpinalLevelsAndPairedMajorLimbNerves()
    {
        Assert.Equal(FunctionalRole.SpinalCord, _anatomy.GetRequired("nerve.spinal-cord-cervical").FunctionalRole);
        Assert.Equal(BodyPartType.Thorax, _anatomy.GetRequired("nerve.spinal-cord-thoracic").Region);
        Assert.Equal(BodyPartType.Abdomen, _anatomy.GetRequired("nerve.spinal-cord-lumbar").Region);

        string[] pairedNerves = ["brachial-plexus", "median", "radial", "ulnar", "sciatic", "femoral", "tibial", "common-peroneal"];
        foreach (string nerve in pairedNerves)
        {
            Assert.Equal("left", _anatomy.GetRequired($"nerve.{nerve}-left").Laterality);
            Assert.Equal("right", _anatomy.GetRequired($"nerve.{nerve}-right").Laterality);
        }
    }

    [Fact]
    public void LeftPeripheralNerveInjury_AffectsOnlyRelevantLimb()
    {
        NeurologicalFunctionalState state = _resolver.Resolve([
            Nerve("nerve.median-left", NerveDamageGrade.CompleteDisruption, "left")
        ], _anatomy);

        Assert.Equal(0f, state.LeftUpperLimbCapacity);
        Assert.Equal(1f, state.RightUpperLimbCapacity);
        Assert.Equal(1f, state.LeftLowerLimbCapacity);
        Assert.Equal(1f, state.RightLowerLimbCapacity);
    }

    [Fact]
    public void CervicalHemilesion_IsLevelAndLateralitySpecific()
    {
        NeurologicalFunctionalState state = _resolver.Resolve([
            Nerve("nerve.spinal-cord-cervical", NerveDamageGrade.PartialDisruption, "right", "cervical")
        ], _anatomy);

        Assert.Equal(1f, state.LeftUpperLimbCapacity);
        Assert.Equal(.4f, state.RightUpperLimbCapacity);
        Assert.Equal(1f, state.LeftLowerLimbCapacity);
        Assert.Equal(.4f, state.RightLowerLimbCapacity);
    }

    [Fact]
    public void ThoracicMidlineLesion_LeavesArmsIntactAndAffectsBothLegs()
    {
        NeurologicalFunctionalState state = _resolver.Resolve([
            Nerve("nerve.spinal-cord-thoracic", NerveDamageGrade.CompleteDisruption, null, "thoracic")
        ], _anatomy);

        Assert.Equal(1f, state.UpperLimbCapacity);
        Assert.Equal(0f, state.LeftLowerLimbCapacity);
        Assert.Equal(0f, state.RightLowerLimbCapacity);
    }

    [Theory]
    [InlineData(NerveDamageGrade.Neuropraxia, .8f)]
    [InlineData(NerveDamageGrade.PartialDisruption, .4f)]
    [InlineData(NerveDamageGrade.CompleteDisruption, 0f)]
    public void DamageGrade_ProducesDeterministicCapacity(NerveDamageGrade grade, float expected)
    {
        Assert.Equal(expected, NeurologicalFunctionalResolver.CapacityFor(grade));
    }

    [Fact]
    public void ActorFunction_UsesNerveLesionsWithoutDestroyedBrainVoxels()
    {
        var physiology = new TacticalActorPhysiology();
        var leg = new BodyPart { Type = BodyPartType.LeftLeg };
        var intactMuscle = new PhysiologicalVoxel(Vector3.Zero, .01f, TissueRegistry.Muscle, OrganType.Muscle);
        leg.Voxels.Add(intactMuscle);
        physiology.SetRoot(leg);
        physiology.SetAnatomy(_anatomy);
        physiology.LesionRepository.AddRange([
            Nerve("nerve.spinal-cord-thoracic", NerveDamageGrade.CompleteDisruption, null, "thoracic")
        ]);

        physiology.TickPhysiology(0f);

        Assert.False(intactMuscle.IsDestroyed);
        Assert.Equal(0f, physiology.MobilityLevel);
        Assert.False(physiology.CanStand);
        Assert.Equal(1f, physiology.WeaponHandlingLevel);
    }

    [Fact]
    public void AddDamageModel_RegistersNeurologicalResolver()
    {
        using ServiceProvider provider = new ServiceCollection().AddDamageModel().BuildServiceProvider();
        Assert.IsType<NeurologicalFunctionalResolver>(provider.GetRequiredService<INeurologicalFunctionalResolver>());
    }

    private static NerveLesion Nerve(string structureId, NerveDamageGrade grade, string? side, string? level = null) =>
        new($"lesion-{structureId}-{grade}", structureId, "impact-test",
            level is null ? LesionKind.NerveInjury : LesionKind.BrainOrSpinalInjury,
            grade switch { NerveDamageGrade.Neuropraxia => .2f, NerveDamageGrade.PartialDisruption => .5f, _ => .9f },
            new LesionGeometry(Vector3.Zero, Vector3.UnitZ, Distance.FromMeters(.01f), Distance.FromMeters(.002f)),
            LesionTreatmentState.Untreated, DateTimeOffset.UnixEpoch, grade, side, level);
}
