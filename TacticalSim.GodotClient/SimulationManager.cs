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
using System.Linq;

namespace TacticalSim.GodotClient
{
    public partial class SimulationManager : Node
    {
        private const float WalkingSpeedMetersPerSecond = 1.4f;
        private const float ChestWoundDestroyedLungFraction = 0.20f;
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
        private ProjectileState? _projectileState;
        private bool _isProjectileSelected;
        private MedicalAction? _pendingMedicalAction;
        private Node3D _penetrationVisualization = null!;

        public string? LoadedScenarioName { get; private set; }
        public float ElapsedScenarioTime { get; private set; }
        public bool IsScenarioLoaded => LoadedScenarioName != null;
        public bool HasBulletBeenFired { get; private set; }
        public bool IsTargetedShotPending { get; private set; }
        public ProjectileTerminationReason LastProjectileTermination { get; private set; }
        public TacticalEntity? FocusedAgent => _focusedAgent;
        public bool HasFocusedAgent => _focusedAgent != null;
        public bool HasPendingMove => _pendingMoveDestination.HasValue;
        public bool IsProjectileSelected => _isProjectileSelected;
        public bool HasCompletePenetration { get; private set; }
        public bool HasPendingMedicalAction => _pendingMedicalAction.HasValue;
        public bool HasMaterialHit { get; private set; }
        public System.Numerics.Vector3? PenetrationEntryPoint { get; private set; }
        public System.Numerics.Vector3? PenetrationExitPoint { get; private set; }
        public System.Numerics.Vector3? MaterialHitPoint { get; private set; }
        public ProjectileTelemetry? SelectedProjectileTelemetry =>
            _isProjectileSelected && _projectileState.HasValue
                ? ProjectileTelemetry.From(_projectileState.Value, ActiveAmmo.Ballistics)
                : null;

        public override void _Ready()
        {
            _bulletMesh = GetNode<MeshInstance3D>(BulletPath);
            _scenarioRoot = GetNode<Node3D>(ScenarioRootPath);
            _shooterVisual = GetNode<Node3D>(ShooterVisualPath);
            _dummyVisual = GetNode<Node3D>(DummyVisualPath);

            InitializeDependencyInjection();
            CreatePenetrationVisualization();
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
            if (sceneName != "Training Dummy Outside" && sceneName != "Chest Wound Response")
                throw new ArgumentException($"Unknown scenario: {sceneName}", nameof(sceneName));

            ActiveAmmo = weapon ?? throw new ArgumentNullException(nameof(weapon));
            ActiveTarget = target;
            LoadedScenarioName = sceneName;
            ElapsedScenarioTime = 0f;
            HasBulletBeenFired = false;
            IsTargetedShotPending = false;
            LastProjectileTermination = ProjectileTerminationReason.None;
            _projectileState = null;
            _isProjectileSelected = false;
            ClearPenetrationVisualization();
            ClearAgentFocus();
            _pendingMedicalAction = null;

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
            ApplyPendingMedicalAction();

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
            if (!ReferenceEquals(_focusedAgent, Shooter))
                throw new InvalidOperationException("Select the shooter before assigning a shot target.");
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
            _projectileState = null;
            _isProjectileSelected = false;
            ClearPenetrationVisualization();
            ClearAgentFocus();
            _pendingMedicalAction = null;
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
            if (HasBulletBeenFired && IsProjectileCollider(collider))
            {
                ClearAgentFocus();
                _isProjectileSelected = true;
                return "Projectile selected.";
            }

            if (collider == _shooterVisual)
            {
                FocusAgent(Shooter);
                return "Shooter focused. Right click the ground or a valid target for commands.";
            }

            if (collider == _dummyVisual)
            {
                FocusAgent(Dummy);
                return "Dummy focused. Right click the ground for commands.";
            }

            return _focusedAgent == null
                ? "Select an agent before assigning a command."
                : "Right click the ground and select Move.";
        }

        public bool TryGetMoveDestination(Godot.Vector2 screenPosition, out Godot.Vector3 destination)
        {
            destination = default;
            if (!IsScenarioLoaded || _focusedAgent == null)
                return false;

            Camera3D? camera = GetViewport().GetCamera3D();
            if (camera == null)
                return false;

            Godot.Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
            Godot.Vector3 rayEnd = rayOrigin + camera.ProjectRayNormal(screenPosition) * 1000f;
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
            Godot.Collections.Dictionary hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (!hit.TryGetValue("collider", out Variant colliderVariant)
                || colliderVariant.AsGodotObject() is not Node collider
                || collider.Name != "Ground")
                return false;

            Godot.Vector3 hitPosition = hit["position"].AsVector3();
            destination = new Godot.Vector3(hitPosition.X, 0.9f, hitPosition.Z);
            return true;
        }

        public void AssignMoveDestination(Godot.Vector3 destination)
        {
            if (_focusedAgent == null)
                throw new InvalidOperationException("Select an agent before assigning movement.");

            _pendingMoveDestination = destination;
        }

        private void FocusAgent(TacticalEntity agent)
        {
            _isProjectileSelected = false;
            _focusedAgent = agent;
            _pendingMoveDestination = null;
        }

        private bool IsProjectileCollider(GodotObject? collider)
        {
            return collider is Node node
                && (node == _bulletMesh || _bulletMesh.IsAncestorOf(node));
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

        public bool IsValidShotTargetAtScreenPosition(Godot.Vector2 screenPosition)
        {
            if (!IsScenarioLoaded || !ReferenceEquals(_focusedAgent, Shooter))
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

        public enum MedicalAction
        {
            ApplyChestSeal,
            NeedleDecompression,
            ApplyLeftArmTourniquet,
            ApplyRightArmTourniquet,
            ApplyLeftLegTourniquet,
            ApplyRightLegTourniquet,
            PackAbdominalWound
        }

        public IReadOnlyList<MedicalAction> GetAvailableMedicalActions()
        {
            var actions = new List<MedicalAction>();
            if (!IsScenarioLoaded || !ReferenceEquals(_focusedAgent, Shooter))
                return actions;

            bool hasOpenChestWound = GetAllVoxels(Dummy.Physiology.RootBodyPart)
                .Exists(voxel => voxel.Organ == OrganType.Lung && voxel.IsDestroyed);
            if (hasOpenChestWound && !Dummy.Physiology.HasChestSeal)
                actions.Add(MedicalAction.ApplyChestSeal);
            if (Dummy.Physiology.TensionPneumothoraxLevel > 0f)
                actions.Add(MedicalAction.NeedleDecompression);
            AddTourniquetIfApplicable(actions, BodyPartType.LeftArm, MedicalAction.ApplyLeftArmTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.RightArm, MedicalAction.ApplyRightArmTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.LeftLeg, MedicalAction.ApplyLeftLegTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.RightLeg, MedicalAction.ApplyRightLegTourniquet);

            BodyPart? abdomen = FindBodyPart(Dummy.Physiology.RootBodyPart, BodyPartType.Abdomen);
            if (abdomen != null && !abdomen.HasWoundPacking &&
                abdomen.Voxels.Exists(voxel => voxel.IsDestroyed && voxel.Organ == OrganType.Muscle))
                actions.Add(MedicalAction.PackAbdominalWound);
            return actions;
        }

        private void AddTourniquetIfApplicable(
            List<MedicalAction> actions, BodyPartType type, MedicalAction action)
        {
            BodyPart? part = FindBodyPart(Dummy.Physiology.RootBodyPart, type);
            if (part != null && !part.HasTourniquet && part.GetActiveBleedRate() > 0f)
                actions.Add(action);
        }

        private static BodyPart? FindBodyPart(BodyPart part, BodyPartType type)
        {
            if (part.Type == type)
                return part;
            foreach (BodyPart child in part.Children)
            {
                BodyPart? match = FindBodyPart(child, type);
                if (match != null)
                    return match;
            }
            return null;
        }

        public bool IsDummyAtScreenPosition(Godot.Vector2 screenPosition)
        {
            if (!IsScenarioLoaded)
                return false;

            Camera3D? camera = GetViewport().GetCamera3D();
            if (camera == null)
                return false;
            Godot.Vector3 origin = camera.ProjectRayOrigin(screenPosition);
            var query = PhysicsRayQueryParameters3D.Create(
                origin, origin + camera.ProjectRayNormal(screenPosition) * 1000f);
            var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            return hit.TryGetValue("collider", out Variant collider)
                && collider.AsGodotObject() == _dummyVisual;
        }

        public void QueueMedicalAction(MedicalAction action)
        {
            if (!GetAvailableMedicalActions().Contains(action))
                throw new InvalidOperationException("That treatment is not currently applicable to the dummy.");
            _pendingMedicalAction = action;
        }

        private void ApplyPendingMedicalAction()
        {
            if (!_pendingMedicalAction.HasValue)
                return;

            switch (_pendingMedicalAction)
            {
                case MedicalAction.ApplyChestSeal:
                    Dummy.Physiology.ApplyChestSeal();
                    break;
                case MedicalAction.NeedleDecompression:
                    Dummy.Physiology.PerformNeedleDecompression();
                    break;
                case MedicalAction.ApplyLeftArmTourniquet:
                    Dummy.Physiology.ApplyTourniquet(BodyPartType.LeftArm);
                    break;
                case MedicalAction.ApplyRightArmTourniquet:
                    Dummy.Physiology.ApplyTourniquet(BodyPartType.RightArm);
                    break;
                case MedicalAction.ApplyLeftLegTourniquet:
                    Dummy.Physiology.ApplyTourniquet(BodyPartType.LeftLeg);
                    break;
                case MedicalAction.ApplyRightLegTourniquet:
                    Dummy.Physiology.ApplyTourniquet(BodyPartType.RightLeg);
                    break;
                case MedicalAction.PackAbdominalWound:
                    Dummy.Physiology.PackExternalWound(BodyPartType.Abdomen);
                    break;
            }
            _pendingMedicalAction = null;
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

            if (LoadedScenarioName == "Chest Wound Response")
            {
                List<PhysiologicalVoxel> lungVoxels = GetAllVoxels(Dummy.Physiology.RootBodyPart)
                    .FindAll(voxel => voxel.Organ == OrganType.Lung);
                int woundedVoxels = Math.Max(1,
                    (int)MathF.Ceiling(lungVoxels.Count * ChestWoundDestroyedLungFraction));
                foreach (PhysiologicalVoxel lungVoxel in lungVoxels.Take(woundedVoxels))
                {
                    lungVoxel.ApplyKineticEnergy(1_000f, lungVoxel.Center, lungVoxel.Size * lungVoxel.Size * lungVoxel.Size);
                }
                Dummy.Physiology.TickPhysiology(1f);
            }

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
            // A loaded scenario owns the actors' persistent physiological state.
            // Rebuilding them for each projectile used to erase earlier wounds,
            // blood loss, and treatment, making the medical report describe only
            // the most recent hit. LoadScenario/PrepareScenario is the explicit
            // reset boundary; every shot within that scenario damages the same
            // actor so its systemic effects remain cumulative.
            
            // Move the visual shooter circle dynamically.
            var shooterCircle = GetNodeOrNull<Godot.Node3D>("../ShooterCircle");
            if (shooterCircle != null)
            {
                shooterCircle.Position = new Godot.Vector3(
                    Shooter.Position.X,
                    0.9f,
                    Shooter.Position.Z);
            }
            
            // Setup initial bullet state right before impact.
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
            System.Numerics.Vector3? firstMaterialContact = null;
            System.Numerics.Vector3? lastMaterialContact = null;
            
            // RK4 physics loop (simulating continuous flight until t = flightTime).
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
                System.Numerics.Vector3 stepStartPosition = impactState.Position;
                impactState = BallisticSolver.StepRK4(impactState, ammo.Ballistics, env, simTimeStep);

                // The building is a scene-level obstacle, so detect its actual
                // collision point rather than using a fixed world-space stopping plane.
                if (TryGetBuildingWallImpact(stepStartPosition, impactState.Position, out var wallContact))
                {
                    firstMaterialContact = wallContact;
                    lastMaterialContact = wallContact;
                    impactState.Position = wallContact;
                    impactState.Velocity = System.Numerics.Vector3.Zero;
                    break;
                }
                
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
                            firstMaterialContact ??= impactState.Position;
                            lastMaterialContact = impactState.Position;
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
                
            }

            int destroyedCount = 0;
            foreach (var v in allVoxels) if (v.IsDestroyed) destroyedCount++;
            System.IO.File.WriteAllText("MedicalReport.txt", $"Knife Debug: Hit {destroyedCount} voxels. Final Pos: {impactState.Position}");
            
            // Apply accumulated cavitation damage to surrounding tissue using spatial grid.
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
            
            // A material stop is reported at the material contact, not at the last
            // integration point. This keeps the rendered projectile attached to the
            // object that stopped it and keeps its telemetry at the terminal point.
            if (firstMaterialContact.HasValue
                && lastMaterialContact.HasValue
                && !HasTravelledBeyondMaterial(
                    impactState.Position,
                    lastMaterialContact.Value,
                    impactDir))
            {
                impactState.Position = firstMaterialContact.Value;
                impactState.Velocity = System.Numerics.Vector3.Zero;
            }

            _projectileState = impactState;
            UpdatePenetrationVisualization(
                firstMaterialContact,
                lastMaterialContact,
                impactState.Position,
                impactDir);
            _bulletMesh.GlobalPosition = ToGodot(impactState.Position);
            
            // Look in the direction of velocity
            if (impactState.Velocity.LengthSquared() > 0)
            {
                var targetPt = impactState.Position + impactState.Velocity;
                _bulletMesh.LookAt(new Godot.Vector3(targetPt.X, targetPt.Y, targetPt.Z), Godot.Vector3.Up);
            }

            // We dispense with the heavy visual voxel animation, just show the bullet moving
            // _voxelRenderer.RefreshVoxels(Dummy.Physiology, cavEvents, flightTime, Dummy.Position);
        }

        private void CreatePenetrationVisualization()
        {
            _penetrationVisualization = new Node3D { Name = "PenetrationVisualization" };
            AddChild(_penetrationVisualization);
        }

        private void ClearPenetrationVisualization()
        {
            HasCompletePenetration = false;
            HasMaterialHit = false;
            PenetrationEntryPoint = null;
            PenetrationExitPoint = null;
            MaterialHitPoint = null;

            if (_penetrationVisualization != null)
            {
                foreach (Node child in _penetrationVisualization.GetChildren())
                    child.QueueFree();
            }
        }

        private bool TryGetBuildingWallImpact(
            System.Numerics.Vector3 start,
            System.Numerics.Vector3 end,
            out System.Numerics.Vector3 contactPoint)
        {
            contactPoint = default;

            Godot.Vector3 rayStart = ToGodot(start);
            Godot.Vector3 rayEnd = ToGodot(end);
            if (rayStart.DistanceSquaredTo(rayEnd) <= 0.000001f)
                return false;

            var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
            Godot.Collections.Dictionary hit = _scenarioRoot.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (!hit.TryGetValue("collider", out Variant colliderVariant)
                || !IsBuildingWallCollider(colliderVariant.AsGodotObject())
                || !hit.TryGetValue("position", out Variant positionVariant))
            {
                return false;
            }

            Godot.Vector3 godotContact = positionVariant.AsVector3();
            contactPoint = new System.Numerics.Vector3(
                godotContact.X,
                godotContact.Y,
                godotContact.Z);
            return true;
        }

        private bool IsBuildingWallCollider(GodotObject? collider)
        {
            if (collider is not Node node)
                return false;

            for (Node? current = node; current != null; current = current.GetParent())
            {
                if (current == _scenarioRoot)
                    return false;
                if (current.Name == "BrickBuilding")
                    return true;
            }

            return false;
        }

        private void UpdatePenetrationVisualization(
            System.Numerics.Vector3? firstContact,
            System.Numerics.Vector3? lastContact,
            System.Numerics.Vector3 projectilePosition,
            System.Numerics.Vector3 direction)
        {
            ClearPenetrationVisualization();
            if (!firstContact.HasValue || !lastContact.HasValue)
                return;

            HasMaterialHit = true;

            if (!HasTravelledBeyondMaterial(projectilePosition, lastContact.Value, direction))
            {
                MaterialHitPoint = firstContact;
                AddPenetrationMarker(firstContact.Value, "HIT", new Color(1f, 0.55f, 0.05f));
                return;
            }

            HasCompletePenetration = true;
            PenetrationEntryPoint = firstContact;
            PenetrationExitPoint = lastContact;

            AddPenetrationMarker(firstContact.Value, "ENTRY", new Color(1f, 0.2f, 0.08f));
            AddPenetrationMarker(lastContact.Value, "EXIT", new Color(0.1f, 0.9f, 1f));
            AddPenetrationChannel(firstContact.Value, lastContact.Value);
        }

        private static bool HasTravelledBeyondMaterial(
            System.Numerics.Vector3 projectilePosition,
            System.Numerics.Vector3 lastContact,
            System.Numerics.Vector3 direction)
        {
            return System.Numerics.Vector3.Dot(projectilePosition - lastContact, direction) > 0.01f;
        }

        private void AddPenetrationMarker(System.Numerics.Vector3 position, string text, Color color)
        {
            var marker = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    EmissionEnabled = true,
                    Emission = color,
                    EmissionEnergyMultiplier = 3f
                },
            };
            _penetrationVisualization.AddChild(marker);
            marker.GlobalPosition = ToGodot(position);

            var label = new Label3D
            {
                Text = text,
                FontSize = 32,
                Modulate = color,
                OutlineSize = 8,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            };
            _penetrationVisualization.AddChild(label);
            label.GlobalPosition = ToGodot(position) + new Godot.Vector3(0f, 0.1f, 0f);
        }

        private void AddPenetrationChannel(
            System.Numerics.Vector3 entryPoint,
            System.Numerics.Vector3 exitPoint)
        {
            Godot.Vector3 entry = ToGodot(entryPoint);
            Godot.Vector3 exit = ToGodot(exitPoint);
            Godot.Vector3 channel = exit - entry;
            float length = channel.Length();
            if (length <= 0.001f)
                return;

            var channelMesh = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = length },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.5f, 0.05f, 0.85f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.25f, 0.02f),
                    EmissionEnergyMultiplier = 2f
                },
            };
            _penetrationVisualization.AddChild(channelMesh);
            channelMesh.GlobalPosition = (entry + exit) * 0.5f;
            channelMesh.GlobalBasis = new Basis(new Godot.Quaternion(Godot.Vector3.Up, channel.Normalized()));
        }

        private static Godot.Vector3 ToGodot(System.Numerics.Vector3 value) =>
            new(value.X, value.Y, value.Z);

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
