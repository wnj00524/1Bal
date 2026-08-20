using Godot;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.World;
using System.Numerics;

namespace TacticalSim.GodotClient
{
    public partial class SimulationManager : Node
    {
        private const float WalkingSpeedMetersPerSecond = 1.4f;
        private IServiceProvider _serviceProvider = null!;
        
        public TacticalEntity Shooter { get; private set; } = null!;
        public TacticalEntity Dummy { get; private set; } = null!;

        [Export] public NodePath BulletPath { get; set; } = null!;
        [Export] public NodePath ScenarioRootPath { get; set; } = null!;
        [Export] public NodePath ShooterVisualPath { get; set; } = null!;
        [Export] public NodePath DummyVisualPath { get; set; } = null!;
        
        private MeshInstance3D _bulletMesh = null!;
        private Node3D _scenarioRoot = null!;
        private Node3D _shooterVisual = null!;
        private Node3D _dummyVisual = null!;
        private TacticalEntity? _focusedAgent;
        private Godot.Vector3? _pendingMoveDestination;

        public string? LoadedScenarioName { get; private set; }
        public float ElapsedScenarioTime { get; private set; }
        public bool IsScenarioLoaded => LoadedScenarioName != null;
        public bool HasBulletBeenFired { get; private set; }
        public bool IsTargetedShotPending { get; private set; }
        public ProjectileTerminationReason LastProjectileTermination { get; private set; }
        public TacticalEntity? FocusedAgent => _focusedAgent;
        public bool HasFocusedAgent => _focusedAgent != null;
        public bool HasPendingMove => _pendingMoveDestination.HasValue;

        public override void _Ready()
        {
            _bulletMesh = GetNode<MeshInstance3D>(BulletPath);
            _scenarioRoot = GetNode<Node3D>(ScenarioRootPath);
            _shooterVisual = GetNode<Node3D>(ShooterVisualPath);
            _dummyVisual = GetNode<Node3D>(DummyVisualPath);

            InitializeDependencyInjection();
            SetScenarioVisibility(false);
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

        public string ActiveTarget { get; set; } = "Chest";
        
        public float TargetDistance { get; private set; } = 10f;

        public void LoadScenario(string sceneName, AmmunitionProfile weapon, string target)
        {
            if (sceneName != "Training Dummy Outside")
                throw new ArgumentException($"Unknown scenario: {sceneName}", nameof(sceneName));

            ActiveAmmo = weapon ?? throw new ArgumentNullException(nameof(weapon));
            ActiveTarget = target;
            LoadedScenarioName = sceneName;
            ElapsedScenarioTime = 0f;
            HasBulletBeenFired = false;
            IsTargetedShotPending = false;
            LastProjectileTermination = ProjectileTerminationReason.None;
            ClearAgentFocus();

            PrepareScenario();
            SetScenarioVisibility(true);
        }

        public void AdvanceScenario(float seconds = 5f)
        {
            if (!IsScenarioLoaded)
                throw new InvalidOperationException("Load a scenario before advancing it.");
            if (seconds != 5f)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Scenarios advance in five-second increments.");

            ApplyPendingMovement(seconds);

            if (IsTargetedShotPending)
            {
                IsTargetedShotPending = false;
                HasBulletBeenFired = true;
                _bulletMesh.Visible = true;
                ScrubToTime(seconds);
            }

            Dummy.Physiology.TickPhysiology(seconds);
            ElapsedScenarioTime += seconds;
        }

        public void QueueTargetedShot(string target)
        {
            if (!IsScenarioLoaded)
                throw new InvalidOperationException("Load a scenario before selecting a shot target.");
            if (_focusedAgent == null)
                throw new InvalidOperationException("Select an agent before assigning a shot target.");
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("A target region is required.", nameof(target));

            ActiveTarget = target;
            IsTargetedShotPending = true;
        }

        public void UnloadScenario()
        {
            LoadedScenarioName = null;
            ElapsedScenarioTime = 0f;
            HasBulletBeenFired = false;
            IsTargetedShotPending = false;
            LastProjectileTermination = ProjectileTerminationReason.None;
            ClearAgentFocus();
            SetScenarioVisibility(false);
        }

        public string HandleLeftClick(Godot.Vector2 screenPosition)
        {
            if (!IsScenarioLoaded)
                return "No scenario loaded.";

            Camera3D? camera = GetViewport().GetCamera3D();
            if (camera == null)
                return "No active camera.";

            Godot.Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
            Godot.Vector3 rayEnd = rayOrigin + camera.ProjectRayNormal(screenPosition) * 1000f;
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
            Godot.Collections.Dictionary hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (!hit.TryGetValue("collider", out Variant colliderVariant))
                return "No agent or walkable position selected.";

            GodotObject? collider = colliderVariant.AsGodotObject();
            if (collider == _shooterVisual)
            {
                FocusAgent(Shooter);
                return "Shooter focused. Left click the ground to assign movement.";
            }

            if (collider == _dummyVisual)
            {
                FocusAgent(Dummy);
                return "Dummy focused. Left click the ground to assign movement.";
            }

            if (_focusedAgent == null)
                return "Select an agent before assigning a command.";

            Godot.Vector3 destination = hit["position"].AsVector3();
            _pendingMoveDestination = new Godot.Vector3(destination.X, 0.9f, destination.Z);
            return "Movement assigned. Advance the simulation to execute it.";
        }

        private void FocusAgent(TacticalEntity agent)
        {
            _focusedAgent = agent;
            _pendingMoveDestination = null;
        }

        private void ClearAgentFocus()
        {
            _focusedAgent = null;
            _pendingMoveDestination = null;
        }

        private void ApplyPendingMovement(float elapsedSeconds)
        {
            if (_focusedAgent == null || !_pendingMoveDestination.HasValue)
                return;

            Godot.Vector3 destination = _pendingMoveDestination.Value;
            var targetPosition = new System.Numerics.Vector3(
                destination.X, _focusedAgent.Position.Y, destination.Z);
            _focusedAgent.Position = MoveTacticalAction.AdvanceTowards(
                _focusedAgent.Position,
                targetPosition,
                WalkingSpeedMetersPerSecond,
                elapsedSeconds);

            Node3D visual = ReferenceEquals(_focusedAgent, Shooter) ? _shooterVisual : _dummyVisual;
            visual.Position = new Godot.Vector3(
                _focusedAgent.Position.X,
                destination.Y,
                _focusedAgent.Position.Z);

            if (_focusedAgent.Position == targetPosition)
                _pendingMoveDestination = null;
        }

        public bool IsDummyAtScreenPosition(Godot.Vector2 screenPosition)
        {
            if (!IsScenarioLoaded)
                return false;

            Camera3D? camera = GetViewport().GetCamera3D();
            if (camera == null)
                return false;

            Godot.Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
            Godot.Vector3 rayEnd = rayOrigin + camera.ProjectRayNormal(screenPosition) * 1000f;
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
            Godot.Collections.Dictionary hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);

            return hit.TryGetValue("collider", out Variant collider)
                && collider.AsGodotObject() == _dummyVisual;
        }

        private void SetScenarioVisibility(bool visible)
        {
            _scenarioRoot.Visible = visible;
            _shooterVisual.Visible = visible;
            _dummyVisual.Visible = visible;
            _bulletMesh.Visible = visible && HasBulletBeenFired;
        }

        private void PrepareScenario()
        {
            TargetDistance = ActiveAmmo.Name.Contains("Knife") ? 1f : 10f;

            var shooterPhysiology = new TacticalActorPhysiology();
            shooterPhysiology.SetRoot(new BodyPart { Type = BodyPartType.Thorax });
            Shooter = new TacticalEntity(
                new System.Numerics.Vector3(0, 1.5f, -TargetDistance),
                shooterPhysiology);
            Dummy = new TacticalEntity(
                new System.Numerics.Vector3(0, 1f, 0),
                AnatomicalDummyBuilder.BuildDummy());

            _shooterVisual.Position = new Godot.Vector3(0, 0.9f, -TargetDistance);
            _dummyVisual.Position = new Godot.Vector3(0, 0.9f, 0);
            _bulletMesh.Position = new Godot.Vector3(
                Shooter.Position.X,
                Shooter.Position.Y,
                Shooter.Position.Z);
        }

        private void ScrubToTime(float flightTime)
        {
            var ammo = ActiveAmmo;

            bool shooterWasFocused = ReferenceEquals(_focusedAgent, Shooter);
            bool dummyWasFocused = ReferenceEquals(_focusedAgent, Dummy);
            System.Numerics.Vector3 shooterPosition = Shooter.Position;
            System.Numerics.Vector3 dummyPosition = Dummy.Position;

            // 1. Instantiate a fresh Dummy with full health
            var actorPhysiology = new TacticalActorPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            Shooter = new TacticalEntity(shooterPosition, actorPhysiology);
            
            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            Dummy = new TacticalEntity(dummyPosition, dummyPhysiology);
            if (shooterWasFocused)
                _focusedAgent = Shooter;
            else if (dummyWasFocused)
                _focusedAgent = Dummy;
            
            // Move the visual shooter circle dynamically
            var shooterCircle = GetNodeOrNull<Godot.Node3D>("../ShooterCircle");
            if (shooterCircle != null)
            {
                shooterCircle.Position = new Godot.Vector3(
                    Shooter.Position.X,
                    0.9f,
                    Shooter.Position.Z);
            }
            
            // 2. Setup initial bullet state right before impact
            float aimXOffset = 0f;
            float aimYOffset = 0.25f; // Chest
            
            switch (ActiveTarget)
            {
                case "Head": aimYOffset = 0.76f; break;
                case "Neck": aimYOffset = 0.58f; break;
                case "Chest": aimYOffset = 0.25f; break;
                case "Abdomen": aimYOffset = 0.10f; break;
                case "Left Arm": aimXOffset = -0.3f; aimYOffset = 0.25f; break;
                case "Right Arm": aimXOffset = 0.3f; aimYOffset = 0.25f; break;
                case "Left Leg": aimXOffset = -0.1f; aimYOffset = -0.4f; break;
                case "Right Leg": aimXOffset = 0.1f; aimYOffset = -0.4f; break;
            }
            
            System.Numerics.Vector3 globalTargetCenter = Dummy.Position + new System.Numerics.Vector3(aimXOffset, aimYOffset, 0); 
            System.Numerics.Vector3 muzzlePoint = Shooter.Position + new System.Numerics.Vector3(aimXOffset, aimYOffset, 0);
            System.Numerics.Vector3 impactDir = System.Numerics.Vector3.Normalize(globalTargetCenter - muzzlePoint);
            
            var impactState = new ProjectileState 
            {
                Position = muzzlePoint, 
                Velocity = impactDir * ammo.MuzzleVelocity,
                Time = 0f
            };

            var allVoxels = GetAllVoxels(Dummy.Physiology.RootBodyPart);
            
            // Build spatial index for O(1) voxel lookup
            // Extent bounds mapped to grid coordinates: X[-50..50]->[0..100], Y[-100..120]->[0..220], Z[-50..50]->[0..100]
            var voxelGrid = new PhysiologicalVoxel[100, 220, 100]; 
            foreach (var v in allVoxels)
            {
                int vx = (int)MathF.Round(v.Center.X * 100f) + 50;
                int vy = (int)MathF.Round(v.Center.Y * 100f) + 100;
                int vz = (int)MathF.Round(v.Center.Z * 100f) + 50;
                if (vx >= 0 && vx < 100 && vy >= 0 && vy < 220 && vz >= 0 && vz < 100)
                    voxelGrid[vx, vy, vz] = v;
            }

            var cavEvents = new List<(float Time, CavitationEvent Cav)>();
            
            // 3. RK4 Physics Loop (simulating continuous flight until t = flightTime)
            var env = _serviceProvider.GetRequiredService<IEnvironmentModel>();
            WorldBounds worldBounds = WorldBounds.CreateDefault();
            
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
                
                LastProjectileTermination = ProjectileFlightTermination.Evaluate(
                    impactState, ammo.Ballistics, worldBounds);
                if (LastProjectileTermination != ProjectileTerminationReason.None)
                    break;

                // Update local position after step
                localPos = impactState.Position - Dummy.Position;

                // O(1) spatial grid lookup instead of looping 40,000 voxels
                if (isNearTarget)
                {
                    int bx = (int)MathF.Round(localPos.X * 100f) + 50;
                    int by = (int)MathF.Round(localPos.Y * 100f) + 100;
                    int bz = (int)MathF.Round(localPos.Z * 100f) + 50;
                    
                    if (bx >= 0 && bx < 100 && by >= 0 && by < 220 && bz >= 0 && bz < 100)
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
                int cx = (int)MathF.Round(cav.Origin.X * 100f) + 50;
                int cy = (int)MathF.Round(cav.Origin.Y * 100f) + 100;
                int cz = (int)MathF.Round(cav.Origin.Z * 100f) + 50;

                float cavVolume = (4f/3f) * MathF.PI * cav.Radius * cav.Radius * cav.Radius;
                float peakEnergyDensity = cavVolume > 0 ? 4f * (cav.Energy / cavVolume) : 0f;
                float voxelVolume = 0.01f * 0.01f * 0.01f;

                for (int x = cx - radCells; x <= cx + radCells; x++)
                {
                    for (int y = cy - radCells; y <= cy + radCells; y++)
                    {
                        for (int z = cz - radCells; z <= cz + radCells; z++)
                        {
                            if (x >= 0 && x < 100 && y >= 0 && y < 220 && z >= 0 && z < 100)
                            {
                                var neighbor = voxelGrid[x, y, z];
                                if (neighbor != null)
                                {
                                    float dist = (neighbor.Center - cav.Origin).Length();
                                    if (dist > 0 && dist <= cav.Radius)
                                    {
                                        float energyDensityAtDist = peakEnergyDensity * (1f - (dist / cav.Radius));
                                        float energyToVoxel = energyDensityAtDist * voxelVolume;
                                        neighbor.ApplyKineticEnergy(energyToVoxel, cav.Origin, 0f);
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
// Force Godot rebuild
