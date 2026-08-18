using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TacticalSim.Tests")]

namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Represents an action that consumes Time Units (TUs) within the tactical simulation.
    /// </summary>
    public abstract class TacticalAction
    {
        /// <summary>
        /// Unique identifier for this action instance.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Unique identifier of the actor performing this action.
        /// </summary>
        public Guid ActorId { get; set; }

        /// <summary>
        /// Total Time Units (TUs) required to complete this action.
        /// </summary>
        public float TUCost { get; set; }

        /// <summary>
        /// Current accumulated execution progress in Time Units (TUs).
        /// </summary>
        public float ExecutionProgress { get; set; }

        /// <summary>
        /// Current lifecycle state of the action.
        /// </summary>
        public TacticalActionState State { get; internal set; } = TacticalActionState.Pending;

        /// <summary>
        /// Simulation global time when this action started executing.
        /// </summary>
        public float StartTime { get; internal set; }

        /// <summary>
        /// Simulation global time when this action completed, or null if not completed.
        /// </summary>
        public float? CompletionTime { get; internal set; }

        /// <summary>
        /// Unhandled exception that caused the action to fail, or null if not failed.
        /// </summary>
        public Exception? FailureException { get; internal set; }

        /// <summary>
        /// Remaining Time Units (TUs) required to finish execution.
        /// </summary>
        public float RemainingTU => MathF.Max(0f, TUCost - ExecutionProgress);

        /// <summary>
        /// Normalized progress fraction between 0.0 and 1.0.
        /// </summary>
        public float NormalizedProgress => TUCost > 0f ? Math.Clamp(ExecutionProgress / TUCost, 0f, 1f) : 1f;

        /// <summary>
        /// Indicates whether the action has finished executing or completed.
        /// </summary>
        public bool IsComplete => State == TacticalActionState.Completed || ExecutionProgress >= TUCost;

        /// <summary>
        /// Initializes a new instance of <see cref="TacticalAction"/>.
        /// </summary>
        protected TacticalAction()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="TacticalAction"/> with specified actor ID and TU cost.
        /// </summary>
        /// <param name="actorId">Actor performing the action.</param>
        /// <param name="tuCost">Total TU cost.</param>
        protected TacticalAction(Guid actorId, float tuCost)
        {
            ActorId = actorId;
            TUCost = tuCost;
        }

        /// <summary>
        /// Advances the action execution by fractionated delta time units.
        /// </summary>
        /// <param name="dt">Time step delta in TUs.</param>
        public abstract void Execute(float dt);

        /// <summary>
        /// Hook invoked when the action starts executing.
        /// </summary>
        public virtual void OnStart() { }

        /// <summary>
        /// Hook invoked when the action completes execution.
        /// </summary>
        public virtual void OnComplete() { }

        /// <summary>
        /// Hook invoked when the action is cancelled.
        /// </summary>
        public virtual void OnCancel() { }

        /// <summary>
        /// Hook invoked when the action fails with an exception.
        /// </summary>
        /// <param name="ex">The exception thrown during execution.</param>
        public virtual void OnFail(Exception ex) { }
    }
}
