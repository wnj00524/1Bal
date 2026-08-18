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

        private RichTextLabel _reportText = null!;
        private bool _hasExportedReport = false;

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            
            _playPauseButton = GetNode<Button>("Control/Panel/Margin/VBox/HBox/PlayBtn");
            _playPauseButton.Pressed += OnPlayPausePressed;
            
            _timelineSlider = GetNode<HSlider>("Control/Panel/Margin/VBox/HBox/Slider");
            _timelineSlider.ValueChanged += OnSliderValueChanged;
            
            _timeLabel = GetNode<Label>("Control/Panel/Margin/VBox/HBox/TimeLbl");
            _reportText = GetNode<RichTextLabel>("Control/ReportPanel/Margin/ReportText");
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
            _timeLabel.Text = $"{_currentPlaybackTime:F4} / {_maxPlaybackTime:F4} s";
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
                // Playback speed: extremely slow motion for 10ms flight/cavitation
                float nextTime = _currentPlaybackTime + (float)delta * 0.005f; 
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
