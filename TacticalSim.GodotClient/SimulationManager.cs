using Godot;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Physiology;
using System.Numerics;

namespace TacticalSim.GodotClient
{
    public partial class SimulationManager : Node
    {
        private IServiceProvider _serviceProvider = null!;
        
        public TacticalEntity Shooter { get; private set; } = null!;
        public TacticalEntity Dummy { get; private set; } = null!;

        [Export] public NodePath BulletPath { get; set; } = null!;
        [Export] public NodePath VoxelRendererPath { get; set; } = null!;
        
        private MeshInstance3D _bulletMesh = null!;
        private VoxelRenderer _voxelRenderer = null!;

        public override void _Ready()
        {
            _bulletMesh = GetNode<MeshInstance3D>(BulletPath);
            _voxelRenderer = GetNode<VoxelRenderer>(VoxelRendererPath);

            InitializeDependencyInjection();
            
            // Initial scrub sets everything to t=0
            ScrubToTime(0.0f);
        }

        private void InitializeDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            _serviceProvider = services.BuildServiceProvider();
        }

        public void ScrubToTime(float flightTime)
        {
            // 1. Instantiate a fresh Dummy with full health
            var actorPhysiology = new DummyPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            Shooter = new TacticalEntity(new System.Numerics.Vector3(0, 1.5f, -10f), actorPhysiology);
            
            var ammo = new AmmunitionProfile
            {
                Name = "5.56x45mm NATO",
                MuzzleVelocity = 900f,
                Ballistics = new BallisticProfile
                {
                    Mass = 0.004f, 
                    CrossSectionalArea = 0.000024f,
                    DragModel = new StandardDragCurve(0.3f)
                }
            };

            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            Dummy = new TacticalEntity(new System.Numerics.Vector3(0, 1.0f, 0), dummyPhysiology);
            
            // 2. Setup initial bullet state right before impact
            // Dummy is at <0, 1.0, 0>. Torso center locally is <0, 0.25, 0>. So global torso center is <0, 1.25, 0>.
            System.Numerics.Vector3 globalTorsoCenter = Dummy.Position + new System.Numerics.Vector3(0, 0.25f, 0); 
            System.Numerics.Vector3 impactDir = System.Numerics.Vector3.Normalize(globalTorsoCenter - new System.Numerics.Vector3(0, globalTorsoCenter.Y, -10f));
            
            var impactState = new ProjectileState 
            {
                Position = globalTorsoCenter - (impactDir * 0.5f),
                Velocity = impactDir * (ammo.MuzzleVelocity * 0.9f),
                Time = 0f
            };

            var env = _serviceProvider.GetRequiredService<IEnvironmentModel>();
            
            // 3. Step physics until target time
            float simTimeStep = 0.00001f; // 10 microsecond steps to prevent voxel tunneling
            
            var cavEvents = new List<(float Time, CavitationEvent Cav)>();

            while (impactState.Time < flightTime)
            {
                // Advance flight path
                impactState = BallisticSolver.StepRK4(impactState, ammo.Ballistics, env, simTimeStep);
                
                // Process terminal ballistics against dummy
                foreach (var voxel in Dummy.Physiology.RootBodyPart.Voxels)
                {
                    // Convert to local space for voxel intersection
                    var localState = impactState;
                    localState.Position -= Dummy.Position;
                    
                    if (voxel.Contains(localState.Position))
                    {
                        float distanceThisStep = localState.Velocity.Length() * simTimeStep;
                        var cav = voxel.ProcessPenetrationStep(ref localState, ammo.Ballistics, distanceThisStep);
                        
                        // Sync state back
                        impactState.Velocity = localState.Velocity;

                        if (cav.HasValue)
                        {
                            cavEvents.Add((impactState.Time, cav.Value));
                        }
                        break; // Can only be in one voxel at a time
                    }
                }
            }

            // 4. Update the visual state
            _bulletMesh.Position = new Godot.Vector3(impactState.Position.X, impactState.Position.Y, impactState.Position.Z);
            
            // Look in the direction of velocity
            if (impactState.Velocity.LengthSquared() > 0)
            {
                var targetPt = impactState.Position + impactState.Velocity;
                _bulletMesh.LookAt(new Godot.Vector3(targetPt.X, targetPt.Y, targetPt.Z), Godot.Vector3.Up);
            }

            _voxelRenderer.RefreshVoxels(Dummy.Physiology);
            _voxelRenderer.DrawCavities(cavEvents, flightTime, Dummy.Position);
        }
    }
}
