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
        
        private MeshInstance3D _bulletMesh = null!;

        public override void _Ready()
        {
            _bulletMesh = GetNode<MeshInstance3D>(BulletPath);

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

        public float TargetDistance { get; private set; } = 10f;

        public void ScrubToTime(float flightTime)
        {
            var ammo = ActiveAmmo;
            
            TargetDistance = ammo.Name.Contains("Knife") ? 1.0f : 10.0f;

            // 1. Instantiate a fresh Dummy with full health
            var actorPhysiology = new TacticalActorPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            Shooter = new TacticalEntity(new System.Numerics.Vector3(0, 1.5f, -TargetDistance), actorPhysiology);
            
            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            Dummy = new TacticalEntity(new System.Numerics.Vector3(0, 1.0f, 0), dummyPhysiology);
            
            // Move the visual shooter circle dynamically
            var shooterCircle = GetNodeOrNull<Godot.Node3D>("../ShooterCircle");
            if (shooterCircle != null)
            {
                shooterCircle.Position = new Godot.Vector3(0, 0, -TargetDistance);
            }
            
            // 2. Setup initial bullet state right before impact
            float aimYOffset = 0.25f; // Chest
            if (ammo.Name.Contains("Abdomen")) aimYOffset = 0.10f;
            else if (ammo.Name.Contains("Neck")) aimYOffset = 0.58f;
            else if (ammo.Name.Contains("Head")) aimYOffset = 0.76f;
            
            System.Numerics.Vector3 globalTorsoCenter = Dummy.Position + new System.Numerics.Vector3(0, aimYOffset, 0); 
            System.Numerics.Vector3 muzzlePoint = Shooter.Position + new System.Numerics.Vector3(0, aimYOffset, 0);
            System.Numerics.Vector3 impactDir = System.Numerics.Vector3.Normalize(globalTorsoCenter - muzzlePoint);
            
            var impactState = new ProjectileState 
            {
                Position = muzzlePoint, 
                Velocity = impactDir * ammo.MuzzleVelocity,
                Time = 0f
            };

            var allVoxels = GetAllVoxels(Dummy.Physiology.RootBodyPart);
            
            // Build spatial index for O(1) voxel lookup
            // Extent bounds mapped to grid coordinates: X[-30..30]->[0..60], Y[0..100]->[0..100], Z[-30..30]->[0..60]
            var voxelGrid = new PhysiologicalVoxel[60, 100, 60]; 
            foreach (var v in allVoxels)
            {
                int vx = (int)MathF.Round(v.Center.X * 100f) + 30;
                int vy = (int)MathF.Round(v.Center.Y * 100f);
                int vz = (int)MathF.Round(v.Center.Z * 100f) + 30;
                if (vx >= 0 && vx < 60 && vy >= 0 && vy < 100 && vz >= 0 && vz < 60)
                    voxelGrid[vx, vy, vz] = v;
            }

            var cavEvents = new List<(float Time, CavitationEvent Cav)>();
            
            // 3. RK4 Physics Loop (simulating continuous flight until t = flightTime)
            var env = _serviceProvider.GetRequiredService<IEnvironmentModel>();
            
            while (impactState.Time < flightTime)
            {
                // Fast distance check to optimize time step and voxel collision checks
                var localPos = impactState.Position - Dummy.Position;
                bool isNearTarget = localPos.LengthSquared() < 4.0f; // Within 2 meters of dummy
                
                // 10 microseconds for extremely precise physics near target, 1 millisecond in the air
                float simTimeStep = isNearTarget ? 0.00001f : 0.001f; 

                // Prevent overshooting the target flightTime scrubber
                if (impactState.Time + simTimeStep > flightTime)
                {
                    simTimeStep = flightTime - impactState.Time;
                }

                // Advance flight path
                impactState = BallisticSolver.StepRK4(impactState, ammo.Ballistics, env, simTimeStep);
                
                // Break if projectile has essentially stopped (kinetic energy depleted)
                if (impactState.Velocity.LengthSquared() < 0.01f) break;

                // Update local position after step
                localPos = impactState.Position - Dummy.Position;

                // O(1) spatial grid lookup instead of looping 40,000 voxels
                if (isNearTarget)
                {
                    int bx = (int)MathF.Round(localPos.X * 100f) + 30;
                    int by = (int)MathF.Round(localPos.Y * 100f);
                    int bz = (int)MathF.Round(localPos.Z * 100f) + 30;
                    
                    if (bx >= 0 && bx < 60 && by >= 0 && by < 100 && bz >= 0 && bz < 60)
                    {
                        var voxel = voxelGrid[bx, by, bz];
                        if (voxel != null && voxel.Contains(localPos))
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
                        }
                    }
                }
                
                // Backstop material interaction
                if (impactState.Position.Z >= 1.5f)
                {
                    impactState.Velocity = System.Numerics.Vector3.Zero;
                }
            }

            int destroyedCount = 0;
            foreach (var v in allVoxels) if (v.IsDestroyed) destroyedCount++;
            System.IO.File.WriteAllText("MedicalReport.txt", $"Knife Debug: Hit {destroyedCount} voxels. Final Pos: {impactState.Position}");
            
            // 4. Apply accumulated cavitation damage to surrounding tissue using spatial grid
            foreach (var cavEvent in cavEvents)
            {
                var cav = cavEvent.Cav;
                
                int radCells = (int)MathF.Ceiling(cav.Radius * 100f);
                int cx = (int)MathF.Round(cav.Origin.X * 100f) + 30;
                int cy = (int)MathF.Round(cav.Origin.Y * 100f);
                int cz = (int)MathF.Round(cav.Origin.Z * 100f) + 30;

                for (int x = cx - radCells; x <= cx + radCells; x++)
                {
                    for (int y = cy - radCells; y <= cy + radCells; y++)
                    {
                        for (int z = cz - radCells; z <= cz + radCells; z++)
                        {
                            if (x >= 0 && x < 60 && y >= 0 && y < 100 && z >= 0 && z < 60)
                            {
                                var neighbor = voxelGrid[x, y, z];
                                if (neighbor != null)
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
                        }
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
        private List<PhysiologicalVoxel> GetAllVoxels(TacticalSim.Core.Physiology.BodyPart root)
        {
            var list = new List<PhysiologicalVoxel>();
            list.AddRange(root.Voxels);
            foreach (var child in root.Children)
            {
                list.AddRange(GetAllVoxels(child));
            }
            return list;
        }
    }
}
