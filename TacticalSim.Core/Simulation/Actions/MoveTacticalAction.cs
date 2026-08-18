using System;
using System.Numerics;

namespace TacticalSim.Core.Simulation.Actions
{
    /// <summary>
    /// Tactical action representing entity movement in 3D space over Time Units.
    /// </summary>
    public class MoveTacticalAction : TacticalAction
    {
        public Vector3 StartPosition { get; set; }
        public Vector3 TargetPosition { get; set; }
        public Vector3 CurrentPosition { get; private set; }
        public float MovementSpeed { get; set; }

        public float Distance => Vector3.Distance(StartPosition, TargetPosition);

        public MoveTacticalAction()
        {
        }

        public MoveTacticalAction(Guid actorId, Vector3 startPosition, Vector3 targetPosition, float tuCost)
            : base(actorId, tuCost)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            MovementSpeed = tuCost > 0f ? Distance / tuCost : 0f;
        }

        public MoveTacticalAction(Guid actorId, Vector3 startPosition, Vector3 targetPosition, float movementSpeed, bool computeCostFromSpeed)
            : base(actorId, (computeCostFromSpeed && movementSpeed > 0f) ? MathF.Max(0.001f, Vector3.Distance(startPosition, targetPosition) / movementSpeed) : 1f)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            MovementSpeed = movementSpeed;
        }

        public override void Execute(float dt)
        {
            CurrentPosition = Vector3.Lerp(StartPosition, TargetPosition, NormalizedProgress);
        }

        public override void OnComplete()
        {
            base.OnComplete();
            CurrentPosition = TargetPosition;
        }
    }
}
