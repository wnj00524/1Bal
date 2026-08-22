using TacticalSim.Core.Units;
using TacticalSim.Core.Materials;

namespace TacticalSim.Tests;

public class TypedUnitTests
{
    [Fact]
    public void Pressure_ExplicitlyConvertsMpaToCanonicalPa()
    {
        Pressure pressure = Pressure.FromMegapascals(2.5f);

        Assert.Equal(2_500_000f, pressure.Pascals);
        Assert.Equal(2.5f, UnitBoundaryConversions.ToDisplayMegapascals(pressure));
    }

    [Fact]
    public void MaterialProperties_ExposeYieldStrengthAsCanonicalPascals()
    {
        var material = new MaterialProperties("test", MaterialType.Wood, 600f, 0.5f, 2.5f);

        Assert.Equal(2_500_000f, material.YieldStrengthPressure.Pascals);
        Assert.Equal(0f, material.YieldEnergy.Joules);
    }

    [Fact]
    public void MaterialProperties_EnergyThresholdDoesNotBecomeYieldStrength()
    {
        var material = new MaterialProperties(
            "test",
            MaterialType.Wood,
            density: 600f,
            resistanceCoefficient: 0.5f,
            ricochetAngleThreshold: 1.4f,
            yieldEnergyThreshold: 50f);

        Assert.Equal(50f, material.YieldEnergy.Joules);
        Assert.Equal(0f, material.YieldStrengthPressure.Pascals);
    }

    [Fact]
    public void BoundaryConversions_RoundTripAllCanonicalQuantities()
    {
        Distance distance = UnitBoundaryConversions.FromSerializedMeters(1.25f);
        Area area = UnitBoundaryConversions.FromSerializedSquareMeters(0.000024f);
        Volume volume = UnitBoundaryConversions.FromSerializedCubicMeters(0.00025f);
        Mass mass = UnitBoundaryConversions.FromSerializedKilograms(0.0097f);
        Density density = UnitBoundaryConversions.FromSerializedKilogramsPerCubicMeter(1_060f);
        TacticalSim.Core.Units.Time time = UnitBoundaryConversions.FromSerializedSeconds(0.01f);
        Energy energy = UnitBoundaryConversions.FromSerializedJoules(1_200f);
        Pressure pressure = UnitBoundaryConversions.FromSerializedPascals(2_500_000f);
        FlowRate flow = UnitBoundaryConversions.FromSerializedCubicMetersPerSecond(0.000002f);

        Assert.Equal(1.25f, UnitBoundaryConversions.ToSerializedMeters(distance));
        Assert.Equal(0.000024f, UnitBoundaryConversions.ToSerializedSquareMeters(area));
        Assert.Equal(0.00025f, UnitBoundaryConversions.ToSerializedCubicMeters(volume));
        Assert.Equal(0.0097f, UnitBoundaryConversions.ToSerializedKilograms(mass), 6);
        Assert.Equal(1_060f, UnitBoundaryConversions.ToSerializedKilogramsPerCubicMeter(density));
        Assert.Equal(0.01f, UnitBoundaryConversions.ToSerializedSeconds(time));
        Assert.Equal(1_200f, UnitBoundaryConversions.ToSerializedJoules(energy));
        Assert.Equal(2_500_000f, UnitBoundaryConversions.ToSerializedPascals(pressure));
        Assert.Equal(0.000002f, UnitBoundaryConversions.ToSerializedCubicMetersPerSecond(flow));
    }

    [Fact]
    public void DisplayBoundaryConversions_RoundTripAreaVolumeDensityAndFlow()
    {
        Area area = UnitBoundaryConversions.FromDisplaySquareMillimeters(24f);
        Volume volume = UnitBoundaryConversions.FromDisplayCubicCentimeters(250f);
        Density density = UnitBoundaryConversions.FromDisplayKilogramsPerCubicMeter(1_060f);
        FlowRate perSecond = UnitBoundaryConversions.FromDisplayMillilitersPerSecond(2f);
        FlowRate perMinute = UnitBoundaryConversions.FromDisplayMillilitersPerMinute(120f);

        Assert.Equal(24f, UnitBoundaryConversions.ToDisplaySquareMillimeters(area), 4);
        Assert.Equal(250f, UnitBoundaryConversions.ToDisplayCubicCentimeters(volume), 4);
        Assert.Equal(1_060f, UnitBoundaryConversions.ToDisplayKilogramsPerCubicMeter(density));
        Assert.Equal(2f, UnitBoundaryConversions.ToDisplayMillilitersPerSecond(perSecond), 4);
        Assert.Equal(120f, UnitBoundaryConversions.ToDisplayMillilitersPerMinute(perMinute), 4);
    }

    [Fact]
    public void UnitValues_RejectNonFiniteBoundaryInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pressure.FromMegapascals(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Time.FromSeconds(float.PositiveInfinity));
    }

    [Fact]
    public void UnitTypes_DoNotExposeImplicitNumericOrCrossQuantityConversions()
    {
        Assert.Null(typeof(Pressure).GetMethod("op_Implicit"));
        Assert.Null(typeof(Energy).GetMethod("op_Implicit"));
        Assert.Null(typeof(Pressure).GetMethod("op_Addition", new[] { typeof(Pressure), typeof(Energy) }));
    }
}
