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
            : base(shooter?.Id ?? throw new ArgumentNullException(nameof(shooter)), 
                   (shooter.EquippedWeapon?.BaseTUCostToFire ?? 15f) * (1.0f + (shooter.Physiology.PainLevel * 1.5f)))
        {
            _shooter = shooter;
            _targetDirection = targetDirection.LengthSquared() > 0f ? Vector3.Normalize(targetDirection) : Vector3.UnitZ;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public override void Execute(float dt)
        {
            if (State == TacticalActionState.Cancelled || State == TacticalActionState.Failed)
            {
                return;
            }

            // When action TU is fully consumed, the shot is fired.
            if (IsComplete || ExecutionProgress >= TUCost)
            {
                FireShot();
            }
        }

        public override void OnComplete()
        {
            base.OnComplete();
            if (FinalState == null)
            {
                FireShot();
            }
        }

        private void FireShot()
        {
            if (FinalState != null)
            {
                return;
            }

            if (_shooter.EquippedWeapon?.LoadedAmmunition == null)
            {
                throw new InvalidOperationException("Cannot shoot without a weapon or ammunition.");
            }

            var ammo = _shooter.EquippedWeapon.LoadedAmmunition;
            
            // Flinch/pain drift logic
            Vector3 fireDir = _targetDirection;
            float pain = _shooter.Physiology.PainLevel;
            float shock = _shooter.Physiology.ShockLevel;
            float deviationFactor = pain + (shock * 0.5f);
            
            if (deviationFactor > 0.01f)
            {
                // Max pain/shock causes ~10 degrees of deviation
                float maxDeviationRadians = 10f * (MathF.PI / 180f) * deviationFactor;
                
                var random = new Random(_shooter.Id.GetHashCode()); // Deterministic random per actor
                float randomPitch = ((float)random.NextDouble() * 2f - 1f) * maxDeviationRadians;
                float randomYaw = ((float)random.NextDouble() * 2f - 1f) * maxDeviationRadians;
                
                // Construct a rotation and apply to the base direction
                var rotation = Quaternion.CreateFromYawPitchRoll(randomYaw, randomPitch, 0f);
                fireDir = Vector3.Transform(fireDir, rotation);
                fireDir = Vector3.Normalize(fireDir);
            }
            
            var currentState = new ProjectileState
            {
                Position = _shooter.Position,
                Velocity = fireDir * ammo.MuzzleVelocity,
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
        }
    }
}
