using Godot;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core;
using System.Numerics;

namespace TacticalSim.GodotClient
{
    public partial class SimulationManager : Node
    {
        private IServiceProvider _serviceProvider = null!;
        private ITurnResolver _turnResolver = null!;
        
        public TacticalEntity Shooter { get; private set; } = null!;
        public TacticalEntity Dummy { get; private set; } = null!;
        
        // Cache to store the bullet position at a given TU
        private List<(float Time, System.Numerics.Vector3 Position)> _trajectoryCache = new();

        public override void _Ready()
        {
            InitializeDependencyInjection();
            InitializeSimulationState();
            RunSimulationToCompletion();
        }

        private void InitializeDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            _serviceProvider = services.BuildServiceProvider();
            _turnResolver = _serviceProvider.GetRequiredService<ITurnResolver>();
        }

        private void InitializeSimulationState()
        {
            var actorPhysiology = new DummyPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            
            Shooter = new TacticalEntity(new System.Numerics.Vector3(0, 1.5f, -10f), actorPhysiology);
            Shooter.EquippedWeapon = new WeaponProfile
            {
                Name = "Rifle",
                BaseTUCostToFire = 15f,
                LoadedAmmunition = new AmmunitionProfile
                {
                    Name = "5.56x45mm NATO",
                    MuzzleVelocity = 900f,
                    Ballistics = new BallisticProfile
                    {
                        Mass = 0.004f, 
                        CrossSectionalArea = 0.000024f,
                        DragModel = new StandardDragCurve(0.3f)
                    }
                }
            };

            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            Dummy = new TacticalEntity(new System.Numerics.Vector3(0, 1.0f, 0), dummyPhysiology);
        }

        private void RunSimulationToCompletion()
        {
            var targetDir = System.Numerics.Vector3.Normalize(Dummy.Position - Shooter.Position);
            var env = _serviceProvider.GetRequiredService<IEnvironmentModel>();
            var shootAction = new TacticalSim.Core.Simulation.Actions.ShootTacticalAction(Shooter, targetDir, env);
            
            _turnResolver.ScheduleAction(shootAction);

            // Run until complete
            while (_turnResolver.HasActiveActions)
            {
                _turnResolver.Tick(1.0f);
            }
            
            // In a full implementation, we'd capture the timeline snapshots here
            // For now, the dummy has accumulated the end-state damage
        }
    }
}
