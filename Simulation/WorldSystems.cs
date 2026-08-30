using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace ProxyState.Simulation;

public sealed class WorldClockSystem : QuerySystem<WorldTime>
{
    private readonly Entity _clockEntity;
    private double _pendingRealSeconds;

    public WorldClockSystem(EntityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var clocks = store.Query<WorldTime>().Entities;
        if (clocks.Count > 1)
        {
            throw new InvalidOperationException("The world must contain exactly one WorldTime singleton.");
        }

        _clockEntity = clocks.Count == 1
            ? clocks.First()
            : store.CreateEntity(new WorldTime());
    }

    public Entity ClockEntity => _clockEntity;

    public void Advance(double realElapsedSeconds)
    {
        if (!double.IsFinite(realElapsedSeconds) || realElapsedSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(realElapsedSeconds), "Elapsed time must be finite and non-negative.");
        }

        _pendingRealSeconds += realElapsedSeconds;
    }

    protected override void OnUpdate()
    {
        var simulationSeconds = _pendingRealSeconds *
            (SimulationDefaults.SimulationSecondsPerDay / SimulationDefaults.RealSecondsPerSimulationDay);
        _pendingRealSeconds = 0d;

        Query.ForEachEntity((ref WorldTime time, Entity _) =>
        {
            time.DeltaSimulationSeconds = simulationSeconds;
            time.ElapsedSimulationSeconds += simulationSeconds;
        });
    }
}

public sealed class CommutingSystem : QuerySystem<AgentLocation, AgentTravel, IntentionState, ActivityState, DecisionState>
{
    private readonly WorldTopology _world;
    private readonly Entity _clockEntity;
    private readonly int _workActionHash;
    private readonly int _restActionHash;
    private readonly int _socializeActionHash;

    public CommutingSystem(ContentCatalog catalog, Entity clockEntity)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _world = catalog.World;
        _clockEntity = clockEntity;
        _workActionHash = catalog.Actions.First(action =>
            string.Equals(action.Id, "work", StringComparison.OrdinalIgnoreCase)).Hash;
        _restActionHash = catalog.Actions.First(action =>
            string.Equals(action.Id, "rest", StringComparison.OrdinalIgnoreCase)).Hash;
        _socializeActionHash = catalog.Actions.First(action =>
            string.Equals(action.Id, "socialize", StringComparison.OrdinalIgnoreCase)).Hash;
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var time = _clockEntity.GetComponent<WorldTime>();
        var elapsedMinutes = time.DeltaSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute;
        if (elapsedMinutes <= 0d)
        {
            return;
        }

        Query.ForEachEntity((
            ref AgentLocation location,
            ref AgentTravel travel,
            ref IntentionState intention,
            ref ActivityState activity,
            ref DecisionState decision,
            Entity _) =>
        {
            UpdateAgent(
                ref location,
                ref travel,
                ref intention,
                ref activity,
                ref decision,
                (long)Math.Floor(time.ElapsedSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute),
                elapsedMinutes);
        });
    }

    private void UpdateAgent(
        ref AgentLocation location,
        ref AgentTravel travel,
        ref IntentionState intention,
        ref ActivityState activity,
        ref DecisionState decision,
        long minute,
        double elapsedMinutes)
    {
        if (travel.Mode is AgentTravelMode.TravellingToWork or AgentTravelMode.TravellingHome)
        {
            if (AdvanceTravel(ref location, ref travel, elapsedMinutes)) decision.Dirty = true;
        }

        if (intention.ActionHash == _restActionHash && travel.Mode == AgentTravelMode.AtWork)
        {
            BeginTravelHome(ref location, ref travel);
        }
        else if (intention.ActionHash == _workActionHash && travel.Mode == AgentTravelMode.AtHome)
        {
            BeginTravelToWork(ref location, ref travel);
        }

        var kind = travel.Mode is AgentTravelMode.TravellingHome or AgentTravelMode.TravellingToWork
            ? ActivityKind.Commuting
            : intention.ActionHash == _workActionHash && travel.Mode == AgentTravelMode.AtWork
                ? ActivityKind.Working
                : intention.ActionHash == _socializeActionHash
                    ? ActivityKind.Socializing
                    : intention.ActionHash == _restActionHash && travel.Mode == AgentTravelMode.AtHome
                        ? ActivityKind.Resting
                        : ActivityKind.Idle;
        if (activity.Kind != kind || activity.CurrentActionHash != intention.ActionHash)
        {
            activity.Kind = kind;
            activity.CurrentActionHash = intention.ActionHash;
            activity.StartedAtMinute = minute;
        }
    }

    private void BeginTravelToWork(ref AgentLocation location, ref AgentTravel travel)
    {
        if (travel.RouteLocationIds.Length == 1)
        {
            location.CurrentLocationId = location.WorkLocationId;
            travel.Mode = AgentTravelMode.AtWork;
            travel.RemainingTravelMinutes = 0f;
            return;
        }

        travel.Mode = AgentTravelMode.TravellingToWork;
        travel.RoutePosition = 0;
        travel.RemainingTravelMinutes = _world.GetTravelMinutes(
            travel.RouteLocationIds[0],
            travel.RouteLocationIds[1]);
    }

    private void BeginTravelHome(ref AgentLocation location, ref AgentTravel travel)
    {
        if (travel.RouteLocationIds.Length == 1)
        {
            location.CurrentLocationId = location.HomeLocationId;
            travel.Mode = AgentTravelMode.AtHome;
            travel.RemainingTravelMinutes = 0f;
            return;
        }

        travel.Mode = AgentTravelMode.TravellingHome;
        travel.RoutePosition = travel.RouteLocationIds.Length - 1;
        travel.RemainingTravelMinutes = _world.GetTravelMinutes(
            travel.RouteLocationIds[^1],
            travel.RouteLocationIds[^2]);
    }

    private bool AdvanceTravel(
        ref AgentLocation location,
        ref AgentTravel travel,
        double elapsedMinutes)
    {
        var arrivedAtDestination = false;
        while (elapsedMinutes > 0d &&
               travel.Mode is AgentTravelMode.TravellingToWork or AgentTravelMode.TravellingHome)
        {
            var nextPosition = travel.Mode == AgentTravelMode.TravellingToWork
                ? travel.RoutePosition + 1
                : travel.RoutePosition - 1;

            if (travel.RemainingTravelMinutes > elapsedMinutes)
            {
                travel.RemainingTravelMinutes -= (float)elapsedMinutes;
                break;
            }

            elapsedMinutes -= travel.RemainingTravelMinutes;
            travel.RoutePosition = nextPosition;
            location.CurrentLocationId = travel.RouteLocationIds[nextPosition];

            var arrived = travel.Mode == AgentTravelMode.TravellingToWork
                ? nextPosition == travel.RouteLocationIds.Length - 1
                : nextPosition == 0;
            if (arrived)
            {
                travel.RemainingTravelMinutes = 0f;
                travel.Mode = travel.Mode == AgentTravelMode.TravellingToWork
                    ? AgentTravelMode.AtWork
                    : AgentTravelMode.AtHome;
                arrivedAtDestination = true;
                continue;
            }

            var followingPosition = travel.Mode == AgentTravelMode.TravellingToWork
                ? nextPosition + 1
                : nextPosition - 1;
            travel.RemainingTravelMinutes = _world.GetTravelMinutes(
                travel.RouteLocationIds[nextPosition],
                travel.RouteLocationIds[followingPosition]);
        }
        return arrivedAtDestination;
    }
}
