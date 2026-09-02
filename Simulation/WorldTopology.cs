namespace ProxyState.Simulation;

public sealed record WorldRoute(IReadOnlyList<int> LocationIds, int TravelMinutes);

public sealed class WorldTopology
{
    private readonly Dictionary<int, WorldLocationDefinition> _locationsByHash;
    private readonly Dictionary<(int From, int To), int> _travelMinutes;
    private readonly Dictionary<int, List<(int Destination, int TravelMinutes)>> _neighbors;

    internal WorldTopology(
        IReadOnlyList<WorldLocationDefinition> locations,
        IReadOnlyList<WorldConnectionDefinition> connections)
    {
        Locations = locations;
        Connections = connections;
        _locationsByHash = locations.ToDictionary(location => location.Hash);
        _travelMinutes = new Dictionary<(int From, int To), int>();
        _neighbors = locations.ToDictionary(location => location.Hash, _ => new List<(int, int)>());

        var locationsById = locations.ToDictionary(
            location => location.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections)
        {
            var from = locationsById[connection.From];
            var to = locationsById[connection.To];

            AddConnection(from.Hash, to.Hash, connection.TravelMinutes);
            AddConnection(to.Hash, from.Hash, connection.TravelMinutes);
        }

        foreach (var neighbors in _neighbors.Values)
        {
            // Stable ordering makes equal-cost shortest paths reproducible.
            neighbors.Sort((left, right) => left.Destination.CompareTo(right.Destination));
        }
    }

    public IReadOnlyList<WorldLocationDefinition> Locations { get; }
    public IReadOnlyList<WorldConnectionDefinition> Connections { get; }

    public IReadOnlyList<WorldLocationDefinition> GetLocationsByType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return Locations
            .Where(location => string.Equals(location.Type, type, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public WorldLocationDefinition GetLocation(int locationId)
    {
        return _locationsByHash.TryGetValue(locationId, out var location)
            ? location
            : throw new KeyNotFoundException($"Location {locationId} is not defined in the world topology.");
    }

    public int GetTravelMinutes(int fromLocationId, int toLocationId)
    {
        return _travelMinutes.TryGetValue((fromLocationId, toLocationId), out var minutes)
            ? minutes
            : throw new InvalidOperationException(
                $"No world connection exists from location {fromLocationId} to {toLocationId}.");
    }

    public WorldRoute? FindShortestRoute(int startLocationId, int destinationLocationId)
    {
        if (!_locationsByHash.ContainsKey(startLocationId) || !_locationsByHash.ContainsKey(destinationLocationId))
        {
            throw new KeyNotFoundException("A route endpoint is not defined in the world topology.");
        }

        return WorldPathfinder.FindShortestRoute(
            startLocationId,
            destinationLocationId,
            _locationsByHash.Keys,
            _neighbors);
    }

    private void AddConnection(int from, int to, int travelMinutes)
    {
        _travelMinutes[(from, to)] = travelMinutes;
        _neighbors[from].Add((to, travelMinutes));
    }
}
