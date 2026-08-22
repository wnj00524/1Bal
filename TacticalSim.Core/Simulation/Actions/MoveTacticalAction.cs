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

        /// <summary>
        /// Advances a position toward a destination using SI units without overshooting it.
        /// </summary>
        public static Vector3 AdvanceTowards(
            Vector3 currentPosition,
            Vector3 targetPosition,
            float movementSpeedMetersPerSecond,
            float elapsedSeconds)
        {
            if (!float.IsFinite(movementSpeedMetersPerSecond) || movementSpeedMetersPerSecond < 0f)
                throw new ArgumentOutOfRangeException(nameof(movementSpeedMetersPerSecond));
            if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

            Vector3 displacement = targetPosition - currentPosition;
            float remainingDistance = displacement.Length();
            float travelDistance = movementSpeedMetersPerSecond * elapsedSeconds;

            if (remainingDistance == 0f || travelDistance >= remainingDistance)
                return targetPosition;

            return currentPosition + displacement / remainingDistance * travelDistance;
        }

        public MoveTacticalAction()
        {
        }

        /// <summary>
        /// Creates movement from a healthy-actor TU cost, scaling traversal time
        /// by the actor's current mobility capacity.
        /// </summary>
        public MoveTacticalAction(IEntity actor, Vector3 startPosition, Vector3 targetPosition, float tuCost)
            : base(actor.Id, tuCost)
        {
            float mobility = actor.Physiology.MobilityLevel;
            EnsureActorCanMove(startPosition, targetPosition, mobility);
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

        /// <summary>
        /// Creates movement from a healthy-actor base speed, applying the actor's
        /// current mobility capacity before optionally computing TU cost.
        /// </summary>
        public MoveTacticalAction(IEntity actor, Vector3 startPosition, Vector3 targetPosition, float baseMovementSpeed, bool computeCostFromSpeed)
            : base(actor.Id, 1f)
        {
            float mobility = actor.Physiology.MobilityLevel;
            EnsureActorCanMove(startPosition, targetPosition, mobility);
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
            TUCost = mobility > 0f ? tuCost / mobility : tuCost;
            MovementSpeed = TUCost > 0f ? Distance / TUCost : 0f;
        }

        private static void EnsureActorCanMove(
            Vector3 startPosition,
            Vector3 targetPosition,
            float mobility)
        {
            if (mobility <= 0f && startPosition != targetPosition)
            {
                throw new InvalidOperationException(
                    "The actor cannot start a movement action with zero mobility.");
            }
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
