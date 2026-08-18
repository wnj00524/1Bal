using System;

namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Event arguments for action lifecycle events (Scheduled, Started, Completed, Cancelled).
    /// </summary>
    public class ActionEventArgs : EventArgs
    {
        /// <summary>
        /// The tactical action associated with this event.
        /// </summary>
        public TacticalAction Action { get; }

        /// <summary>
        /// Global simulation time when this event occurred.
        /// </summary>
        public float GlobalTime { get; }

        public ActionEventArgs(TacticalAction action, float globalTime)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            GlobalTime = globalTime;
        }
    }

    /// <summary>
    /// Event arguments for action execution progression.
    /// </summary>
    public class ActionProgressEventArgs : EventArgs
    {
        /// <summary>
        /// The tactical action progressing.
        /// </summary>
        public TacticalAction Action { get; }

        /// <summary>
        /// Delta time elapsed in this progression step.
        /// </summary>
        public float DeltaTime { get; }

        /// <summary>
        /// Current accumulated execution progress in Time Units.
        /// </summary>
        public float CurrentProgress { get; }

        /// <summary>
        /// Total TU cost of the action.
        /// </summary>
        public float TotalCost { get; }

        /// <summary>
        /// Global simulation time after this step.
        /// </summary>
        public float GlobalTime { get; }

        public ActionProgressEventArgs(TacticalAction action, float deltaTime, float currentProgress, float totalCost, float globalTime)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            DeltaTime = deltaTime;
            CurrentProgress = currentProgress;
            TotalCost = totalCost;
            GlobalTime = globalTime;
        }
    }

    /// <summary>
    /// Event arguments when an action fails due to an exception during execution.
    /// </summary>
    public class ActionFailedEventArgs : EventArgs
    {
        /// <summary>
        /// The tactical action that failed.
        /// </summary>
        public TacticalAction Action { get; }

        /// <summary>
        /// The unhandled exception thrown during execution.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Error message describing the failure.
        /// </summary>
        public string ErrorMessage => Exception.Message;

        /// <summary>
        /// Global simulation time when the failure occurred.
        /// </summary>
        public float GlobalTime { get; }

        public ActionFailedEventArgs(TacticalAction action, Exception exception, float globalTime)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            GlobalTime = globalTime;
        }
    }

    /// <summary>
    /// Event arguments when the global simulation timeline advances.
    /// </summary>
    public class TimeAdvancedEventArgs : EventArgs
    {
        /// <summary>
        /// Delta time by which the simulation advanced.
        /// </summary>
        public float DeltaTime { get; }

        /// <summary>
        /// Global simulation time before this tick.
        /// </summary>
        public float PreviousGlobalTime { get; }

        /// <summary>
        /// Global simulation time after this tick.
        /// </summary>
        public float CurrentGlobalTime { get; }

        public TimeAdvancedEventArgs(float deltaTime, float previousGlobalTime, float currentGlobalTime)
        {
            DeltaTime = deltaTime;
            PreviousGlobalTime = previousGlobalTime;
            CurrentGlobalTime = currentGlobalTime;
        }
    }
}
