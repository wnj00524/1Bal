using System.Text.Json;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage.Variation;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;

namespace TacticalSim.Tests;

public class M11VariationTests
{
    [Fact]
    public void DefaultProfile_PreservesLegacyBaseline_AndRoundTrips()
    {
        string json = JsonSerializer.Serialize(CasualtyProfile.Default);
        var profile = JsonSerializer.Deserialize<CasualtyProfile>(json)!;
        var physiology = new TacticalActorPhysiology(
            new TacticalSim.Core.Damage.Physiology.MusculoskeletalFunctionalResolver(),
            new TacticalSim.Core.Damage.Physiology.NeurologicalFunctionalResolver(), profile);

        Assert.Equal(5000f, physiology.TotalBloodVolume);
        Assert.Equal(80f, physiology.HeartRateBpm);
        Assert.Equal(CasualtyProfile.CurrentSchemaVersion, profile.SchemaVersion);
    }

    [Fact]
    public void BodyMassProfile_DerivesBloodVolume()
    {
        var profile = CasualtyProfile.FromBodyMass("large", 100f);
        profile.Validate();
        Assert.Equal(7000f, profile.BloodVolumeMilliliters);
    }

    [Fact]
    public void PhysiologicalUncertainty_IsBoundedSeededAndCanBeDisabled()
    {
        static PhysiologicalVariation Draw(ulong seed) => PhysiologicalVariationSampler.Sample(
            new DeterministicRandomStreamProvider(new FixedRootSeedProvider(seed)), "actor-1");
        Assert.Equal(Draw(42), Draw(42));
        Assert.NotEqual(Draw(42), Draw(43));
        var sample = Draw(42);
        Assert.InRange(sample.BloodVolumeMultiplier, 0.92f, 1.08f);
        Assert.InRange(sample.HeartRateOffsetBpm, -8f, 8f);
        Assert.Equal(new PhysiologicalVariation(1f, 0f, 0f, 1f),
            PhysiologicalVariationSampler.Sample(
                new DeterministicRandomStreamProvider(new FixedRootSeedProvider(42)), "actor-1",
                new PhysiologicalUncertaintyOptions { Enabled = false }));
    }

    [Fact]
    public void TerminalProfiles_DifferentConstructionChangesTrackInputsAtEqualEnergy()
    {
        var source = new BallisticProfile { Mass = 0.008f, CrossSectionalArea = 0.00005f, DragModel = new StandardDragCurve(0.2f) };
        var fmj = new TerminalProjectileProfile("fmj", ProjectileConstruction.FullMetalJacket, 0.1f, 1000f, 1f, 1000f, 1f);
        var expanding = new TerminalProjectileProfile("hp", ProjectileConstruction.HollowPoint, 0.2f, 250f, 2f, 1000f, 1f);
        Assert.True(expanding.Apply(source, 350f).CrossSectionalAreaSquareMeters.SquareMeters >
            fmj.Apply(source, 350f).CrossSectionalAreaSquareMeters.SquareMeters);
    }

    [Fact]
    public void Wearables_CanStopProjectileAndReportBluntEnergy()
    {
        var result = WearableBarrierResolver.Resolve(0.008f, 300f,
            [new("shirt", 5f, 0.98f, false), new("plate", 1000f, 0f, true)]);
        Assert.False(result.Penetrated);
        Assert.Equal(0f, result.ResidualSpeedMetersPerSecond);
        Assert.True(result.BluntEnergy.Joules > 0f);
        Assert.Equal(["shirt", "plate"], result.AppliedLayers);
    }

    [Fact]
    public void CohortRunner_IsReproducibleAndIsolatesFailures()
    {
        static ulong Scenario(CohortCase item) => item.Index == 2 ? throw new InvalidOperationException("case failed") : item.Seed;
        var first = CohortRunner.Run(1000, 123UL, "v2", Scenario);
        var replay = CohortRunner.Run(1000, 123UL, "v2", Scenario);
        Assert.Equal(first.Outcomes, replay.Outcomes);
        Assert.Single(first.Failures);
        Assert.Equal(2, first.Failures[0].Index);
        Assert.Equal(999, first.Outcomes.Count);
        Assert.True(first.Elapsed >= TimeSpan.Zero);
    }
}
