using Godot;

namespace TacticalSim.GodotClient
{
    public partial class CameraOrbit : Camera3D
    {
        [Export] public float RotationSpeed { get; set; } = 0.005f;
        [Export] public float ZoomSpeed { get; set; } = 0.5f;
        [Export] public float MinZoom { get; set; } = 0.5f;
        [Export] public float MaxZoom { get; set; } = 10.0f;
        
        private Vector3 _targetPosition = new Vector3(0, 1.25f, 0); // Global Torso center
        private float _distance = 3.0f;
        private float _yaw = 0.0f;
        private float _pitch = 0.0f;

        public override void _Ready()
        {
            UpdateCameraPosition();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion mouseMotion)
            {
                if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right))
                {
                    _yaw -= mouseMotion.Relative.X * RotationSpeed;
                    _pitch -= mouseMotion.Relative.Y * RotationSpeed;
                    
                    // Clamp pitch to avoid flipping
                    _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2.1f, Mathf.Pi / 2.1f);
                    
                    UpdateCameraPosition();
                }
            }
            else if (@event is InputEventMouseButton mouseButton)
            {
                if (mouseButton.IsPressed())
                {
                    if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                    {
                        _distance -= ZoomSpeed;
                    }
                    else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                    {
                        _distance += ZoomSpeed;
                    }
                    
                    _distance = Mathf.Clamp(_distance, MinZoom, MaxZoom);
                    UpdateCameraPosition();
                }
            }
        }

        private void UpdateCameraPosition()
        {
            var offset = new Vector3(
                _distance * Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
                _distance * Mathf.Sin(_pitch),
                _distance * Mathf.Cos(_pitch) * Mathf.Cos(_yaw)
            );

            Position = _targetPosition + offset;
            LookAt(_targetPosition, Vector3.Up);
        }
    }
}
