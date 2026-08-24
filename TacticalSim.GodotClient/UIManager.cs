using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Entities;

namespace TacticalSim.GodotClient
{
    public partial class UIManager : CanvasLayer
    {
        private const float AdvanceSeconds = 5f;
        private const float TrajectoryPixelsPerMeter = 180f;
        private const float TrajectoryPointSeconds = 0.08f;
        private const int MaximumPendingTelemetryRecords = 8192;
        private const int MaximumPendingTrajectories = 2048;

        [Export]
        public NodePath SimulationManagerPath { get; set; } = null!;

        private SimulationManager _simulationManager = null!;
        private Control _setupPanel = null!;
        private Control _scenarioControls = null!;
        private OptionButton _sceneSelect = null!;
        private OptionButton _weaponSelect = null!;
        private OptionButton _targetSelect = null!;
        private Label _scenarioLabel = null!;
        private Label _timeLabel = null!;
        private Label _focusLabel = null!;
        private Control _projectilePanel = null!;
        private Label _projectileVelocityLabel = null!;
        private Label _projectileEnergyLabel = null!;
        private Label _projectileHeightLabel = null!;
        private Label _penetrationLabel = null!;
        private RichTextLabel _reportText = null!;
        private PopupPanel _targetMenu = null!;
        private TargetSilhouetteMenu _targetSilhouette = null!;
        private PopupPanel _commandMenu = null!;
        private PopupMenu _medicalMenu = null!;
        private RadialCommandMenu _radialCommands = null!;
        private Vector3 _commandDestination;
        private Vector2 _commandScreenPosition;
        private readonly ConcurrentQueue<TrajectorySnapshot> _trajectoryQueue = new();
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly SemaphoreSlim _logSignal = new(0);
        private CancellationTokenSource? _loggerCancellation;
        private Task? _loggerTask;
        private Line2D? _activeTrajectory;
        private TrajectorySnapshot? _activeSnapshot;
        private int _activePointIndex;
        private double _playbackAccumulator;
        private int _pendingLogRecords;
        private int _pendingTrajectories;
        private long _droppedLogRecords;

        private readonly string[] _sceneProfiles = { "Training Dummy Outside", "Chest Wound Response" };

        private readonly string[] _targetProfiles =
        {
            "Chest", "Head", "Neck", "Abdomen",
            "Left Arm", "Right Arm", "Left Leg", "Right Leg"
        };

        private readonly List<AmmunitionProfile> _weaponProfiles = new()
        {
            AmmunitionCatalog.TwentyTwoLongRifle,
            AmmunitionCatalog.ThreeEightyAcp,
            CreateWeapon("5.56x45mm NATO", 900f, 0.004f, 0.000024f, 0.3f),
            CreateWeapon("9x19mm Parabellum", 380f, 0.008f, 0.0000636f, 0.15f),
            CreateWeapon(".308 Winchester", 800f, 0.0097f, 0.000048f, 0.4f),
            CreateWeapon("12 Gauge Slug", 480f, 0.0283f, 0.00025f, 0.5f),
            CreateWeapon("Combat Knife (Abdomen)", 15f, 0.4f, 0.00015f, 10f),
            CreateWeapon("Combat Knife (Neck)", 15f, 0.4f, 0.00015f, 10f),
            CreateWeapon("9x19mm (Head)", 380f, 0.008f, 0.0000636f, 0.15f)
        };

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            _setupPanel = GetNode<Control>("Control/SetupPanel");
            _scenarioControls = GetNode<Control>("Control/ScenarioPanel");
            _sceneSelect = GetNode<OptionButton>("Control/SetupPanel/Margin/VBox/SceneSelect");
            _weaponSelect = GetNode<OptionButton>("Control/SetupPanel/Margin/VBox/WeaponSelect");
            _targetSelect = GetNode<OptionButton>("Control/SetupPanel/Margin/VBox/TargetSelect");
            _scenarioLabel = GetNode<Label>("Control/ScenarioPanel/Margin/HBox/ScenarioLbl");
            _timeLabel = GetNode<Label>("Control/ScenarioPanel/Margin/HBox/TimeLbl");
            _focusLabel = GetNode<Label>("Control/ScenarioPanel/Margin/HBox/FocusLbl");
            _projectilePanel = GetNode<Control>("Control/ProjectilePanel");
            _projectileVelocityLabel = GetNode<Label>("Control/ProjectilePanel/Margin/VBox/VelocityLbl");
            _projectileEnergyLabel = GetNode<Label>("Control/ProjectilePanel/Margin/VBox/EnergyLbl");
            _projectileHeightLabel = GetNode<Label>("Control/ProjectilePanel/Margin/VBox/HeightLbl");
            _penetrationLabel = GetNode<Label>("Control/ScenarioPanel/Margin/HBox/PenetrationLbl");
            _reportText = GetNode<RichTextLabel>("Control/ReportPanel/Margin/ReportText");
            _targetMenu = GetNode<PopupPanel>("Control/TargetContextMenu");
            _targetSilhouette = GetNode<TargetSilhouetteMenu>("Control/TargetContextMenu/Margin/VBox/Silhouette");
            _commandMenu = GetNode<PopupPanel>("Control/CommandMenu");
            _radialCommands = GetNode<RadialCommandMenu>("Control/CommandMenu/RadialCommands");

            foreach (string scene in _sceneProfiles)
                _sceneSelect.AddItem(scene);
            foreach (AmmunitionProfile weapon in _weaponProfiles)
                _weaponSelect.AddItem(weapon.Name);
            foreach (string target in _targetProfiles)
                _targetSelect.AddItem(target);

            GetNode<Button>("Control/SetupPanel/Margin/VBox/LoadBtn").Pressed += OnLoadScenarioPressed;
            GetNode<Button>("Control/ScenarioPanel/Margin/HBox/AdvanceBtn").Pressed += OnAdvancePressed;
            GetNode<Button>("Control/ScenarioPanel/Margin/HBox/ChangeBtn").Pressed += OnChangeScenarioPressed;
            _targetSilhouette.TargetSelected += OnContextTargetSelected;
            _radialCommands.MoveSelected += OnMoveSelected;
            _radialCommands.ShootSelected += OnShootSelected;
            _radialCommands.MedicalSelected += OnMedicalSelected;
            _medicalMenu = new PopupMenu { Name = "MedicalMenu" };
            AddChild(_medicalMenu);
            _medicalMenu.IdPressed += OnMedicalActionSelected;
            _simulationManager.ActionCompleted += OnResolutionCompleted;

            _loggerCancellation = new CancellationTokenSource();
            CancellationToken loggerToken = _loggerCancellation.Token;
            _loggerTask = Task.Run(() => RunLogWriterAsync(loggerToken));

            _scenarioControls.Hide();
            _projectilePanel.Hide();
            _reportText.Text = "Choose a scene and loadout to begin.";
            _penetrationLabel.Text = "Penetration: none";
        }

        public override void _Process(double delta)
        {
            _playbackAccumulator += Math.Max(0d, delta);
            while (_playbackAccumulator >= TrajectoryPointSeconds)
            {
                _playbackAccumulator -= TrajectoryPointSeconds;
                AdvanceTrajectoryPlayback();
            }
        }

        public override void _ExitTree()
        {
            if (_simulationManager is not null)
                _simulationManager.ActionCompleted -= OnResolutionCompleted;

            _loggerCancellation?.Cancel();
            _logSignal.Release();
            _ = _loggerTask?.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _loggerCancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _loggerTask = null;
            _loggerCancellation = null;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Left,
                    Pressed: true
                } leftClick && _simulationManager.IsScenarioLoaded)
            {
                _focusLabel.Text = _simulationManager.HandleLeftClick(leftClick.Position);
                RefreshProjectileTelemetry();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event is not InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Right,
                    Pressed: true
                } click || !_simulationManager.IsScenarioLoaded)
                return;

            _targetMenu.Hide();
            _medicalMenu.Hide();
            _commandScreenPosition = click.Position;

            bool isDummy = _simulationManager.IsDummyAtScreenPosition(click.Position);
            var commands = new List<RadialCommandMenu.Command>();
            if (_simulationManager.IsValidShotTargetAtScreenPosition(click.Position))
                commands.Add(RadialCommandMenu.Command.Shoot);
            if (isDummy && _simulationManager.GetAvailableMedicalActions().Count > 0)
                commands.Add(RadialCommandMenu.Command.Medical);
            if (commands.Count > 0)
            {
                ShowCommandMenu(click.Position, commands);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_simulationManager.TryGetMoveDestination(click.Position, out _commandDestination))
            {
                ShowCommandMenu(click.Position, new[] { RadialCommandMenu.Command.Move });
                GetViewport().SetInputAsHandled();
            }
        }

        private void OnLoadScenarioPressed()
        {
            string sceneName = _sceneProfiles[_sceneSelect.Selected];
            AmmunitionProfile weapon = _weaponProfiles[_weaponSelect.Selected];
            string target = _targetProfiles[_targetSelect.Selected];

            _simulationManager.LoadScenario(sceneName, weapon, target);
            _scenarioLabel.Text = $"{sceneName}  |  {weapon.Name}  |  {target}";
            _setupPanel.Hide();
            _scenarioControls.Show();
            RefreshScenarioStatus();
        }

        private void OnAdvancePressed()
        {
            _simulationManager.AdvanceScenario(AdvanceSeconds);
            RefreshScenarioStatus();
        }

        private void OnChangeScenarioPressed()
        {
            _targetMenu.Hide();
            _commandMenu.Hide();
            _medicalMenu.Hide();
            _simulationManager.UnloadScenario();
            ClearTrajectoryPlayback();
            _scenarioControls.Hide();
            _setupPanel.Show();
            _reportText.Text = "Choose a scene and loadout to begin.";
            _focusLabel.Text = "Focus: none";
            _projectilePanel.Hide();
            _penetrationLabel.Text = "Penetration: none";
        }

        private void OnMoveSelected()
        {
            _simulationManager.AssignMoveDestination(_commandDestination);
            _focusLabel.Text = "Move assigned. Advance the simulation to execute it.";
            _commandMenu.Hide();
        }

        private void OnShootSelected()
        {
            _commandMenu.Hide();
            // The target actor is created when a scenario is loaded, after the silhouette's
            // _Ready callback. Rebind here so every shot uses the current model geometry.
            _targetSilhouette.SetTarget(_simulationManager.Dummy);
            _targetMenu.Position = new Vector2I(
                (int)_commandScreenPosition.X + 12,
                (int)_commandScreenPosition.Y + 12);
            _targetMenu.Popup();
        }

        private void OnMedicalSelected()
        {
            _commandMenu.Hide();
            _medicalMenu.Clear();
            foreach (SimulationManager.MedicalAction action in _simulationManager.GetAvailableMedicalActions())
                _medicalMenu.AddItem(FormatMedicalAction(action), (int)action);
            _medicalMenu.Position = new Vector2I((int)_commandScreenPosition.X + 12, (int)_commandScreenPosition.Y + 12);
            _medicalMenu.Popup();
        }

        private void OnMedicalActionSelected(long id)
        {
            var action = (SimulationManager.MedicalAction)id;
            _simulationManager.QueueMedicalAction(action);
            _focusLabel.Text = $"{FormatMedicalAction(action)} ordered. Advance the simulation to treat the dummy.";
            _medicalMenu.Hide();
        }

        private static string FormatMedicalAction(SimulationManager.MedicalAction action) => action switch
        {
            SimulationManager.MedicalAction.ApplyChestSeal => "Apply chest seal",
            SimulationManager.MedicalAction.NeedleDecompression => "Needle decompression",
            SimulationManager.MedicalAction.ApplyLeftArmTourniquet => "Tourniquet: left arm",
            SimulationManager.MedicalAction.ApplyRightArmTourniquet => "Tourniquet: right arm",
            SimulationManager.MedicalAction.ApplyLeftLegTourniquet => "Tourniquet: left leg",
            SimulationManager.MedicalAction.ApplyRightLegTourniquet => "Tourniquet: right leg",
            SimulationManager.MedicalAction.PackAbdominalWound => "Pack abdominal-wall wound",
            _ => action.ToString()
        };

        private void ShowCommandMenu(Vector2 screenPosition, IEnumerable<RadialCommandMenu.Command> commands)
        {
            _radialCommands.ShowCommands(commands);
            _commandMenu.Position = new Vector2I(
                (int)screenPosition.X - 76,
                (int)screenPosition.Y - 76);
            _commandMenu.Popup();
        }

        private void OnContextTargetSelected(string target)
        {
            if (!ReferenceEquals(_simulationManager.FocusedAgent, _simulationManager.Shooter))
            {
                _focusLabel.Text = "Select the shooter before assigning a shot target.";
                _targetMenu.Hide();
                return;
            }

            _simulationManager.QueueTargetedShot(target);
            _targetSelect.Select(System.Array.IndexOf(_targetProfiles, target));
            UpdateScenarioLabel();
            _targetMenu.Hide();
        }

        private void UpdateScenarioLabel()
        {
            _scenarioLabel.Text = $"{_simulationManager.LoadedScenarioName}  |  "
                + $"{_simulationManager.ActiveAmmo.Name}  |  {_simulationManager.ActiveTarget}";
        }

        private void RefreshScenarioStatus()
        {
            _timeLabel.Text = $"Elapsed: {_simulationManager.ElapsedScenarioTime:F0}s";
            var report = TacticalSim.Core.MedicalAssessor.AssessTrauma(_simulationManager.Dummy.Physiology);
            _reportText.Text = report.AssessmentText;
            _penetrationLabel.Text = _simulationManager.HasCompletePenetration
                ? "Penetration: THROUGH (ENTRY → EXIT)"
                : _simulationManager.HasMaterialHit
                    ? "Penetration: HIT (STOPPED)"
                    : "Penetration: none";
            _penetrationLabel.Modulate = _simulationManager.HasCompletePenetration
                ? new Color(0.25f, 1f, 0.7f)
                : _simulationManager.HasMaterialHit
                    ? new Color(1f, 0.65f, 0.15f)
                    : Colors.White;
            BindPhysiologicalState(CaptureNeurologicalState());
            EnqueueLog($"MEDICAL ASSESSMENT{System.Environment.NewLine}{report.AssessmentText}");
            RefreshProjectileTelemetry();
        }

        private void OnResolutionCompleted(string actionType, float globalTime)
        {
            // The bridge signal is delivered on Godot's main thread. Copy all core
            // values here; queued playback never retains or mutates simulation state.
            WoundTrack? woundTrack = _simulationManager.LastWoundTrack;
            if (woundTrack is not null)
            {
                EnqueueTrack("projectile", woundTrack.Segments.SelectMany(static segment =>
                    new[] { segment.EntryPoint, segment.EndPoint }));

                foreach (FragmentTrack fragment in woundTrack.FragmentTracks.OrderBy(static item => item.Sequence))
                {
                    EnqueueTrack(fragment.FragmentId, fragment.Segments.SelectMany(static segment =>
                        new[] { segment.EntryPoint, segment.EndPoint }));
                }
            }

            NeurologicalFunctionalState neurological = CaptureNeurologicalState();
            BindPhysiologicalState(neurological);
            EnqueueLog(string.Format(CultureInfo.InvariantCulture,
                "RESOLUTION action={0} time={1:F3}s neuro=[LU:{2:F3},RU:{3:F3},LL:{4:F3},RL:{5:F3}] tracks={6}",
                actionType, globalTime, neurological.LeftUpperLimbCapacity,
                neurological.RightUpperLimbCapacity, neurological.LeftLowerLimbCapacity,
                neurological.RightLowerLimbCapacity, woundTrack?.FragmentTracks.Count ?? 0));
        }

        private void EnqueueTrack(string id, IEnumerable<System.Numerics.Vector3> source)
        {
            System.Numerics.Vector3[] points = source
                .Where(static point => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z))
                .ToArray();
            if (points.Length < 2)
                return;

            if (Interlocked.Increment(ref _pendingTrajectories) > MaximumPendingTrajectories)
            {
                Interlocked.Decrement(ref _pendingTrajectories);
                EnqueueLog($"TRAJECTORY_DROPPED id={id} reason=queue_capacity");
                return;
            }

            var screenPoints = new Vector2[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                // Body-local X/Y metres are projected into the diagnostic overlay.
                screenPoints[index] = new Vector2(580f + points[index].X * TrajectoryPixelsPerMeter,
                    360f - points[index].Y * TrajectoryPixelsPerMeter);
            }

            _trajectoryQueue.Enqueue(new TrajectorySnapshot(id, screenPoints));
        }

        private void AdvanceTrajectoryPlayback()
        {
            if (_activeSnapshot is null)
            {
                if (!_trajectoryQueue.TryDequeue(out TrajectorySnapshot? next))
                    return;
                Interlocked.Decrement(ref _pendingTrajectories);

                _activeSnapshot = next;
                _activePointIndex = 0;
                _activeTrajectory = new Line2D
                {
                    Name = $"Telemetry_{SanitizeNodeName(next.Id)}",
                    Width = 2.5f,
                    DefaultColor = new Color(1f, 0.35f, 0.12f, 0.9f),
                    Antialiased = true
                };
                AddChild(_activeTrajectory);
            }

            if (_activePointIndex < _activeSnapshot.Points.Length)
            {
                _activeTrajectory!.AddPoint(_activeSnapshot.Points[_activePointIndex++]);
                return;
            }

            _activeTrajectory?.QueueFree();
            _activeTrajectory = null;
            _activeSnapshot = null;
        }

        private void BindPhysiologicalState(NeurologicalFunctionalState state)
        {
            float capacity = Math.Clamp(Math.Min(state.UpperLimbCapacity, state.LowerLimbCapacity), 0f, 1f);
            float impairment = 1f - capacity;
            _reportText.Modulate = new Color(1f, 1f - (0.65f * impairment), 1f - (0.8f * impairment),
                0.55f + (0.45f * capacity));
        }

        private NeurologicalFunctionalState CaptureNeurologicalState()
        {
            if (_simulationManager.Dummy.Physiology is INeurologicalFunctionalTarget neurologicalTarget)
                return neurologicalTarget.NeurologicalFunctionalState;

            // IActorPhysiology exposes the resolver's aggregate motor projections even
            // when a custom physiology implementation omits the optional target interface.
            float upper = Math.Clamp(_simulationManager.Dummy.Physiology.WeaponHandlingLevel, 0f, 1f);
            float lower = Math.Clamp(_simulationManager.Dummy.Physiology.MobilityLevel, 0f, 1f);
            return new NeurologicalFunctionalState(upper, upper, lower, lower);
        }

        private void EnqueueLog(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;
            if (Interlocked.Increment(ref _pendingLogRecords) > MaximumPendingTelemetryRecords)
            {
                Interlocked.Decrement(ref _pendingLogRecords);
                Interlocked.Increment(ref _droppedLogRecords);
                return;
            }

            _logQueue.Enqueue($"[{DateTimeOffset.UtcNow:O}] {payload}");
            _logSignal.Release();
        }

        private async Task RunLogWriterAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "MedicalReport.txt");
            try
            {
                await using var stream = new System.IO.FileStream(
                    path,
                    System.IO.FileMode.Append,
                    System.IO.FileAccess.Write,
                    System.IO.FileShare.Read,
                    65536,
                    System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536)
                { AutoFlush = false };

                while (!cancellationToken.IsCancellationRequested || !_logQueue.IsEmpty)
                {
                    try { await _logSignal.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken); }
                    catch (OperationCanceledException) { }

                    while (_logQueue.TryDequeue(out string? record))
                    {
                        Interlocked.Decrement(ref _pendingLogRecords);
                        await writer.WriteLineAsync(record);
                    }

                    long dropped = Interlocked.Exchange(ref _droppedLogRecords, 0);
                    if (dropped > 0)
                        await writer.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] TELEMETRY_DROPPED count={dropped}");
                    await writer.FlushAsync(cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Medical telemetry writer stopped: {exception.Message}");
            }
        }

        private void ClearTrajectoryPlayback()
        {
            while (_trajectoryQueue.TryDequeue(out _))
                Interlocked.Decrement(ref _pendingTrajectories);
            _activeTrajectory?.QueueFree();
            _activeTrajectory = null;
            _activeSnapshot = null;
            _activePointIndex = 0;
            _playbackAccumulator = 0d;
        }

        private static string SanitizeNodeName(string value) =>
            string.Concat(value.Select(static character => char.IsLetterOrDigit(character) ? character : '_'));

        private sealed record TrajectorySnapshot(string Id, Vector2[] Points);

        private void RefreshProjectileTelemetry()
        {
            var telemetry = _simulationManager.SelectedProjectileTelemetry;
            if (!telemetry.HasValue)
            {
                _projectilePanel.Hide();
                return;
            }

            _projectileVelocityLabel.Text = $"Velocity: {telemetry.Value.Velocity:F1} m/s";
            _projectileEnergyLabel.Text = $"Energy: {telemetry.Value.KineticEnergy:F1} J";
            _projectileHeightLabel.Text = $"Height: {telemetry.Value.Height:F2} m";
            _projectilePanel.Show();
        }

        private static AmmunitionProfile CreateWeapon(
            string name, float muzzleVelocity, float mass, float area, float dragCoefficient)
        {
            return new AmmunitionProfile
            {
                Name = name,
                MuzzleVelocity = muzzleVelocity,
                Ballistics = new BallisticProfile
                {
                    Mass = mass,
                    CrossSectionalArea = area,
                    DragModel = new StandardDragCurve(dragCoefficient)
                }
            };
        }
    }
}
