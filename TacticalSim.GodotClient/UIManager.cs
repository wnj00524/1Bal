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
        private float _maxPlaybackTime = 0.030f; // 30ms bullet flight time

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            BuildUI();
        }

        private void BuildUI()
        {
            var panel = new PanelContainer();
            panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            panel.CustomMinimumSize = new Godot.Vector2(0, 100);
            AddChild(panel);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 20);
            margin.AddThemeConstantOverride("margin_right", 20);
            margin.AddThemeConstantOverride("margin_top", 10);
            margin.AddThemeConstantOverride("margin_bottom", 10);
            panel.AddChild(margin);

            var vbox = new VBoxContainer();
            margin.AddChild(vbox);

            var header = new Label { Text = "TacticalSim - Bullet Time Scrubber" };
            vbox.AddChild(header);

            var hbox = new HBoxContainer();
            vbox.AddChild(hbox);

            _playPauseButton = new Button { Text = "Play" };
            _playPauseButton.Pressed += OnPlayPausePressed;
            hbox.AddChild(_playPauseButton);

            _timelineSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = _maxPlaybackTime,
                Step = 0.0005, // 0.5ms steps
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Godot.Vector2(0, 30)
            };
            _timelineSlider.ValueChanged += OnSliderValueChanged;
            hbox.AddChild(_timelineSlider);

            _timeLabel = new Label { Text = "0.000 / 0.030 s" };
            hbox.AddChild(_timeLabel);
        }

        private void OnPlayPausePressed()
        {
            _isPlaying = !_isPlaying;
            _playPauseButton.Text = _isPlaying ? "Pause" : "Play";
        }

        private void OnSliderValueChanged(double value)
        {
            _currentPlaybackTime = (float)value;
            _timeLabel.Text = $"{_currentPlaybackTime:F4} / {_maxPlaybackTime:F3} s";
            
            _simulationManager.ScrubToTime(_currentPlaybackTime);
        }

        public override void _Process(double delta)
        {
            if (_isPlaying)
            {
                // Playback speed: 0.01 seconds of flight per real second (slow motion)
                float nextTime = _currentPlaybackTime + (float)delta * 0.01f; 
                if (nextTime >= _maxPlaybackTime)
                {
                    nextTime = _maxPlaybackTime;
                    OnPlayPausePressed(); // Auto-pause at end
                }
                
                _timelineSlider.Value = nextTime;
            }
        }
    }
}
