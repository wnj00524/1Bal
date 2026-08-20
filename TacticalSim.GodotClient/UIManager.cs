using Godot;
using System.Collections.Generic;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Entities;

namespace TacticalSim.GodotClient
{
    public partial class UIManager : CanvasLayer
    {
        private const float AdvanceSeconds = 5f;

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
        private RichTextLabel _reportText = null!;
        private PopupPanel _targetMenu = null!;
        private TargetSilhouetteMenu _targetSilhouette = null!;

        private readonly string[] _sceneProfiles = { "Training Dummy Outside" };

        private readonly string[] _targetProfiles =
        {
            "Chest", "Head", "Neck", "Abdomen",
            "Left Arm", "Right Arm", "Left Leg", "Right Leg"
        };

        private readonly List<AmmunitionProfile> _weaponProfiles = new()
        {
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
            _reportText = GetNode<RichTextLabel>("Control/ReportPanel/Margin/ReportText");
            _targetMenu = GetNode<PopupPanel>("Control/TargetContextMenu");
            _targetSilhouette = GetNode<TargetSilhouetteMenu>("Control/TargetContextMenu/Margin/VBox/Silhouette");

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

            _scenarioControls.Hide();
            _projectilePanel.Hide();
            _reportText.Text = "Choose a scene and loadout to begin.";
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

            if (_simulationManager.IsDummyAtScreenPosition(click.Position))
            {
                _targetMenu.Position = new Vector2I((int)click.Position.X + 12, (int)click.Position.Y + 12);
                _targetMenu.Popup();
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
            _simulationManager.UnloadScenario();
            _scenarioControls.Hide();
            _setupPanel.Show();
            _reportText.Text = "Choose a scene and loadout to begin.";
            _focusLabel.Text = "Focus: none";
            _projectilePanel.Hide();
        }

        private void OnContextTargetSelected(string target)
        {
            if (!_simulationManager.HasFocusedAgent)
            {
                _focusLabel.Text = "Select an agent before assigning a shot target.";
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
            System.IO.File.WriteAllText("MedicalReport.txt", report.AssessmentText);
            RefreshProjectileTelemetry();
        }

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
