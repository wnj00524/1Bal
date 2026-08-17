using System;
using System.Collections.Generic;

namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Represents an action that consumes Time Units (TUs) within the simulation.
    /// </summary>
    public abstract class TacticalAction
    {
        public Guid ActorId { get; set; }
        public float TUCost { get; set; }
        public float ExecutionProgress { get; set; }
        
        /// <summary>
        /// Advances the action execution by fractionated timesteps.
        /// </summary>
        public abstract void Execute(float dt);
        public bool IsComplete => ExecutionProgress >= TUCost;
    }

    /// <summary>
    /// Interface for managing the Simultaneous Turn Resolution system.
    /// </summary>
    public interface ITurnResolver
    {
        /// <summary>
        /// Current global time in the simulation.
        /// </summary>
        float GlobalTime { get; }

        /// <summary>
        /// Schedules an action for an actor.
        /// </summary>
        void ScheduleAction(TacticalAction action);

        /// <summary>
        /// Advances the simulation by a fractionated timestep, executing all concurrent actions.
        /// </summary>
        void Tick(float dt);
    }
}
