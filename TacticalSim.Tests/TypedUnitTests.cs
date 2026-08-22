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
    }

    [Fact]
    public void BoundaryConversions_RoundTripCanonicalQuantities()
    {
        Mass mass = UnitBoundaryConversions.FromSerializedKilograms(0.0097f);
        Volume volume = Volume.FromCubicCentimeters(250f);
        FlowRate flow = FlowRate.FromMillilitersPerMinute(120f);

        Assert.Equal(0.0097f, UnitBoundaryConversions.ToSerializedKilograms(mass), 6);
        Assert.Equal(250f, UnitBoundaryConversions.ToDisplayCubicCentimeters(volume), 4);
        Assert.Equal(120f, UnitBoundaryConversions.ToDisplayMillilitersPerMinute(flow), 4);
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
