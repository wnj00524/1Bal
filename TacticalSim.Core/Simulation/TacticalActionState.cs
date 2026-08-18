namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Represents the execution state of a tactical action within the turn resolver.
    /// </summary>
    public enum TacticalActionState
    {
        /// <summary>
        /// Action is registered/enqueued and awaiting execution.
        /// </summary>
        Pending,

        /// <summary>
        /// Action is actively executing during simulation ticks.
        /// </summary>
        Executing,

        /// <summary>
        /// Action has completed its required Time Unit (TU) cost.
        /// </summary>
        Completed,

        /// <summary>
        /// Action was cancelled before reaching completion.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Action execution threw an exception and failed.
        /// </summary>
        Failed
    }
}
