using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;

namespace TacticalSim.Core.World;

/// <summary>The spatial authority for entities and static cover in a bounded simulation.</summary>
public interface ITacticalWorld
{
    WorldBounds Bounds { get; }
    void AddEntity(IEntity entity);
    bool RemoveEntity(Guid entityId);
    IEntity? GetEntity(Guid entityId);
    IReadOnlyCollection<IEntity> GetEntities();
    void SetEntityPosition(Guid entityId, Vector3 newPosition);
    void AddCoverSurface(CoverPolygon cover);
    IReadOnlyList<CoverPolygon> GetCoverSurfaces();
    event EventHandler<EntityEventArgs>? EntityAdded;
    event EventHandler<EntityEventArgs>? EntityRemoved;
}
