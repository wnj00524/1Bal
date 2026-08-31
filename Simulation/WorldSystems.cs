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

// Intent execution owns generic movement and performance mechanics. It never
// identifies an action by name or hash; executor definitions supply all semantics.
public sealed class IntentExecutionSystem : QuerySystem<AgentLocation, AgentTravel, IntentionState, ActivityState, DecisionState>
{
    private readonly EntityStore _store;
    private readonly WorldTopology _world;
    private readonly Entity _clockEntity;
    private readonly Dictionary<int, ExecutorKind> _executors;
    private readonly Dictionary<int, int> _activityTypes;

    public IntentExecutionSystem(EntityStore store, ContentCatalog catalog, Entity clockEntity)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(catalog);
        _world = catalog.World;
        _clockEntity = clockEntity;
        _executors = catalog.Intents.All.ToDictionary(intent => intent.Hash, intent => intent.Executor);
        _activityTypes = catalog.Intents.All.ToDictionary(intent => intent.Hash, intent => intent.Activity.Hash);
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        var time = _clockEntity.GetComponent<WorldTime>();
        var elapsedMinutes = time.DeltaSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute;
        if (elapsedMinutes <= 0d) return;

        // A snapshot avoids repeated ECS lookups and remains stable throughout
        // this execution tick when several agents target one another.
        var entityLocations = _store.Query<AgentLocation>().Entities.ToDictionary(
            entity => entity.Id, entity => entity.GetComponent<AgentLocation>().CurrentLocationId);
        var minute = (long)Math.Floor(time.ElapsedSimulationSeconds / SimulationDefaults.SimulationSecondsPerMinute);

        Query.ForEachEntity((ref AgentLocation location, ref AgentTravel travel,
            ref IntentionState intention, ref ActivityState activity,
            ref DecisionState decision, Entity _) =>
        {
            if (!_executors.TryGetValue(intention.ActionHash, out var executor))
            {
                decision.Dirty = true;
                SetActivity(ref activity, ActivityPhase.Blocked, 0, 0, minute);
                return;
            }

            var destination = ResolveDestination(executor, intention, location, entityLocations, ref decision);
            if (destination is null)
            {
                CancelTravel(ref travel);
                SetActivity(ref activity, ActivityPhase.Blocked, intention.ActionHash,
                    _activityTypes[intention.ActionHash], minute);
                return;
            }

            if (travel.Mode == AgentTravelMode.Travelling)
            {
                // Moving entity targets can invalidate the old route. Rebuild it
                // deterministically from the actor's current graph node.
                if (travel.DestinationLocationId != destination.Value)
                    BeginTravel(location.CurrentLocationId, destination.Value, ref travel, ref decision);
                if (travel.Mode == AgentTravelMode.Travelling &&
                    AdvanceTravel(ref location, ref travel, elapsedMinutes))
                    DecisionInvalidation.SignalLocation(ref decision);
            }

            if (location.CurrentLocationId != destination.Value)
            {
                if (travel.Mode != AgentTravelMode.Travelling)
                    BeginTravel(location.CurrentLocationId, destination.Value, ref travel, ref decision);
                SetActivity(ref activity, travel.Mode == AgentTravelMode.Travelling
                    ? ActivityPhase.Moving : ActivityPhase.Blocked, intention.ActionHash,
                    _activityTypes[intention.ActionHash], minute);
                return;
            }

            CancelTravel(ref travel);
            SetActivity(ref activity,
                executor == ExecutorKind.Wait ? ActivityPhase.Idle : ActivityPhase.Performing,
                intention.ActionHash, _activityTypes[intention.ActionHash], minute);
        });
    }

    private static int? ResolveDestination(ExecutorKind executor, IntentionState intention,
        AgentLocation location, IReadOnlyDictionary<int, int> entityLocations, ref DecisionState decision)
    {
        switch (executor)
        {
            case ExecutorKind.PerformHere:
            case ExecutorKind.Wait:
                return location.CurrentLocationId;
            case ExecutorKind.PerformAtLocation:
                if (intention.TargetLocationId != 0) return intention.TargetLocationId;
                DecisionInvalidation.SignalTargetAvailability(ref decision);
                return null;
            case ExecutorKind.PerformWithEntity:
                if (intention.TargetEntityId != 0 && entityLocations.TryGetValue(intention.TargetEntityId, out var targetLocation))
                    return targetLocation;
                DecisionInvalidation.SignalTargetAvailability(ref decision);
                return null;
            default:
                DecisionInvalidation.SignalTargetAvailability(ref decision);
                return null;
        }
    }

    private void BeginTravel(int start, int destination, ref AgentTravel travel, ref DecisionState decision)
    {
        var route = _world.FindShortestRoute(start, destination);
        if (route is null)
        {
            decision.Dirty = true;
            CancelTravel(ref travel);
            return;
        }
        travel.RouteLocationIds = route.LocationIds.ToArray();
        travel.TotalTravelMinutes = route.TravelMinutes;
        travel.RoutePosition = 0;
        travel.DestinationLocationId = destination;
        if (travel.RouteLocationIds.Length == 1)
        {
            travel.Mode = AgentTravelMode.Stationary;
            travel.RemainingTravelMinutes = 0f;
            return;
        }
        travel.Mode = AgentTravelMode.Travelling;
        travel.RemainingTravelMinutes = _world.GetTravelMinutes(travel.RouteLocationIds[0], travel.RouteLocationIds[1]);
    }

    private bool AdvanceTravel(ref AgentLocation location, ref AgentTravel travel, double elapsedMinutes)
    {
        var locationChanged = false;
        while (elapsedMinutes > 0d && travel.Mode == AgentTravelMode.Travelling)
        {
            if (travel.RemainingTravelMinutes > elapsedMinutes)
            {
                travel.RemainingTravelMinutes -= (float)elapsedMinutes;
                return locationChanged;
            }
            elapsedMinutes -= travel.RemainingTravelMinutes;
            travel.RoutePosition++;
            location.CurrentLocationId = travel.RouteLocationIds[travel.RoutePosition];
            locationChanged = true;
            if (travel.RoutePosition == travel.RouteLocationIds.Length - 1)
            {
                CancelTravel(ref travel);
                return locationChanged;
            }
            travel.RemainingTravelMinutes = _world.GetTravelMinutes(
                travel.RouteLocationIds[travel.RoutePosition], travel.RouteLocationIds[travel.RoutePosition + 1]);
        }
        return locationChanged;
    }

    private static void CancelTravel(ref AgentTravel travel)
    {
        travel.Mode = AgentTravelMode.Stationary;
        travel.RemainingTravelMinutes = 0f;
        travel.DestinationLocationId = 0;
    }

    private static void SetActivity(ref ActivityState activity, ActivityPhase phase,
        int actionHash, int activityTypeHash, long minute)
    {
        if (activity.Phase == phase && activity.ActionHash == actionHash &&
            activity.ActivityTypeHash == activityTypeHash) return;
        activity.Phase = phase;
        activity.ActionHash = actionHash;
        activity.ActivityTypeHash = activityTypeHash;
        activity.StartedAtMinute = minute;
    }
}
