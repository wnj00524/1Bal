using System.Numerics;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Tactical;
using TacticalSim.Core.World;

namespace TacticalSim.Tests;

public sealed class TacticalIntegrationTests
{
    [Fact]
    public void CapabilityPolicyCentralizesCostsStabilityAndSevereGates()
    {
        var policy = new CapabilityActionPolicy();
        CapabilityState state = Capabilities((TacticalCapability.Movement, .5f), (TacticalCapability.Aiming, .25f),
            (TacticalCapability.Firing, .6f), (TacticalCapability.Reloading, .4f), (TacticalCapability.Communication, .1f));

        Assert.Equal(20f, policy.Evaluate(TacticalActionKind.Move, 10f, state).AdjustedTuCost);
        Assert.Equal(40f, policy.Evaluate(TacticalActionKind.Aim, 10f, state).AdjustedTuCost);
        Assert.Equal(.6f, policy.Evaluate(TacticalActionKind.Fire, 10f, state).Stability);
        Assert.Equal(25f, policy.Evaluate(TacticalActionKind.Reload, 10f, state).AdjustedTuCost);
        Assert.False(policy.Evaluate(TacticalActionKind.Command, 10f, state).IsAllowed);
    }

    [Fact]
    public void DragRequiresReachAndCapabilityAndMovesBothActors()
    {
        var world = new TacticalWorld(new WorldBounds(new(-20), new(20)));
        var rescuer = Actor(new(0, 0, 0)); var casualty = Actor(new(1, 0, 0));
        world.AddEntity(rescuer); world.AddEntity(casualty);
        var action = new CasualtyTransportAction(rescuer, casualty, world, new(5, 0, 0),
            CasualtyTransportMode.Drag, Capabilities());

        action.ExecutionProgress = action.TUCost / 2f;
        action.Execute(action.TUCost / 2f);
        Assert.Equal(new Vector3(2.5f, 0, 0), rescuer.Position);
        Assert.Equal(new Vector3(3.5f, 0, 0), casualty.Position);
        Assert.True(action.RescuerWeaponUseBlocked);
        Assert.Throws<InvalidOperationException>(() => new CasualtyTransportAction(rescuer, Actor(new(10, 0, 0)),
            world, Vector3.Zero, CasualtyTransportMode.Drag, Capabilities()));
    }

    [Fact]
    public void AiUsesObservableContextDeterministically()
    {
        var policy = new CasualtyBehaviorPolicy();
        var context = new CasualtyBehaviorContext(Capabilities((TacticalCapability.Posture, 0f)),
            CasualtyState.Incapacitated, UnderFire: true, HasCover: false, HasSelfAidEquipment: true,
            MissionRequiresHoldingPosition: false);
        Assert.Equal(CasualtyBehaviorState.CrawlingToSafety, policy.Decide(context));
        Assert.Equal(policy.Decide(context), policy.Decide(context));
    }

    [Fact]
    public void OverlayHidesDebugDetailsUnlessExplicitlyEnabled()
    {
        var factory = new CasualtyOverlayFactory();
        var ordinary = factory.Create(Capabilities(), CasualtyState.Effective, false, false,
            authoritativeDebugDetails: ["lesion: artery"]);
        var debug = factory.Create(Capabilities(), CasualtyState.Effective, false, false, true, ["lesion: artery"]);
        Assert.Empty(ordinary.DebugDetails);
        Assert.Equal("lesion: artery", Assert.Single(debug.DebugDetails));
        Assert.Equal(CasualtyOverlayStatus.Effective, ordinary.Status);
    }

    [Fact]
    public void TeammateResponseHonorsMissionAndUsesStableVisiblePriority()
    {
        Guid near = new("00000000-0000-0000-0000-000000000001");
        Guid calling = new("00000000-0000-0000-0000-000000000002");
        ObservableCasualty[] observations = [new(near, CasualtyOverlayStatus.Critical, 1f, false),
            new(calling, CasualtyOverlayStatus.Critical, 4f, true)];
        var policy = new TeammateResponsePolicy();
        Assert.Null(policy.SelectRescueTarget(observations, false));
        Assert.Equal(calling, policy.SelectRescueTarget(observations, true));
    }

    [Fact]
    public void ScoringKeepsMissionCasualtyRescueAndOpportunityCostsSeparate()
    {
        var score = new CasualtyScenarioScorer().Score(new(true, 2, 1, 3, 1, 20, 10, 2), new());
        Assert.Equal(100f, score.Mission);
        Assert.Equal(10f, score.Survival);
        Assert.Equal(30f, score.Neutralization);
        Assert.Equal(15f, score.Evacuation);
        Assert.Equal(-2f, score.Delay);
        Assert.Equal(-.5f, score.Exposure);
        Assert.Equal(-2f, score.Resources);
    }

    private static TacticalEntity Actor(Vector3 position) => new(position, new TacticalActorPhysiology());
    private static CapabilityState Capabilities(params (TacticalCapability Capability, float Value)[] overrides)
    {
        var values = Enum.GetValues<TacticalCapability>().ToDictionary(x => x, _ => 1f);
        foreach (var item in overrides) values[item.Capability] = item.Value;
        return new(values, []);
    }
}
