using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Damage.Treatment;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.World;

namespace TacticalSim.Tests;

public sealed class TreatmentModelTests
{
    [Fact]
    public void TimedTourniquet_ConsumesEquipmentWhilePhysiologyContinues()
    {
        var physiology = Bleeding(); var inventory = Inventory(("tourniquet", 1)); var service = new TreatmentService();
        Guid provider = Guid.NewGuid(), casualty = Guid.NewGuid();
        var action = service.CreateTourniquet(provider, new(casualty, "artery"), "left-leg", LimbPlacementZone.Proximal, physiology, inventory, 5f);
        var resolver = Resolver(provider); resolver.ScheduleAction(action);
        float before = physiology.Blood.CirculatingMilliliters; physiology.Tick(2f); resolver.Tick(2f);
        Assert.Equal(TreatmentResult.InProgress, action.Result); Assert.True(physiology.Blood.CirculatingMilliliters < before);
        resolver.Tick(3f);
        Assert.Equal(TreatmentResult.Completed, action.Result); Assert.Equal(0, inventory.GetCount("tourniquet"));
        Assert.Equal(BleedingControlState.Tourniquet, physiology.Sources[0].ControlState);
        Assert.NotNull(action.Reassessment);
    }

    [Fact]
    public void PartialSecondTourniquet_ImprovesControlAndRecordsQuality()
    {
        var physiology = Bleeding(); var service = new TreatmentService(); var action = service.CreateTourniquet(Guid.NewGuid(),
            new(Guid.NewGuid(), "artery"), "right-arm", LimbPlacementZone.Proximal, physiology, Inventory(("tourniquet", 1)),
            quality: TreatmentApplicationQuality.Partial, secondDevice: true);
        Run(action); Assert.Equal(TreatmentResult.Completed, action.Result); Assert.Single(service.Trace);
    }

    [Fact]
    public void PackingRejectsInternalBleeding_AndWorksForAnyCompressibleRegion()
    {
        var internalModel = new HemorrhagePhysiologyModel(); internalModel.AddSource(new("internal", PressureRegime.Arterial, 5, false, BloodDestination.Peritoneal, false));
        var service = new TreatmentService();
        Assert.Throws<InvalidOperationException>(() => service.CreatePressureOrPacking(Guid.NewGuid(), new(Guid.NewGuid(), "internal", "pelvis"), internalModel, Inventory(("gauze", 1)), true));
        var action = service.CreatePressureOrPacking(Guid.NewGuid(), new(Guid.NewGuid(), "artery", "junction"), Bleeding(), Inventory(("gauze", 1)), true);
        Run(action); Assert.Equal(TreatmentResult.Completed, action.Result);
    }

    [Fact]
    public void InterruptedPressure_ReleasesProviderEquipmentAndControl()
    {
        var physiology = Bleeding(); var action = new TreatmentService().CreatePressureOrPacking(Guid.NewGuid(), new(Guid.NewGuid(), "artery"), physiology, Inventory(), false);
        var resolver = Resolver(action.ProviderId); resolver.ScheduleAction(action); resolver.Tick(2f);
        Assert.Equal(BleedingControlState.Compressed, physiology.Sources[0].ControlState);
        action.Interrupt(TreatmentInterruptionReason.Suppression); resolver.CancelAction(action.Id);
        Assert.Equal(TreatmentResult.Interrupted, action.Result); Assert.Equal(TreatmentInterruptionReason.Suppression, action.InterruptionReason);
        Assert.Equal(BleedingControlState.Uncontrolled, physiology.Sources[0].ControlState);
    }

    [Fact]
    public void MissingEquipment_FailsWithoutConsumingOrApplyingTreatment()
    {
        var physiology = Bleeding(); var action = new TreatmentService().CreateTourniquet(Guid.NewGuid(), new(Guid.NewGuid(), "artery"), "leg", LimbPlacementZone.Proximal, physiology, Inventory());
        Run(action); Assert.Equal(TacticalActionState.Failed, action.State); Assert.Equal(TreatmentResult.Failed, action.Result);
        Assert.Equal(BleedingControlState.Uncontrolled, physiology.Sources[0].ControlState);
    }

    [Fact]
    public void DebugQuickTreatment_MustBeExplicitAndIsTraced()
    {
        var service = new TreatmentService(); var target = new TreatmentTarget(Guid.NewGuid(), "artery");
        Assert.Throws<InvalidOperationException>(() => service.QuickApply(Guid.NewGuid(), target, TreatmentKind.Tourniquet, _ => TreatmentResult.Completed, false));
        Assert.Equal(TreatmentResult.Partial, service.QuickApply(Guid.NewGuid(), target, TreatmentKind.Tourniquet, _ => TreatmentResult.Partial, true));
        Assert.True(service.Trace.Single().IsDebug);
    }

    private static HemorrhagePhysiologyModel Bleeding() { var p = new HemorrhagePhysiologyModel(); p.AddSource(new("artery", PressureRegime.Arterial, 7, false, BloodDestination.External, true)); return p; }
    private static TreatmentInventory Inventory(params (string item, int count)[] items) => new(items.Select(x => KeyValuePair.Create(x.item, x.count)));
    private static TurnResolver Resolver(Guid actor) => new(new TacticalWorld(WorldBounds.CreateDefault()));
    private static void Run(TreatmentAction action) { var resolver = Resolver(action.ProviderId); resolver.ScheduleAction(action); resolver.Tick(action.TUCost); }
}
