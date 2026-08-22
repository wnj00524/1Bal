using System.Numerics;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public class HemorrhagePhysiologyModelTests
{
    [Fact]
    public void ArterialVenousAndTissueSourcesHaveDistinctPressureDependentFlows()
    {
        var model = new HemorrhagePhysiologyModel();
        model.AddSource(new("artery", PressureRegime.Arterial, 4f, false, BloodDestination.External, true));
        model.AddSource(new("vein", PressureRegime.Venous, 4f, false, BloodDestination.LocalSoftTissue, true));
        model.AddSource(new("tissue", PressureRegime.Parenchymal, 4f, false, BloodDestination.Peritoneal, false));
        float initial = model.CurrentBleedRateMlPerSecond;

        model.Tick(60f);

        Assert.True(initial > model.CurrentBleedRateMlPerSecond);
        Assert.True(model.Blood.LostByDestination[BloodDestination.External] > model.Blood.LostByDestination[BloodDestination.LocalSoftTissue]);
        Assert.True(model.Blood.LostByDestination[BloodDestination.LocalSoftTissue] > model.Blood.LostByDestination[BloodDestination.Peritoneal]);
    }

    [Fact]
    public void EveryLostMilliliterIsAssignedExactlyOnce()
    {
        var model = new HemorrhagePhysiologyModel();
        foreach (BloodDestination destination in Enum.GetValues<BloodDestination>())
            model.AddSource(new(destination.ToString(), PressureRegime.Venous, 2f, false, destination, true));

        model.Tick(20f);

        Assert.All(model.Blood.LostByDestination.Values, value => Assert.True(value > 0f));
        Assert.InRange(MathF.Abs(model.Blood.ConservationErrorMilliliters), 0f, .05f);
        Assert.InRange(MathF.Abs(model.Blood.BaselineMilliliters - model.Blood.CirculatingMilliliters - model.Blood.TotalLostMilliliters), 0f, .05f);
    }

    [Fact]
    public void ControlIsSourceSpecificAndRespectsCompressibility()
    {
        var model = new HemorrhagePhysiologyModel();
        model.AddSource(new("limb", PressureRegime.Arterial, 5f, false, BloodDestination.External, true));
        model.AddSource(new("internal", PressureRegime.Arterial, 5f, false, BloodDestination.Peritoneal, false));

        Assert.True(model.TryControlSource("limb", BleedingControlState.Tourniquet));
        Assert.False(model.TryControlSource("internal", BleedingControlState.Tourniquet));
        model.Tick(5f);

        Assert.True(model.Blood.LostByDestination[BloodDestination.Peritoneal] > model.Blood.LostByDestination[BloodDestination.External] * 20f);
    }

    [Fact]
    public void MinorSourceClotsButMajorVesselDoesNotSelfResolveAndMovementCanRebleed()
    {
        var minor = new BleedingSource("minor", PressureRegime.Venous, 1f, false, BloodDestination.External, true);
        var major = new BleedingSource("major", PressureRegime.Arterial, 7f, true, BloodDestination.External, true);
        var model = new HemorrhagePhysiologyModel(); model.AddSource(minor); model.AddSource(major);
        minor.TrySetControl(BleedingControlState.Compressed);

        model.Tick(180f);
        Assert.Equal(ClotState.Stable, minor.ClotState);
        Assert.NotEqual(ClotState.Stable, major.ClotState);
        model.ApplyMovementStress(.8f);
        Assert.Equal(ClotState.Disrupted, minor.ClotState);
    }

    [Fact]
    public void LargeAndSmallTimestepsRemainClose()
    {
        HemorrhagePhysiologyModel Create() { var m = new HemorrhagePhysiologyModel(); m.AddSource(new("a", PressureRegime.Arterial, 5f, true, BloodDestination.External, true)); return m; }
        var large = Create(); var small = Create();
        large.Tick(60f); for (int i = 0; i < 600; i++) small.Tick(.1f);
        Assert.InRange(MathF.Abs(large.Blood.CirculatingMilliliters - small.Blood.CirculatingMilliliters), 0f, .1f);
        Assert.InRange(MathF.Abs(large.Cardiovascular.MeanArterialPressureMmhg - small.Cardiovascular.MeanArterialPressureMmhg), 0f, .1f);
    }

    [Fact]
    public void SaturationAndDeliveryAreIndependentAndDeathIsLatched()
    {
        var model = new HemorrhagePhysiologyModel(100f);
        model.AddSource(new("fatal", PressureRegime.Arterial, 20f, true, BloodDestination.External, true));
        model.Tick(60f);
        Assert.True(model.OxygenDelivery.ArterialSaturation > .95f);
        Assert.True(model.OxygenDelivery.CerebralDeliveryIndex < .18f);
        Assert.Equal(CasualtyState.Dead, model.CasualtyState);
        model.TryControlSource("fatal", BleedingControlState.Definitive);
        model.Tick(60f);
        Assert.Equal(CasualtyState.Dead, model.CasualtyState);
    }

    [Fact]
    public void CapabilityResolverCoversEveryTacticalCapabilityAndExplainsPenalties()
    {
        var resolver = new PhysiologyCapabilityResolver();
        var cardiovascular = new CardiovascularState(110f, .5f, .6f, 1.4f, 55f, .6f);
        var oxygen = new OxygenDeliveryState(1f, 1f, .6f, .6f, .45f);
        var muscle = new MusculoskeletalFunctionalState(.4f, .4f, .75f, true);
        var state = resolver.Resolve(cardiovascular, oxygen, CasualtyState.Incapacitated, muscle, NeurologicalFunctionalState.Healthy);

        Assert.Equal(Enum.GetValues<TacticalCapability>().Length, state.Capacity.Count);
        Assert.Equal(.4f, state[TacticalCapability.Movement], 3);
        Assert.Equal(.45f, state[TacticalCapability.Firing], 3);
        Assert.Contains("reduced perfusion", state.Reasons);
        Assert.Contains("musculoskeletal injury", state.Reasons);
    }

    [Fact]
    public void FactoryBuildsSourceFromPersistentLesionAndAnatomy()
    {
        IAnatomicalStructureCatalog anatomy = StandardAnatomy.CreateCatalog();
        var lesion = new VesselLesion("L1", "vessel.femoral-left", "impact", LesionKind.VesselTransection, .9f,
            new(new Vector3(-.1f, -.1f, 0), Vector3.UnitY, Distance.FromMeters(.02f), Distance.FromMeters(.004f)),
            LesionTreatmentState.Untreated, DateTimeOffset.UnixEpoch, Distance.FromMeters(.006f), PressureRegime.Arterial, true);

        BleedingSource source = Assert.IsType<BleedingSource>(BleedingSourceFactory.FromLesion(lesion, anatomy));
        Assert.Equal(BloodDestination.LocalSoftTissue, source.Destination);
        Assert.True(source.Compressible);
        Assert.True(source.CompleteTransection);
    }
}
