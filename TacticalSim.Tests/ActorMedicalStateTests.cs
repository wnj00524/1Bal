using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Damage.Variation;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class ActorMedicalStateTests
{
    [Fact]
    public void ImpactIsIdempotentAndUsesSimulationClock()
    {
        var state = Create(); state.Tick(12.5f);
        Lesion lesion = Vessel("impact-1");
        Assert.True(state.ApplyImpact("impact-1", [lesion]));
        Assert.False(state.ApplyImpact("impact-1", [lesion]));
        Assert.Single(state.LesionRepository.Lesions);
        Assert.Single(state.Hemorrhage.Sources);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(12.5), state.LesionRepository.Lesions[0].CreatedAt);
    }

    [Fact]
    public void OneTickAdvancesCompositeStateAndSnapshotIsDetached()
    {
        var state = Create(); state.ApplyImpact("impact-1", [Vessel("impact-1")]);
        var before = state.CaptureSnapshot(); state.Tick(30f); var after = state.CaptureSnapshot();
        Assert.Equal(0f, before.SimulationTimeSeconds);
        Assert.Equal(30f, after.SimulationTimeSeconds);
        Assert.True(after.CirculatingBloodMl < before.CirculatingBloodMl);
        Assert.Equal("m5-m12-integrated-v3", after.ModelVersion);
        Assert.Single(before.Lesions);
    }

    private static ActorMedicalState Create() => new("casualty-1", CasualtyProfile.Default,
        StandardAnatomy.CreateCatalog(), new DeterministicRandomStreamProvider(new FixedRootSeedProvider(42)),
        new PhysiologicalUncertaintyOptions { Enabled = false });

    private static Lesion Vessel(string impact) => new VesselLesion($"lesion/{impact}/vessel.aorta", "vessel.aorta", impact,
        LesionKind.VesselLaceration, .8f, new(new(0,.3f,0), new(0,0,1), Distance.FromMeters(.02f), Distance.FromMeters(.004f)),
        LesionTreatmentState.Untreated, DateTimeOffset.UnixEpoch, Distance.FromMeters(.006f), PressureRegime.Arterial, false);
}
