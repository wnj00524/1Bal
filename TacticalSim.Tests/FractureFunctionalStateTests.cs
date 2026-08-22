using System.Numerics;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class FractureFunctionalStateTests
{
    private readonly MusculoskeletalFunctionalResolver _resolver = new();
    private readonly IAnatomicalStructureCatalog _anatomy = StandardAnatomy.CreateCatalog();

    [Fact]
    public void NoFractures_LeavesMusculoskeletalFunctionHealthy()
    {
        Assert.Equal(
            MusculoskeletalFunctionalState.Healthy,
            _resolver.Resolve([], _anatomy));
    }

    [Theory]
    [InlineData(FractureStability.Stable, 0.75f, true)]
    [InlineData(FractureStability.Displaced, 0.40f, true)]
    [InlineData(FractureStability.Unstable, 0f, false)]
    public void WeightBearingFracture_ConstrainsStandingAndMovement(
        FractureStability stability,
        float expectedCapacity,
        bool expectedCanStand)
    {
        FractureLesion fracture = CreateFracture(
            "fracture-femur",
            "bone.femur-left",
            stability,
            weightBearing: true);

        MusculoskeletalFunctionalState state = _resolver.Resolve([fracture], _anatomy);

        Assert.Equal(expectedCapacity, state.StandingCapacity);
        Assert.Equal(expectedCapacity, state.MovementCapacity);
        Assert.Equal(1f, state.UpperLimbCapacity);
        Assert.Equal(expectedCanStand, state.CanStand);
    }

    [Fact]
    public void CanonicalNonWeightBearingRole_OverridesStalePersistedFlag()
    {
        FractureLesion rib = CreateFracture(
            "fracture-rib",
            "bone.rib-01-left",
            FractureStability.Unstable,
            weightBearing: true);

        Assert.Equal(
            MusculoskeletalFunctionalState.Healthy,
            _resolver.Resolve([rib], _anatomy));
    }

    [Fact]
    public void TacticalActorPhysiology_RetainsPublicParameterlessConstructor()
    {
        Assert.NotNull(typeof(TacticalActorPhysiology).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void UpperLimbFracture_ConstrainsUpperLimbWithoutBlockingStanding()
    {
        FractureLesion humerus = CreateFracture(
            "fracture-humerus",
            "bone.humerus-right",
            FractureStability.Displaced,
            weightBearing: false);

        MusculoskeletalFunctionalState state = _resolver.Resolve([humerus], _anatomy);

        Assert.Equal(1f, state.StandingCapacity);
        Assert.Equal(1f, state.MovementCapacity);
        Assert.Equal(0.40f, state.UpperLimbCapacity);
        Assert.True(state.CanStand);
    }

    [Fact]
    public void MissingCatalogStructure_UsesPersistedWeightBearingFallback()
    {
        FractureLesion fracture = CreateFracture(
            "fracture-custom",
            "bone.custom",
            FractureStability.Stable,
            weightBearing: true);

        MusculoskeletalFunctionalState state = _resolver.Resolve(
            [fracture],
            new AnatomicalStructureCatalog([]));

        Assert.Equal(0.75f, state.StandingCapacity);
        Assert.Equal(0.75f, state.MovementCapacity);
    }

    [Fact]
    public void WorstEffectAggregation_IsOrderIndependentAndDoesNotDoubleCountDuplicates()
    {
        FractureLesion stable = CreateFracture(
            "fracture-stable",
            "bone.femur-left",
            FractureStability.Stable,
            weightBearing: true);
        FractureLesion displaced = CreateFracture(
            "fracture-displaced",
            "bone.tibia-right",
            FractureStability.Displaced,
            weightBearing: true);

        MusculoskeletalFunctionalState forward = _resolver.Resolve(
            [stable, displaced, stable],
            _anatomy);
        MusculoskeletalFunctionalState reverse = _resolver.Resolve(
            [displaced, stable],
            _anatomy);

        Assert.Equal(reverse, forward);
        Assert.Equal(0.40f, forward.MovementCapacity);
    }

    [Theory]
    [InlineData(FractureStability.Stable, 0.75f, true)]
    [InlineData(FractureStability.Unstable, 0f, false)]
    public void FractureLesion_ChangesActorFunctionWithIntactBoneVoxels(
        FractureStability stability,
        float expectedMobility,
        bool expectedCanStand)
    {
        (TacticalActorPhysiology physiology, PhysiologicalVoxel intactBone) =
            CreateIntactLegPhysiology();
        physiology.LesionRepository.AddRange([
            CreateFracture(
                "fracture-intact-bone",
                "bone.femur-left",
                stability,
                weightBearing: true)
        ]);

        physiology.TickPhysiology(0f);

        Assert.False(intactBone.IsDestroyed);
        Assert.Equal(expectedMobility, physiology.MobilityLevel);
        Assert.Equal(expectedCanStand, physiology.CanStand);
        Assert.Equal(expectedMobility, physiology.MusculoskeletalFunctionalState.StandingCapacity);
    }

    [Fact]
    public void SpeedBasedMoveAction_ConsumesLesionDerivedMobility()
    {
        (TacticalActorPhysiology physiology, _) = CreateIntactLegPhysiology();
        physiology.LesionRepository.AddRange([
            CreateFracture(
                "fracture-move",
                "bone.femur-left",
                FractureStability.Stable,
                weightBearing: true)
        ]);
        physiology.TickPhysiology(0f);
        var actor = new TacticalEntity(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Vector3.Zero,
            physiology);

        var action = new MoveTacticalAction(
            actor,
            Vector3.Zero,
            new Vector3(6f, 0f, 0f),
            baseMovementSpeed: 4f,
            computeCostFromSpeed: true);

        Assert.Equal(3f, action.MovementSpeed);
        Assert.Equal(2f, action.TUCost);
    }

    [Fact]
    public void FixedCostMoveAction_ScalesTraversalTimeForLesionDerivedMobility()
    {
        (TacticalActorPhysiology physiology, _) = CreateIntactLegPhysiology();
        physiology.LesionRepository.AddRange([
            CreateFracture(
                "fracture-fixed-cost-move",
                "bone.femur-left",
                FractureStability.Stable,
                weightBearing: true)
        ]);
        physiology.TickPhysiology(0f);
        var actor = new TacticalEntity(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Vector3.Zero,
            physiology);

        var action = new MoveTacticalAction(
            actor,
            Vector3.Zero,
            new Vector3(6f, 0f, 0f),
            tuCost: 2f);

        Assert.Equal(2f / 0.75f, action.TUCost, 5);
        Assert.Equal(2.25f, action.MovementSpeed, 5);
    }

    [Fact]
    public void StructurallyImmobileActor_CannotStartEntityBoundMovementActions()
    {
        (TacticalActorPhysiology physiology, _) = CreateIntactLegPhysiology();
        physiology.LesionRepository.AddRange([
            CreateFracture(
                "fracture-no-move",
                "bone.femur-left",
                FractureStability.Unstable,
                weightBearing: true)
        ]);
        physiology.TickPhysiology(0f);
        var actor = new TacticalEntity(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Vector3.Zero,
            physiology);

        InvalidOperationException speedBasedException = Assert.Throws<InvalidOperationException>(() =>
            new MoveTacticalAction(
                actor,
                Vector3.Zero,
                Vector3.UnitX,
                baseMovementSpeed: 4f,
                computeCostFromSpeed: true));
        InvalidOperationException fixedCostException = Assert.Throws<InvalidOperationException>(() =>
            new MoveTacticalAction(
                actor,
                Vector3.Zero,
                Vector3.UnitX,
                tuCost: 1f));

        Assert.Contains("zero mobility", speedBasedException.Message);
        Assert.Contains("zero mobility", fixedCostException.Message);
    }

    [Fact]
    public void UpperLimbFractureAndLegacyVoxelDamage_CombineByWorstCapacity()
    {
        var physiology = new TacticalActorPhysiology();
        var arm = new BodyPart { Type = BodyPartType.LeftArm };
        var intactBone = new PhysiologicalVoxel(
            Vector3.Zero,
            0.01f,
            TissueRegistry.Bone,
            OrganType.Bone);
        arm.Voxels.Add(intactBone);
        physiology.SetRoot(arm);
        physiology.SetAnatomy(_anatomy);
        physiology.LesionRepository.AddRange([
            CreateFracture(
                "fracture-arm",
                "bone.humerus-left",
                FractureStability.Stable,
                weightBearing: false)
        ]);

        physiology.TickPhysiology(0f);
        Assert.Equal(0.75f, physiology.WeaponHandlingLevel);
        Assert.Equal(1f, physiology.MobilityLevel);

        intactBone.ApplyKineticEnergy(1_000f, Vector3.Zero, 0.001f);
        physiology.TickPhysiology(0f);

        Assert.Equal(0f, physiology.WeaponHandlingLevel);
        Assert.Equal(1f, physiology.MobilityLevel);
    }

    private static (TacticalActorPhysiology Physiology, PhysiologicalVoxel IntactBone)
        CreateIntactLegPhysiology()
    {
        var physiology = new TacticalActorPhysiology();
        var leg = new BodyPart { Type = BodyPartType.LeftLeg };
        var intactBone = new PhysiologicalVoxel(
            Vector3.Zero,
            0.01f,
            TissueRegistry.Bone,
            OrganType.Bone);
        leg.Voxels.Add(intactBone);
        physiology.SetRoot(leg);
        physiology.SetAnatomy(StandardAnatomy.CreateCatalog());
        return (physiology, intactBone);
    }

    private static FractureLesion CreateFracture(
        string id,
        string structureId,
        FractureStability stability,
        bool weightBearing)
    {
        float severity = stability switch
        {
            FractureStability.Stable => 0.20f,
            FractureStability.Displaced => 0.50f,
            FractureStability.Unstable => 0.80f,
            _ => throw new ArgumentOutOfRangeException(nameof(stability))
        };
        return new FractureLesion(
            id,
            structureId,
            "impact-test",
            severity,
            new LesionGeometry(
                Vector3.Zero,
                Vector3.UnitZ,
                Distance.FromMeters(0.01f),
                Distance.FromMeters(0.002f)),
            LesionTreatmentState.Untreated,
            DateTimeOffset.UnixEpoch,
            stability,
            weightBearing);
    }
}
