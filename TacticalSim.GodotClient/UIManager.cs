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
        private float _maxPlaybackTime = 0.0015f; // 1.5ms bullet flight time

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            
            _playPauseButton = GetNode<Button>("Control/Panel/Margin/VBox/HBox/PlayBtn");
            _playPauseButton.Pressed += OnPlayPausePressed;
            
            _timelineSlider = GetNode<HSlider>("Control/Panel/Margin/VBox/HBox/Slider");
            _timelineSlider.ValueChanged += OnSliderValueChanged;
            
            _timeLabel = GetNode<Label>("Control/Panel/Margin/VBox/HBox/TimeLbl");
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
                // Playback speed: extremely slow motion for 1.5ms flight
                float nextTime = _currentPlaybackTime + (float)delta * 0.0005f; 
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
