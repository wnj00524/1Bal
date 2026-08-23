using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Physiology;

namespace TacticalSim.Tests;

public sealed class ThoracicInjuryModelTests
{
    [Fact]
    public void UnilateralSimplePneumothorax_ProgressesWithoutImmediateTension()
    {
        var model = Create(); model.AddLesion(new("left-lung", ThoracicSide.Left, 20f, 0f, false));
        model.Tick(10f);
        Assert.InRange(model.Left.GasMilliliters, 199f, 201f);
        Assert.False(model.Left.IsTension); Assert.Equal(0f, model.Right.GasMilliliters);
        Assert.True(model.State.VentilationEffectiveness < 1f);
    }

    [Fact]
    public void OpenWound_ExchangesGasWhileEffectiveSealStopsEnvironmentalIngress()
    {
        var open = Create(); open.AddLesion(new("open", ThoracicSide.Left, 0f, 1f, true)); open.Tick(5f);
        var sealedChest = Create(); sealedChest.AddLesion(new("open", ThoracicSide.Left, 0f, 1f, true));
        sealedChest.ApplyChestSeal(ThoracicSide.Left, ChestSealState.Effective); sealedChest.Tick(5f);
        Assert.True(open.Left.GasMilliliters > 0f); Assert.Equal(0f, sealedChest.Left.GasMilliliters);
    }

    [Fact]
    public void OneWayLeak_BecomesTensionAndReducesRespiratoryAndCirculatoryFunction()
    {
        var model = Create(); model.AddLesion(new("valve", ThoracicSide.Right, 150f, .5f, true)); model.Tick(10f);
        Assert.True(model.Right.IsTension);
        Assert.True(model.State.VentilationEffectiveness < 1f); Assert.True(model.State.CardiacOutputModifier < 1f);
    }

    [Fact]
    public void MassiveHemothorax_ConservesBloodAndDoesNotCreatePleuralGas()
    {
        var physiology = new HemorrhagePhysiologyModel();
        physiology.AddSource(new("chest-vessel", PressureRegime.Arterial, 12f, true, BloodDestination.LeftPleural, false));
        var model = new ThoracicInjuryModel(physiology); model.Tick(60f);
        Assert.True(model.Left.BloodMilliliters > 0f); Assert.Equal(0f, model.Left.GasMilliliters);
        Assert.InRange(MathF.Abs(physiology.Blood.ConservationErrorMilliliters), 0f, .05f);
    }

    [Fact]
    public void PulmonaryInjury_ReducesVentilationWithoutTension()
    {
        var model = Create(); model.AddLesion(new("contusion", ThoracicSide.Left, 0f, 0f, false, .5f)); model.Tick(1f);
        Assert.False(model.Left.IsTension); Assert.InRange(model.State.VentilationEffectiveness, .74f, .76f);
    }

    [Fact]
    public void PericardialBleeding_CausesTamponadeIndependentOfLungs()
    {
        var physiology = new HemorrhagePhysiologyModel();
        physiology.AddSource(new("cardiac", PressureRegime.Arterial, 10f, true, BloodDestination.Pericardial, false));
        var model = new ThoracicInjuryModel(physiology, new(TamponadeOnsetMl: 1f, TamponadeCriticalMl: 10f)); model.Tick(10f);
        Assert.True(model.State.TamponadeSeverity > 0f); Assert.True(model.State.CardiacOutputModifier < 1f);
        Assert.Equal(0f, model.Left.LungCompression); Assert.Equal(0f, model.Right.LungCompression);
    }

    [Fact]
    public void IncorrectIntervention_DoesNotTreatHemothoraxOrOppositeSideTension()
    {
        var model = Create(); model.AddLesion(new("right", ThoracicSide.Right, 200f, .5f, true)); model.Tick(10f);
        float gas = model.Right.GasMilliliters;
        Assert.Equal(DecompressionOutcome.WrongSide, model.NeedleDecompress(ThoracicSide.Left));
        Assert.Equal(gas, model.Right.GasMilliliters);
    }

    [Fact]
    public void Decompression_CanSucceedFailAndRecur()
    {
        var model = Create(); model.AddLesion(new("right", ThoracicSide.Right, 200f, .5f, true)); model.Tick(10f);
        Assert.Equal(DecompressionOutcome.Ineffective, model.NeedleDecompress(ThoracicSide.Right, .1f));
        Assert.Equal(DecompressionOutcome.Successful, model.NeedleDecompress(ThoracicSide.Right));
        Assert.False(model.Right.IsTension); model.Tick(20f);
        Assert.True(model.Right.GasMilliliters > 0f); // continuing leak permits recurrence if venting is overwhelmed
    }

    [Theory]
    [InlineData(ChestSealState.Vented)] [InlineData(ChestSealState.Partial)]
    [InlineData(ChestSealState.Blocked)] [InlineData(ChestSealState.Detached)]
    public void ChestSealStates_AreExplicitAndPersisted(ChestSealState state)
    {
        var model = Create(); Assert.True(model.ApplyChestSeal(ThoracicSide.Left, state));
        Assert.Equal(state, model.Left.SealState);
    }

    private static ThoracicInjuryModel Create() => new(new HemorrhagePhysiologyModel());
}
