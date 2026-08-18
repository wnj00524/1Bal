using System;

namespace TacticalSim.Core.Simulation.Actions
{
    /// <summary>
    /// A flexible tactical action backed by delegate callbacks for execution and lifecycle hooks.
    /// </summary>
    public class GenericTacticalAction : TacticalAction
    {
        public Action<float>? OnExecuteCallback { get; set; }
        public Action? OnStartCallback { get; set; }
        public Action? OnCompleteCallback { get; set; }
        public Action? OnCancelCallback { get; set; }
        public Action<Exception>? OnFailCallback { get; set; }

        public int ExecutionCount { get; private set; }
        public float TotalDeltaExecuted { get; private set; }

        public GenericTacticalAction()
        {
        }

        public GenericTacticalAction(
            Guid actorId,
            float tuCost,
            Action<float>? onExecute = null,
            Action? onStart = null,
            Action? onComplete = null,
            Action? onCancel = null,
            Action<Exception>? onFail = null)
            : base(actorId, tuCost)
        {
            OnExecuteCallback = onExecute;
            OnStartCallback = onStart;
            OnCompleteCallback = onComplete;
            OnCancelCallback = onCancel;
            OnFailCallback = onFail;
        }

        public override void Execute(float dt)
        {
            ExecutionCount++;
            TotalDeltaExecuted += dt;
            OnExecuteCallback?.Invoke(dt);
        }

        public override void OnStart()
        {
            base.OnStart();
            OnStartCallback?.Invoke();
        }

        public override void OnComplete()
        {
            base.OnComplete();
            OnCompleteCallback?.Invoke();
        }

        public override void OnCancel()
        {
            base.OnCancel();
            OnCancelCallback?.Invoke();
        }

        public override void OnFail(Exception ex)
        {
            base.OnFail(ex);
            OnFailCallback?.Invoke(ex);
        }
    }
}
