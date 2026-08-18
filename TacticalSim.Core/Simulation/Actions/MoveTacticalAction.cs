using System;
using System.Numerics;
using TacticalSim.Core.Entities;

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

        public MoveTacticalAction(IEntity actor, Vector3 startPosition, Vector3 targetPosition, float tuCost)
            : base(actor.Id, tuCost)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            
            float mobility = actor.Physiology.MobilityLevel;
            float baseSpeed = tuCost > 0f ? Distance / tuCost : 0f;
            MovementSpeed = baseSpeed * mobility;
        }

        public MoveTacticalAction(IEntity actor, Vector3 startPosition, Vector3 targetPosition, float baseMovementSpeed, bool computeCostFromSpeed)
            : base(actor.Id, 1f)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            
            float mobility = actor.Physiology.MobilityLevel;
            MovementSpeed = baseMovementSpeed * mobility;
            
            TUCost = (computeCostFromSpeed && MovementSpeed > 0f) ? MathF.Max(0.001f, Distance / MovementSpeed) : 1f;
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
