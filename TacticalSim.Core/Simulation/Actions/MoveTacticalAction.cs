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
            float mobility = actor.Physiology.MobilityLevel;
            InitializeWithCost(startPosition, targetPosition, tuCost, mobility);
        }

        /// <summary>
        /// Creates a movement action when only the actor identifier is available.
        /// No physiological modifier is applied because no actor state was supplied.
        /// </summary>
        public MoveTacticalAction(Guid actorId, Vector3 startPosition, Vector3 targetPosition, float tuCost)
            : base(actorId, tuCost)
        {
            InitializeWithCost(startPosition, targetPosition, tuCost, mobility: 1f);
        }

        public MoveTacticalAction(IEntity actor, Vector3 startPosition, Vector3 targetPosition, float baseMovementSpeed, bool computeCostFromSpeed)
            : base(actor.Id, 1f)
        {
            float mobility = actor.Physiology.MobilityLevel;
            InitializeWithSpeed(startPosition, targetPosition, baseMovementSpeed, computeCostFromSpeed, mobility);
        }

        /// <summary>
        /// Creates a speed-based movement action when only the actor identifier is available.
        /// </summary>
        public MoveTacticalAction(Guid actorId, Vector3 startPosition, Vector3 targetPosition, float movementSpeed, bool computeCostFromSpeed)
            : base(actorId, 1f)
        {
            InitializeWithSpeed(startPosition, targetPosition, movementSpeed, computeCostFromSpeed, mobility: 1f);
        }

        private void InitializeWithCost(Vector3 startPosition, Vector3 targetPosition, float tuCost, float mobility)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            MovementSpeed = tuCost > 0f ? Distance / tuCost * mobility : 0f;
        }

        private void InitializeWithSpeed(
            Vector3 startPosition,
            Vector3 targetPosition,
            float movementSpeed,
            bool computeCostFromSpeed,
            float mobility)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            CurrentPosition = startPosition;
            MovementSpeed = movementSpeed * mobility;
            TUCost = computeCostFromSpeed && MovementSpeed > 0f
                ? MathF.Max(0.001f, Distance / MovementSpeed)
                : 1f;
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
