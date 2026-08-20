using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;

namespace TacticalSim.Core.World;

/// <summary>In-memory bounded world for a single deterministic simulation.</summary>
public sealed class TacticalWorld : ITacticalWorld
{
    private readonly Dictionary<Guid, IEntity> _entities = new();
    private readonly List<CoverPolygon> _coverSurfaces = new();

    public TacticalWorld(WorldBounds bounds) => Bounds = bounds;

    public WorldBounds Bounds { get; }
    public event EventHandler<EntityEventArgs>? EntityAdded;
    public event EventHandler<EntityEventArgs>? EntityRemoved;

    public void AddEntity(IEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Id == Guid.Empty)
            throw new ArgumentException("Entity ID cannot be empty.", nameof(entity));

        entity.Position = Bounds.Clamp(entity.Position);
        _entities[entity.Id] = entity;
        EntityAdded?.Invoke(this, new EntityEventArgs(entity, 0f));
    }

    public bool RemoveEntity(Guid entityId)
    {
        if (!_entities.Remove(entityId, out IEntity? entity))
            return false;

        EntityRemoved?.Invoke(this, new EntityEventArgs(entity, 0f));
        return true;
    }

    public IEntity? GetEntity(Guid entityId) => _entities.GetValueOrDefault(entityId);

    public IReadOnlyCollection<IEntity> GetEntities() =>
        _entities.Values.OrderBy(entity => entity.Id).ToArray();

    public void SetEntityPosition(Guid entityId, Vector3 newPosition)
    {
        if (!_entities.TryGetValue(entityId, out IEntity? entity))
            throw new KeyNotFoundException($"No entity found with ID '{entityId}'.");

        entity.Position = Bounds.Clamp(newPosition);
    }

    public void AddCoverSurface(CoverPolygon cover)
    {
        ArgumentNullException.ThrowIfNull(cover);
        _coverSurfaces.Add(cover);
    }

    public IReadOnlyList<CoverPolygon> GetCoverSurfaces() => _coverSurfaces.AsReadOnly();
}
