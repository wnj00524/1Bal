using System;
using System.Numerics;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Simulation.Actions
{
    public class ShootTacticalAction : TacticalAction
    {
        private readonly IEntity _shooter;
        private readonly Vector3 _targetDirection;
        private readonly IEnvironmentModel _environment;
        
        // For testing/scaffolding, we can just expose the final projectile state
        public ProjectileState? FinalState { get; private set; }

        public ShootTacticalAction(
            IEntity shooter,
            Vector3 targetDirection,
            IEnvironmentModel environment) 
            : base(shooter.Id, shooter.EquippedWeapon?.BaseTUCostToFire ?? 15f)
        {
            _shooter = shooter;
            _targetDirection = Vector3.Normalize(targetDirection);
            _environment = environment;
        }

        public override void Execute(float dt)
        {
            if (State != TacticalActionState.Executing) return;

            ExecutionProgress += dt;

            // When action TU is fully consumed, the shot is fired.
            if (ExecutionProgress >= TUCost)
            {
                if (_shooter.EquippedWeapon?.LoadedAmmunition == null)
                {
                    throw new InvalidOperationException("Cannot shoot without a weapon or ammunition.");
                }

                var ammo = _shooter.EquippedWeapon.LoadedAmmunition;
                
                var currentState = new ProjectileState
                {
                    Position = _shooter.Position,
                    Velocity = _targetDirection * ammo.MuzzleVelocity,
                    Time = 0f
                };

                // For scaffolding, we just step it forward a bit (e.g. 1 second of flight)
                // In Vertical Slice, the world manager will handle collisions step by step.
                float simulationTimeStep = 0.01f;
                float maxFlightTime = 1.0f; 
                
                while (currentState.Time < maxFlightTime)
                {
                    currentState = BallisticSolver.StepRK4(currentState, ammo.Ballistics, _environment, simulationTimeStep);
                }
                
                FinalState = currentState;
                State = TacticalActionState.Completed;
            }
        }
    }
}
