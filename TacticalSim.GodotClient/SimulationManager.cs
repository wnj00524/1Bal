using Godot;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;
using TacticalSim.Core.World;
using TacticalSim.Core.Randomness;
using System.Linq;

namespace TacticalSim.GodotClient
{
    public partial class SimulationManager : Node
    {
        [Signal] public delegate void SimulationInitializedEventHandler();
        [Signal] public delegate void EntityAddedEventHandler(string entityId, string entityType, Godot.Vector3 position, float timestamp);
        [Signal] public delegate void EntityRemovedEventHandler(string entityId, string entityType, float timestamp);
        [Signal] public delegate void ActionScheduledEventHandler(string actionType, float globalTime);
        [Signal] public delegate void ActionStartedEventHandler(string actionType, float globalTime);
        [Signal] public delegate void ActionProgressedEventHandler(string actionType, float deltaTime, float currentProgress, float totalCost, float globalTime);
        [Signal] public delegate void ActionCompletedEventHandler(string actionType, float globalTime);
        [Signal] public delegate void ActionCancelledEventHandler(string actionType, float globalTime);
        [Signal] public delegate void ActionFailedEventHandler(string actionType, string errorMessage, float globalTime);
        [Signal] public delegate void SimulationTimeAdvancedEventHandler(float deltaTime, float previousGlobalTime, float currentGlobalTime);

        private const int MaximumBridgeEventsPerFrame = 512;
        private const float WalkingSpeedMetersPerSecond = 1.4f;
        private const float BodyInteractionBroadphaseRadiusSquaredMeters = 4f;
        private const float BodyInteractionMaximumTraversalMeters = 4f;
        private const string InitialChestWoundImpactId = "godot:chest-wound-response:initial-impact";
        private static readonly Guid ShooterEntityId = new("10000000-0000-0000-0000-000000000001");
        private static readonly Guid DummyEntityId = new("10000000-0000-0000-0000-000000000002");
        private IServiceProvider _serviceProvider = null!;
        private IProjectileInteractionService _projectileInteractionService = null!;
        private ITacticalWorld _world = null!;
        private ITurnResolver _turnResolver = null!;
        private readonly ConcurrentQueue<BridgeEvent> _bridgeEvents = new();
        private bool _bridgeIsSubscribed;
        private int _acceptBridgeEvents;

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
        private int _projectileInteractionSequence;

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
        public ProjectileInteractionResult? LastProjectileInteractionResult { get; private set; }
        public ImpactDebugTrace? LastImpactDebugTrace { get; private set; }
        public WoundTrack? LastWoundTrack { get; private set; }
        public EnergyLedger? LastEnergyLedger { get; private set; }
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
            EmitSignal(SignalName.SimulationInitialized);
        }

        public override void _Process(double delta)
        {
            // _Process is always invoked on Godot's main thread. Core event handlers
            // only enqueue value snapshots, so no Godot object is touched by a
            // simulation worker and event production never waits for rendering.
            for (int count = 0;
                 count < MaximumBridgeEventsPerFrame && _bridgeEvents.TryDequeue(out BridgeEvent bridgeEvent);
                 count++)
            {
                EmitBridgeEvent(bridgeEvent);
            }
        }

        public override void _ExitTree()
        {
            UnsubscribeFromCoreEvents();
            while (_bridgeEvents.TryDequeue(out _)) { }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        private void InitializeDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddTacticalSimCoreWithDamageModel(
                new DamageModelOptions(DamageModelVersion.IntegratedV3));
            _serviceProvider = services.BuildServiceProvider();
            _projectileInteractionService =
                _serviceProvider.GetRequiredService<IProjectileInteractionService>();
            _world = _serviceProvider.GetRequiredService<ITacticalWorld>();
            _turnResolver = _serviceProvider.GetRequiredService<ITurnResolver>();
            SubscribeToCoreEvents();
        }

        private void SubscribeToCoreEvents()
        {
            if (_bridgeIsSubscribed)
                return;

            _world.EntityAdded += OnEntityAdded;
            _world.EntityRemoved += OnEntityRemoved;
            _turnResolver.ActionScheduled += OnActionScheduled;
            _turnResolver.ActionStarted += OnActionStarted;
            _turnResolver.ActionProgressed += OnActionProgressed;
            _turnResolver.ActionCompleted += OnActionCompleted;
            _turnResolver.ActionCancelled += OnActionCancelled;
            _turnResolver.ActionFailed += OnActionFailed;
            _turnResolver.TimeAdvanced += OnTimeAdvanced;
            _bridgeIsSubscribed = true;
            Volatile.Write(ref _acceptBridgeEvents, 1);
        }

        private void UnsubscribeFromCoreEvents()
        {
            if (!_bridgeIsSubscribed)
                return;

            // Close the producer gate before detaching handlers. An event already
            // in flight can finish safely, but it cannot enqueue work for a node
            // that is leaving the scene tree.
            Volatile.Write(ref _acceptBridgeEvents, 0);
            _world.EntityAdded -= OnEntityAdded;
            _world.EntityRemoved -= OnEntityRemoved;
            _turnResolver.ActionScheduled -= OnActionScheduled;
            _turnResolver.ActionStarted -= OnActionStarted;
            _turnResolver.ActionProgressed -= OnActionProgressed;
            _turnResolver.ActionCompleted -= OnActionCompleted;
            _turnResolver.ActionCancelled -= OnActionCancelled;
            _turnResolver.ActionFailed -= OnActionFailed;
            _turnResolver.TimeAdvanced -= OnTimeAdvanced;
            _bridgeIsSubscribed = false;
        }

        private void OnEntityAdded(object? sender, EntityEventArgs args)
        {
            System.Numerics.Vector3 position = args.Entity.Position;
            Enqueue(BridgeEvent.Entity(BridgeEventKind.EntityAdded, args.Entity.Id,
                args.Entity.GetType().Name, position, args.Timestamp));
        }

        private void OnEntityRemoved(object? sender, EntityEventArgs args) =>
            Enqueue(BridgeEvent.Entity(BridgeEventKind.EntityRemoved, args.Entity.Id,
                args.Entity.GetType().Name, args.Entity.Position, args.Timestamp));

        private void OnActionScheduled(object? sender, ActionEventArgs args) => EnqueueAction(BridgeEventKind.ActionScheduled, args);
        private void OnActionStarted(object? sender, ActionEventArgs args) => EnqueueAction(BridgeEventKind.ActionStarted, args);
        private void OnActionCompleted(object? sender, ActionEventArgs args) => EnqueueAction(BridgeEventKind.ActionCompleted, args);
        private void OnActionCancelled(object? sender, ActionEventArgs args) => EnqueueAction(BridgeEventKind.ActionCancelled, args);

        private void EnqueueAction(BridgeEventKind kind, ActionEventArgs args) =>
            Enqueue(BridgeEvent.Action(kind, args.Action.GetType().Name, args.GlobalTime));

        private void OnActionProgressed(object? sender, ActionProgressEventArgs args) =>
            Enqueue(BridgeEvent.Progress(args.Action.GetType().Name, args.DeltaTime,
                args.CurrentProgress, args.TotalCost, args.GlobalTime));

        private void OnActionFailed(object? sender, ActionFailedEventArgs args) =>
            Enqueue(BridgeEvent.Failure(args.Action.GetType().Name, args.ErrorMessage, args.GlobalTime));

        private void OnTimeAdvanced(object? sender, TimeAdvancedEventArgs args) =>
            Enqueue(BridgeEvent.Time(args.DeltaTime, args.PreviousGlobalTime, args.CurrentGlobalTime));

        private void Enqueue(BridgeEvent bridgeEvent)
        {
            if (Volatile.Read(ref _acceptBridgeEvents) != 0)
                _bridgeEvents.Enqueue(bridgeEvent);
        }

        private void EmitBridgeEvent(BridgeEvent value)
        {
            switch (value.Kind)
            {
                case BridgeEventKind.EntityAdded:
                    EmitSignal(SignalName.EntityAdded, value.EntityId.ToString("D"), value.TypeName,
                        new Godot.Vector3(value.X, value.Y, value.Z), value.Time1);
                    break;
                case BridgeEventKind.EntityRemoved:
                    EmitSignal(SignalName.EntityRemoved, value.EntityId.ToString("D"), value.TypeName, value.Time1);
                    break;
                case BridgeEventKind.ActionScheduled: EmitSignal(SignalName.ActionScheduled, value.TypeName, value.Time1); break;
                case BridgeEventKind.ActionStarted: EmitSignal(SignalName.ActionStarted, value.TypeName, value.Time1); break;
                case BridgeEventKind.ActionCompleted: EmitSignal(SignalName.ActionCompleted, value.TypeName, value.Time1); break;
                case BridgeEventKind.ActionCancelled: EmitSignal(SignalName.ActionCancelled, value.TypeName, value.Time1); break;
                case BridgeEventKind.ActionProgressed:
                    EmitSignal(SignalName.ActionProgressed, value.TypeName, value.Time1, value.Time2, value.Time3, value.Time4);
                    break;
                case BridgeEventKind.ActionFailed:
                    EmitSignal(SignalName.ActionFailed, value.TypeName, value.Message, value.Time1);
                    break;
                case BridgeEventKind.TimeAdvanced:
                    EmitSignal(SignalName.SimulationTimeAdvanced, value.Time1, value.Time2, value.Time3);
                    break;
            }
        }

        private enum BridgeEventKind
        {
            EntityAdded, EntityRemoved, ActionScheduled, ActionStarted, ActionProgressed,
            ActionCompleted, ActionCancelled, ActionFailed, TimeAdvanced
        }

        private readonly record struct BridgeEvent(
            BridgeEventKind Kind, Guid EntityId, string TypeName, string Message,
            float X, float Y, float Z, float Time1, float Time2, float Time3, float Time4)
        {
            public static BridgeEvent Entity(BridgeEventKind kind, Guid id, string type,
                System.Numerics.Vector3 position, float timestamp) =>
                new(kind, id, type, string.Empty, position.X, position.Y, position.Z, timestamp, 0f, 0f, 0f);
            public static BridgeEvent Action(BridgeEventKind kind, string type, float time) =>
                new(kind, Guid.Empty, type, string.Empty, 0f, 0f, 0f, time, 0f, 0f, 0f);
            public static BridgeEvent Progress(string type, float delta, float current, float total, float global) =>
                new(BridgeEventKind.ActionProgressed, Guid.Empty, type, string.Empty, 0f, 0f, 0f, delta, current, total, global);
            public static BridgeEvent Failure(string type, string message, float time) =>
                new(BridgeEventKind.ActionFailed, Guid.Empty, type, message, 0f, 0f, 0f, time, 0f, 0f, 0f);
            public static BridgeEvent Time(float delta, float previous, float current) =>
                new(BridgeEventKind.TimeAdvanced, Guid.Empty, string.Empty, string.Empty, 0f, 0f, 0f, delta, previous, current, 0f);
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

            RemoveScenarioEntitiesFromWorld();
            ActiveAmmo = weapon ?? throw new ArgumentNullException(nameof(weapon));
            ActiveTarget = target;
            LoadedScenarioName = sceneName;
            ElapsedScenarioTime = 0f;
            HasBulletBeenFired = false;
            IsTargetedShotPending = false;
            LastProjectileTermination = ProjectileTerminationReason.None;
            _projectileState = null;
            _isProjectileSelected = false;
            _projectileInteractionSequence = 0;
            SetLastProjectileInteraction(null);
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
            RemoveScenarioEntitiesFromWorld();
            LoadedScenarioName = null;
            ElapsedScenarioTime = 0f;
            HasBulletBeenFired = false;
            IsTargetedShotPending = false;
            LastProjectileTermination = ProjectileTerminationReason.None;
            _projectileState = null;
            _isProjectileSelected = false;
            _projectileInteractionSequence = 0;
            SetLastProjectileInteraction(null);
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

            MedicalReport traumaReport = MedicalAssessor.AssessTrauma(Dummy.Physiology);
            bool hasOpenChestWound = Dummy.Physiology is IIntegratedMedicalStateTarget integrated
                ? integrated.MedicalState.Thoracic.Lesions.Count > 0
                : traumaReport.DestroyedVolumeCc.TryGetValue(OrganType.Lung, out float destroyedLungVolumeCc)
                  && destroyedLungVolumeCc > 0f;
            if (hasOpenChestWound && !Dummy.Physiology.HasChestSeal)
                actions.Add(MedicalAction.ApplyChestSeal);
            if (Dummy.Physiology.TensionPneumothoraxLevel > 0f)
                actions.Add(MedicalAction.NeedleDecompression);
            AddTourniquetIfApplicable(actions, BodyPartType.LeftArm, MedicalAction.ApplyLeftArmTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.RightArm, MedicalAction.ApplyRightArmTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.LeftLeg, MedicalAction.ApplyLeftLegTourniquet);
            AddTourniquetIfApplicable(actions, BodyPartType.RightLeg, MedicalAction.ApplyRightLegTourniquet);

            BodyPart? abdomen = FindBodyPart(Dummy.Physiology.RootBodyPart, BodyPartType.Abdomen);
            bool hasPackableAbdominalWound = Dummy.Physiology is IIntegratedMedicalStateTarget integratedTarget
                ? HasControllableBleedingSource(integratedTarget.MedicalState, BodyPartType.Abdomen)
                : abdomen != null && !abdomen.HasWoundPacking
                  && abdomen.Voxels.Exists(voxel => voxel.IsDestroyed && voxel.Organ == OrganType.Muscle);
            if (hasPackableAbdominalWound)
                actions.Add(MedicalAction.PackAbdominalWound);
            return actions;
        }

        private void AddTourniquetIfApplicable(
            List<MedicalAction> actions, BodyPartType type, MedicalAction action)
        {
            if (Dummy.Physiology is IIntegratedMedicalStateTarget integrated)
            {
                if (HasControllableBleedingSource(integrated.MedicalState, type))
                    actions.Add(action);
                return;
            }

            BodyPart? part = FindBodyPart(Dummy.Physiology.RootBodyPart, type);
            if (part != null && !part.HasTourniquet && part.GetActiveBleedRate() > 0f)
                actions.Add(action);
        }

        private static bool HasControllableBleedingSource(
            ActorMedicalState state,
            BodyPartType region)
        {
            foreach (BleedingSource source in state.Hemorrhage.Sources)
            {
                if (!source.Compressible
                    || source.ControlState is BleedingControlState.Tourniquet
                        or BleedingControlState.Packed
                        or BleedingControlState.Definitive)
                {
                    continue;
                }

                Lesion? lesion = state.LesionRepository.Lesions.FirstOrDefault(x => x.Id == source.LesionId);
                if (lesion is null)
                    continue;
                try
                {
                    if (state.Anatomy.GetRequired(lesion.StructureId).Region == region)
                        return true;
                }
                catch (KeyNotFoundException)
                {
                    // A legacy voxel-only structure has no integrated treatment target.
                }
            }
            return false;
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
                ShooterEntityId,
                new System.Numerics.Vector3(0, 1.5f, -TargetDistance),
                shooterPhysiology);
            Dummy = new TacticalEntity(
                DummyEntityId,
                new System.Numerics.Vector3(0, 1f, 0),
                AnatomicalDummyBuilder.BuildIntegratedDummy(
                    DummyEntityId.ToString("D"),
                    _serviceProvider.GetRequiredService<IDeterministicRandomStreamProvider>()));

            _world.AddEntity(Shooter);
            _world.AddEntity(Dummy);

            if (LoadedScenarioName == "Chest Wound Response")
            {
                ResolveInitialChestWound();
                Dummy.Physiology.TickPhysiology(1f);
            }

            _shooterVisual.Position = new Godot.Vector3(0, 0.9f, -TargetDistance);
            _dummyVisual.Position = new Godot.Vector3(0, 0.9f, 0);
            _bulletMesh.Position = new Godot.Vector3(
                Shooter.Position.X,
                Shooter.Position.Y,
                Shooter.Position.Z);
        }

        private void RemoveScenarioEntitiesFromWorld()
        {
            _world.RemoveEntity(ShooterEntityId);
            _world.RemoveEntity(DummyEntityId);
        }

        private void ResolveInitialChestWound()
        {
            // Provisional scenario fixture matching the existing 9x19 mm loadout.
            // The authoritative service still owns all traversal and tissue damage.
            var woundAmmo = new AmmunitionProfile
            {
                Name = "9x19mm scenario wound fixture",
                MuzzleVelocity = 380f,
                Ballistics = new BallisticProfile
                {
                    Mass = 0.008f,
                    CrossSectionalArea = 0.0000636f,
                    DragModel = new StandardDragCurve(0.15f)
                }
            };
            var bodyLocalProjectileState = new ProjectileState
            {
                // Fixed scenario input aimed through the center of the right lung.
                Position = new System.Numerics.Vector3(0.08f, 0.28f, -0.5f),
                Velocity = System.Numerics.Vector3.UnitZ * woundAmmo.MuzzleVelocity,
                Time = 0f
            };
            ProjectileInteractionResult? result = _projectileInteractionService.Resolve(
                new ProjectileInteractionRequest(
                    InitialChestWoundImpactId,
                    woundAmmo.Name,
                    Dummy.Physiology,
                    bodyLocalProjectileState,
                    woundAmmo.Ballistics,
                    Distance.FromMeters(BodyInteractionMaximumTraversalMeters),
                    modelVersion: null,
                    Shooter.Id,
                    Dummy.Id));
            SetLastProjectileInteraction(result);

            MedicalReport report = MedicalAssessor.AssessTrauma(Dummy.Physiology);
            bool destroyedLung = Dummy.Physiology is IIntegratedMedicalStateTarget integrated
                ? integrated.MedicalState.LesionRepository.Lesions.Any(
                    lesion => lesion.StructureId.StartsWith("organ.lung-", StringComparison.Ordinal))
                : report.DestroyedVolumeCc.TryGetValue(
                    OrganType.Lung,
                    out float destroyedLungVolumeCc) && destroyedLungVolumeCc > 0f;
            if (result is null
                || !result.WoundTrack.Segments.Any(
                    segment => segment.StructureType == OrganType.Lung.ToString())
                || !destroyedLung)
            {
                throw new InvalidOperationException(
                    "The deterministic Chest Wound Response setup did not produce an actionable lung wound.");
            }
        }

        private void ScrubToTime(float flightTime)
        {
            var ammo = ActiveAmmo;
            LastProjectileTermination = ProjectileTerminationReason.None;
            SetLastProjectileInteraction(null);

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

            bool bodyInteractionAttempted = false;
            System.Numerics.Vector3? wallMaterialContact = null;
            
            // RK4 physics loop (simulating continuous flight until t = flightTime).
            var env = _serviceProvider.GetRequiredService<IEnvironmentModel>();
            WorldBounds worldBounds = WorldBounds.CreateDefault();
            
            while (impactState.Time < flightTime)
            {
                // Body traversal is resolved as one core event. The client retains
                // only the external-flight integration and scene-wall collision.
                float simTimeStep = 0.001f;

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
                    wallMaterialContact = wallContact;
                    impactState.Position = wallContact;
                    impactState.Velocity = System.Numerics.Vector3.Zero;
                    break;
                }
                
                LastProjectileTermination = ProjectileFlightTermination.Evaluate(
                    impactState, ammo.Ballistics, worldBounds);
                if (LastProjectileTermination != ProjectileTerminationReason.None)
                    break;

                System.Numerics.Vector3 localPosition = impactState.Position - Dummy.Position;
                bool isNearTarget =
                    localPosition.LengthSquared() < BodyInteractionBroadphaseRadiusSquaredMeters;
                if (isNearTarget && !bodyInteractionAttempted)
                {
                    bodyInteractionAttempted = true;
                    var bodyLocalProjectileState = impactState;
                    bodyLocalProjectileState.Position = localPosition;
                    string impactId = FormattableString.Invariant(
                        $"godot:{LoadedScenarioName}:impact-{_projectileInteractionSequence++:D4}");
                    ProjectileInteractionResult? result = _projectileInteractionService.Resolve(
                        new ProjectileInteractionRequest(
                            impactId,
                            ActiveAmmo.Name,
                            Dummy.Physiology,
                            bodyLocalProjectileState,
                            ammo.Ballistics,
                            Distance.FromMeters(BodyInteractionMaximumTraversalMeters),
                            modelVersion: null,
                            Shooter.Id,
                            Dummy.Id));
                    SetLastProjectileInteraction(result);

                    if (result is not null)
                    {
                        impactState = result.FinalProjectileState;
                        impactState.Position += Dummy.Position;

                        LastProjectileTermination = ProjectileFlightTermination.Evaluate(
                            impactState, ammo.Ballistics, worldBounds);
                        if (LastProjectileTermination != ProjectileTerminationReason.None)
                            break;
                    }
                }
            }

            _projectileState = impactState;
            if (wallMaterialContact.HasValue)
            {
                UpdateWallImpactVisualization(wallMaterialContact.Value);
            }
            else if (LastWoundTrack is not null)
            {
                UpdateBodyPenetrationVisualization(LastWoundTrack);
            }
            else
            {
                ClearPenetrationVisualization();
            }
            _bulletMesh.GlobalPosition = ToGodot(impactState.Position);
            
            // Look in the direction of velocity
            if (impactState.Velocity.LengthSquared() > 0)
            {
                var targetPt = impactState.Position + impactState.Velocity;
                _bulletMesh.LookAt(new Godot.Vector3(targetPt.X, targetPt.Y, targetPt.Z), Godot.Vector3.Up);
            }
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

        private void SetLastProjectileInteraction(ProjectileInteractionResult? result)
        {
            LastProjectileInteractionResult = result;
            LastImpactDebugTrace = result?.DebugTrace;
            LastWoundTrack = result?.WoundTrack;
            LastEnergyLedger = result?.EnergyLedger;
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

        private void UpdateWallImpactVisualization(System.Numerics.Vector3 contactPoint)
        {
            ClearPenetrationVisualization();
            HasMaterialHit = true;
            MaterialHitPoint = contactPoint;
            AddPenetrationMarker(contactPoint, "HIT", new Color(1f, 0.55f, 0.05f));
        }

        private void UpdateBodyPenetrationVisualization(WoundTrack woundTrack)
        {
            ClearPenetrationVisualization();
            HasMaterialHit = true;

            System.Numerics.Vector3 entryPoint = Dummy.Position + woundTrack.EntryPoint;
            PenetrationEntryPoint = entryPoint;
            AddPenetrationMarker(entryPoint, "ENTRY", new Color(1f, 0.2f, 0.08f));

            foreach (WoundTrackSegment segment in woundTrack.Segments)
            {
                AddPenetrationChannel(
                    Dummy.Position + segment.EntryPoint,
                    Dummy.Position + segment.EndPoint);
            }

            if (woundTrack.Disposition == ProjectileDisposition.Exited)
            {
                HasCompletePenetration = true;
                PenetrationExitPoint = Dummy.Position + woundTrack.ExitPoint!.Value;
                AddPenetrationMarker(
                    PenetrationExitPoint.Value,
                    "EXIT",
                    new Color(0.1f, 0.9f, 1f));
                return;
            }

            System.Numerics.Vector3 retainedPoint =
                Dummy.Position + woundTrack.RetainedPoint!.Value;
            MaterialHitPoint = retainedPoint;
            AddPenetrationMarker(
                retainedPoint,
                "RETAINED",
                new Color(1f, 0.55f, 0.05f));
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
    }
}
// Force Godot rebuild
