using System;

namespace TacticalSim.Core.Simulation.Actions
{
    /// <summary>
    /// Tactical action representing aiming at a target to accumulate precision bonuses over Time Units.
    /// </summary>
    public class AimTacticalAction : TacticalAction
    {
        public Guid TargetId { get; set; }
        public float MaxAimBonus { get; set; } = 1.0f;

        /// <summary>
        /// Current accumulated aim bonus scaled linearly with normalized progress.
        /// </summary>
        public float CurrentAimBonus => MaxAimBonus * NormalizedProgress;

        public AimTacticalAction()
        {
        }

        public AimTacticalAction(Guid actorId, Guid targetId, float tuCost, float maxAimBonus = 1.0f)
            : base(actorId, tuCost)
        {
            TargetId = targetId;
            MaxAimBonus = maxAimBonus;
        }

        public override void Execute(float dt)
        {
            // Aim bonus is dynamically calculated from NormalizedProgress
        }
    }
}
