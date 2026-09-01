namespace ProxyState.Simulation;

public static class WorldPathfinder
{
    public static WorldRoute? FindShortestRoute(
        int startLocationId,
        int destinationLocationId,
        IEnumerable<int> allLocationIds,
        IReadOnlyDictionary<int, List<(int Destination, int TravelMinutes)>> neighbors)
    {
        if (startLocationId == destinationLocationId)
        {
            return new WorldRoute(new[] { startLocationId }, 0);
        }

        var distances = allLocationIds.ToDictionary(locationId => locationId, _ => int.MaxValue);
        var previous = new Dictionary<int, int>();
        var unvisited = allLocationIds.ToHashSet();
        distances[startLocationId] = 0;

        while (unvisited.Count > 0)
        {
            int? current = null;
            foreach (var candidate in unvisited)
            {
                if (current is null || distances[candidate] < distances[current.Value] ||
                    (distances[candidate] == distances[current.Value] && candidate < current.Value))
                {
                    current = candidate;
                }
            }

            if (current is null || distances[current.Value] == int.MaxValue)
            {
                break;
            }

            unvisited.Remove(current.Value);
            if (current.Value == destinationLocationId)
            {
                break;
            }

            foreach (var neighbor in neighbors[current.Value])
            {
                if (!unvisited.Contains(neighbor.Destination))
                {
                    continue;
                }

                var candidateDistance = distances[current.Value] + neighbor.TravelMinutes;
                if (candidateDistance < distances[neighbor.Destination])
                {
                    distances[neighbor.Destination] = candidateDistance;
                    previous[neighbor.Destination] = current.Value;
                }
            }
        }

        if (!previous.ContainsKey(destinationLocationId))
        {
            return null;
        }

        var route = new List<int> { destinationLocationId };
        var cursor = destinationLocationId;
        while (cursor != startLocationId)
        {
            cursor = previous[cursor];
            route.Add(cursor);
        }

        route.Reverse();
        return new WorldRoute(route, distances[destinationLocationId]);
    }
}
