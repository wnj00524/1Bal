using Godot;
using System;

namespace TacticalSim.GodotClient
{
    public partial class UIManager : CanvasLayer
    {
        [Export]
        public NodePath SimulationManagerPath { get; set; } = null!;
        
        private SimulationManager _simulationManager = null!;
        private HSlider _timelineSlider = null!;
        private Label _timeLabel = null!;
        private Button _playPauseButton = null!;
        
        private bool _isPlaying = false;
        private float _currentPlaybackTime = 0f;
        private float _maxPlaybackTime = 0.010f; // 10ms to see cavitation collapse

        private Button _medTickButton = null!;
        private RichTextLabel _reportText = null!;
        private bool _hasExportedReport = false;

        private OptionButton _ammoSelectButton = null!;
        private OptionButton _targetSelectButton = null!;
        
        private readonly string[] _targetProfiles = new[]
        {
            "Chest",
            "Head",
            "Neck",
            "Abdomen",
            "Left Arm",
            "Right Arm",
            "Left Leg",
            "Right Leg"
        };
        
        private readonly System.Collections.Generic.List<TacticalSim.Core.Entities.AmmunitionProfile> _ammoProfiles = new()
        {
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "5.56x45mm NATO",
                MuzzleVelocity = 900f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.004f, CrossSectionalArea = 0.000024f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(0.3f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "9x19mm Parabellum",
                MuzzleVelocity = 380f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.008f, CrossSectionalArea = 0.0000636f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(0.15f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = ".308 Winchester",
                MuzzleVelocity = 800f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.0097f, CrossSectionalArea = 0.000048f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(0.4f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "12 Gauge Slug",
                MuzzleVelocity = 480f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.0283f, CrossSectionalArea = 0.00025f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(0.5f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "Combat Knife (Abdomen)",
                MuzzleVelocity = 15f, // Fast stab
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.4f, CrossSectionalArea = 0.00015f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(10.0f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "Combat Knife (Neck)",
                MuzzleVelocity = 15f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.4f, CrossSectionalArea = 0.00015f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(10.0f) }
            },
            new TacticalSim.Core.Entities.AmmunitionProfile
            {
                Name = "9x19mm (Head)",
                MuzzleVelocity = 380f,
                Ballistics = new TacticalSim.Core.Ballistics.BallisticProfile { Mass = 0.008f, CrossSectionalArea = 0.0000636f, DragModel = new TacticalSim.Core.Ballistics.StandardDragCurve(0.15f) }
            }
        };

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            
            _ammoSelectButton = GetNode<OptionButton>("Control/Panel/Margin/VBox/HBox/AmmoSelect");
            foreach (var ammo in _ammoProfiles)
            {
                _ammoSelectButton.AddItem(ammo.Name);
            }
            _ammoSelectButton.ItemSelected += OnAmmoSelected;
            
            _targetSelectButton = GetNode<OptionButton>("Control/Panel/Margin/VBox/HBox/TargetSelect");
            foreach (var target in _targetProfiles)
            {
                _targetSelectButton.AddItem(target);
            }
            _targetSelectButton.ItemSelected += OnTargetSelected;
            
            _playPauseButton = GetNode<Button>("Control/Panel/Margin/VBox/HBox/PlayBtn");
            _playPauseButton.Pressed += OnPlayPausePressed;
            
            _timelineSlider = GetNode<HSlider>("Control/Panel/Margin/VBox/HBox/Slider");
            _timelineSlider.ValueChanged += OnSliderValueChanged;
            
            _timeLabel = GetNode<Label>("Control/Panel/Margin/VBox/HBox/TimeLbl");
            
            _medTickButton = GetNode<Button>("Control/Panel/Margin/VBox/HBox/MedTickBtn");
            _medTickButton.Pressed += OnMedTickPressed;
            
            var hbox = GetNode<Godot.BoxContainer>("Control/Panel/Margin/VBox/HBox");
            var analgesicBtn = new Godot.Button { Text = "Give Analgesic" };
            hbox.AddChild(analgesicBtn);
            analgesicBtn.Pressed += () => {
                if (_simulationManager.Dummy != null)
                {
                    _simulationManager.Dummy.Physiology.AdministerAnalgesic(0.5f);
                    var report = TacticalSim.Core.MedicalAssessor.AssessTrauma(_simulationManager.Dummy.Physiology);
                    _reportText.Text = report.AssessmentText;
                }
            };
            
            _reportText = GetNode<RichTextLabel>("Control/ReportPanel/Margin/ReportText");
            
            // Initialize with default ammo and target to set bounds correctly
            OnTargetSelected(0);
            OnAmmoSelected(0);
        }

        private void OnTargetSelected(long index)
        {
            _simulationManager.ActiveTarget = _targetProfiles[(int)index];
            _currentPlaybackTime = 0f;
            _timelineSlider.Value = 0f;
            UpdateScrubber(0f);
        }

        private void OnAmmoSelected(long index)
        {
            _simulationManager.ActiveAmmo = _ammoProfiles[(int)index];
            
            // Calculate time to travel to the target + a little extra for pass-through/cavitation
            float dist = _simulationManager.ActiveAmmo.Name.Contains("Knife") ? 1.0f : 10.0f;
            _maxPlaybackTime = (dist / _simulationManager.ActiveAmmo.MuzzleVelocity) + 0.005f;

            _timelineSlider.MaxValue = _maxPlaybackTime;
            
            // Re-simulate from beginning if we change ammo
            _currentPlaybackTime = 0f;
            _timelineSlider.Value = 0f;
            UpdateScrubber(0f);
        }

        private float _postImpactTime = 0f;

        private void OnMedTickPressed()
        {
            if (_simulationManager.Dummy != null)
            {
                _simulationManager.Dummy.Physiology.TickPhysiology(10.0f);
                _postImpactTime += 10.0f;
                UpdateTimeLabel();
                
                var report = TacticalSim.Core.MedicalAssessor.AssessTrauma(_simulationManager.Dummy.Physiology);
                _reportText.Text = report.AssessmentText;
            }
        }

        private void UpdateTimeLabel()
        {
            if (_postImpactTime > 0f)
            {
                _timeLabel.Text = $"{_currentPlaybackTime:F4} / {_maxPlaybackTime:F4} s  [+{_postImpactTime:F0}s]";
            }
            else
            {
                _timeLabel.Text = $"{_currentPlaybackTime:F4} / {_maxPlaybackTime:F4} s";
            }
        }

        private void OnPlayPausePressed()
        {
            _isPlaying = !_isPlaying;
            _playPauseButton.Text = _isPlaying ? "Pause" : "Play";
        }

        private void OnSliderValueChanged(double value)
        {
            if (!_isPlaying) 
            {
                UpdateScrubber((float)value);
            }
        }

        private void UpdateScrubber(float time)
        {
            _currentPlaybackTime = time;
            _postImpactTime = 0f;
            UpdateTimeLabel();
            
            _simulationManager.ScrubToTime(_currentPlaybackTime);
            
            // Generate live medical assessment
            if (_simulationManager.Dummy != null)
            {
                var report = TacticalSim.Core.MedicalAssessor.AssessTrauma(_simulationManager.Dummy.Physiology);
                _reportText.Text = report.AssessmentText;
                
                // Export report when reaching the end of the timeline
                if (time >= _maxPlaybackTime && !_hasExportedReport)
                {
                    System.IO.File.WriteAllText("MedicalReport.txt", report.AssessmentText);
                    _hasExportedReport = true;
                }
                else if (time < _maxPlaybackTime)
                {
                    _hasExportedReport = false; // Reset if scrubbed backward
                }
            }
        }

        public override void _Process(double delta)
        {
            if (_isPlaying)
            {
                // Playback speed: scaled so full flight takes about 2 seconds of real time
                float playbackSpeed = _maxPlaybackTime / 2.0f;
                float nextTime = _currentPlaybackTime + (float)delta * playbackSpeed; 
                if (nextTime >= _maxPlaybackTime)
                {
                    nextTime = _maxPlaybackTime;
                    OnPlayPausePressed(); // Auto-pause at end
                }
                
                // Update slider without triggering its signal
                _timelineSlider.SetBlockSignals(true);
                _timelineSlider.Value = nextTime;
                _timelineSlider.SetBlockSignals(false);

                UpdateScrubber(nextTime);
            }
        }
    }
}
