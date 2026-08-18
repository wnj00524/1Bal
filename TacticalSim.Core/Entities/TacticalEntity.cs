using System;
using System.Numerics;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core.Entities
{
    public class TacticalEntity : IEntity
    {
        public Guid Id { get; }
        public Vector3 Position { get; set; }
        public IActorPhysiology Physiology { get; }
        public WeaponProfile? EquippedWeapon { get; set; }

        public TacticalEntity(Vector3 position, IActorPhysiology physiology)
        {
            Id = Guid.NewGuid();
            Position = position;
            Physiology = physiology;
        }
    }
}
