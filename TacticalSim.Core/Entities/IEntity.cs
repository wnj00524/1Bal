using System;
using System.Numerics;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core.Entities
{
    public interface IEntity
    {
        Guid Id { get; }
        Vector3 Position { get; set; }
        IActorPhysiology Physiology { get; }
        WeaponProfile? EquippedWeapon { get; set; }
    }
}
