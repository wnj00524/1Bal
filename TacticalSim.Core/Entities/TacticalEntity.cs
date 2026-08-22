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
            : this(Guid.NewGuid(), position, physiology)
        {
        }

        /// <summary>
        /// Creates an entity with a recorded scenario/replay identifier. Scenario
        /// composition should use this overload so named random streams remain
        /// stable when the same scenario is reconstructed.
        /// </summary>
        public TacticalEntity(Guid id, Vector3 position, IActorPhysiology physiology)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("A tactical entity requires a non-empty identifier.", nameof(id));

            Id = id;
            Position = position;
            Physiology = physiology ?? throw new ArgumentNullException(nameof(physiology));
        }
    }
}
