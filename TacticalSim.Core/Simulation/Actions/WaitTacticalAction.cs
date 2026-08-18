using System;

namespace TacticalSim.Core.Simulation.Actions
{
    /// <summary>
    /// Tactical action representing waiting or idling for a specified number of Time Units.
    /// </summary>
    public class WaitTacticalAction : TacticalAction
    {
        public WaitTacticalAction()
        {
        }

        public WaitTacticalAction(Guid actorId, float tuCost)
            : base(actorId, tuCost)
        {
        }

        public override void Execute(float dt)
        {
            // Idle wait - execution progress is handled by turn resolver
        }
    }
}
