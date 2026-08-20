using System;
using System.Collections.Generic;

namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Interface for managing the Simultaneous Fractionated TU Turn Resolution system.
    /// </summary>
    public interface ITurnResolver
    {
        /// <summary>
        /// Current global timeline in the simulation.
        /// </summary>
        float GlobalTime { get; }

        /// <summary>
        /// Gets a value indicating whether there are active or queued actions in the resolver.
        /// </summary>
        bool HasActiveActions { get; }

        /// <summary>
        /// Gets the count of actors currently executing actions.
        /// </summary>
        int ActiveActorCount { get; }

        /// <summary>
        /// Schedules a tactical action for an actor. If the actor already has an active action,
        /// the action is queued in FIFO order for that actor.
        /// </summary>
        /// <param name="action">Tactical action to schedule.</param>
        void ScheduleAction(TacticalAction action);

        /// <summary>
        /// Cancels a specific action by ID (whether active or queued).
        /// If the cancelled action was active, promotes the actor's next queued action.
        /// </summary>
        /// <param name="actionId">Unique ID of the action to cancel.</param>
        /// <returns>True if the action was found and cancelled; otherwise false.</returns>
        bool CancelAction(Guid actionId);

        /// <summary>
        /// Cancels all active and queued actions for a given actor.
        /// </summary>
        /// <param name="actorId">Actor whose actions will be cancelled.</param>
        /// <returns>Number of actions cancelled.</returns>
        int CancelActorActions(Guid actorId);

        /// <summary>
        /// Gets all currently active actions across all actors.
        /// </summary>
        IReadOnlyList<TacticalAction> GetActiveActions();

        /// <summary>
        /// Gets all queued pending actions for a specific actor.
        /// </summary>
        /// <param name="actorId">Unique ID of the actor.</param>
        IReadOnlyList<TacticalAction> GetQueuedActions(Guid actorId);

        /// <summary>
        /// Gets the currently active action for a specific actor, or null if idle.
        /// </summary>
        /// <param name="actorId">Unique ID of the actor.</param>
        TacticalAction? GetCurrentAction(Guid actorId);

        /// <summary>
        /// Advances the simulation timeline by fractionated delta time dt, executing active actions
        /// and carrying over leftover time into queued actions within the same step.
        /// </summary>
        /// <param name="dt">Time step delta in TUs.</param>
        void Tick(float dt);

        /// <summary>
        /// Resets the resolver timeline to 0.0 and clears all active and queued actions.
        /// </summary>
        void Reset();

        /// <summary>
        /// Fired when an action is scheduled or queued.
        /// </summary>
        event EventHandler<ActionEventArgs>? ActionScheduled;

        /// <summary>
        /// Fired when an action transitions from Pending to Executing.
        /// </summary>
        event EventHandler<ActionEventArgs>? ActionStarted;

        /// <summary>
        /// Fired when an action progresses during a tick sub-step.
        /// </summary>
        event EventHandler<ActionProgressEventArgs>? ActionProgressed;

        /// <summary>
        /// Fired when an action completes its required TU cost.
        /// </summary>
        event EventHandler<ActionEventArgs>? ActionCompleted;

        /// <summary>
        /// Fired when an action is cancelled.
        /// </summary>
        event EventHandler<ActionEventArgs>? ActionCancelled;

        /// <summary>
        /// Fired when an action throws an unhandled exception during execution.
        /// </summary>
        event EventHandler<ActionFailedEventArgs>? ActionFailed;

        /// <summary>
        /// Fired when the global timeline advances by dt.
        /// </summary>
        event EventHandler<TimeAdvancedEventArgs>? TimeAdvanced;

    }
}
