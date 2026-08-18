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

        public AmmunitionProfile ActiveAmmo { get; set; } = new AmmunitionProfile
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

        public void ScrubToTime(float flightTime)
        {
            // 1. Instantiate a fresh Dummy with full health
            var actorPhysiology = new TacticalActorPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            Shooter = new TacticalEntity(new System.Numerics.Vector3(0, 1.5f, -10f), actorPhysiology);
            
            var ammo = ActiveAmmo;

            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            Dummy = new TacticalEntity(new System.Numerics.Vector3(0, 1.0f, 0), dummyPhysiology);
            
            // 2. Setup initial bullet state right before impact
            float aimYOffset = 0.25f; // Chest
            if (ammo.Name.Contains("Abdomen")) aimYOffset = 0.10f;
            else if (ammo.Name.Contains("Neck")) aimYOffset = 0.58f;
            else if (ammo.Name.Contains("Head")) aimYOffset = 0.76f;
            
            System.Numerics.Vector3 globalTorsoCenter = Dummy.Position + new System.Numerics.Vector3(0, aimYOffset, 0); 
            System.Numerics.Vector3 impactDir = System.Numerics.Vector3.Normalize(globalTorsoCenter - new System.Numerics.Vector3(0, globalTorsoCenter.Y, -10f));
            
            var impactState = new ProjectileState 
            {
                Position = globalTorsoCenter - (impactDir * 0.26f), // Start outside the 0.25 visual capsule
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
                
                // Fast voxel lookup using grid coordinates
                var localPos = impactState.Position - Dummy.Position;
                
                // 1cm grid, round to nearest cm
                int gridX = (int)MathF.Round(localPos.X * 100f);
                int gridY = (int)MathF.Round(localPos.Y * 100f);
                int gridZ = (int)MathF.Round(localPos.Z * 100f);
                
                // Look for voxel within 1cm
                foreach (var voxel in Dummy.Physiology.RootBodyPart.Voxels)
                {
                    if (voxel.Contains(localPos))
                    {
                        var localState = impactState;
                        localState.Position = localPos;
                        
                        float distanceThisStep = localState.Velocity.Length() * simTimeStep;
                        var cav = voxel.ProcessPenetrationStep(ref localState, ammo.Ballistics, distanceThisStep);
                        
                        impactState.Velocity = localState.Velocity;

                        if (cav.HasValue)
                        {
                            if (cavEvents.Count == 0 || (localPos - cavEvents[cavEvents.Count - 1].Cav.Origin).Length() > 0.01f)
                            {
                                cavEvents.Add((impactState.Time, cav.Value));
                            }
                            else
                            {
                                var last = cavEvents[cavEvents.Count - 1];
                                var modifiedCav = last.Cav;
                                modifiedCav.Energy += cav.Value.Energy;
                                modifiedCav.Radius = MathF.Max(modifiedCav.Radius, cav.Value.Radius);
                                cavEvents[cavEvents.Count - 1] = (last.Time, modifiedCav);
                            }
                        }
                        break; 
                    }
                }
                
                // Backstop material interaction
                if (impactState.Position.Z >= 1.5f)
                {
                    impactState.Velocity = System.Numerics.Vector3.Zero;
                }
            }

            int destroyedCount = 0;
            foreach (var v in Dummy.Physiology.RootBodyPart.Voxels) if (v.IsDestroyed) destroyedCount++;
            System.IO.File.WriteAllText("MedicalReport.txt", $"Knife Debug: Hit {destroyedCount} voxels. Final Pos: {impactState.Position}");
            
            // 4. Apply accumulated cavitation damage to surrounding tissue
            foreach (var cavEvent in cavEvents)
            {
                var cav = cavEvent.Cav;
                foreach (var neighbor in Dummy.Physiology.RootBodyPart.Voxels)
                {
                    float dist = (neighbor.Center - cav.Origin).Length();
                    if (dist > 0 && dist <= cav.Radius)
                    {
                        // Linear falloff for blast wave
                        float energyAtDist = cav.Energy * (1f - (dist / cav.Radius));
                        neighbor.ApplyKineticEnergy(energyAtDist, cav.Origin, 0f);
                    }
                }
            }
            
            var endPos = impactState.Position;
            _bulletMesh.Position = new Godot.Vector3(impactState.Position.X, impactState.Position.Y, impactState.Position.Z);
            
            // Look in the direction of velocity
            if (impactState.Velocity.LengthSquared() > 0)
            {
                var targetPt = impactState.Position + impactState.Velocity;
                _bulletMesh.LookAt(new Godot.Vector3(targetPt.X, targetPt.Y, targetPt.Z), Godot.Vector3.Up);
            }

            // We dispense with the heavy visual voxel animation, just show the bullet moving
            // _voxelRenderer.RefreshVoxels(Dummy.Physiology, cavEvents, flightTime, Dummy.Position);
        }
    }
}
