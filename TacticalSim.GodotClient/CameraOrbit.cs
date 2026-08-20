using Godot;

namespace TacticalSim.GodotClient;

public partial class CameraOrbit : Camera3D
{
    [Export]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export]
    public float PanSensitivity { get; set; } = 0.01f;

    [Export]
    public float RotationSpeed { get; set; } = 0.005f;

    [Export]
    public float ZoomSpeed { get; set; } = 0.5f;

    [Export]
    public float MinZoom { get; set; } = 0.5f;

    [Export]
    public float MaxZoom { get; set; } = 10.0f;

    private Vector3 _targetPosition = new(0.0f, 1.25f, 0.0f);
    private float _distance = 3.0f;
    private float _yaw;
    private float _pitch = Mathf.Pi / 2.1f;
    private bool _isPanning;
    private bool _isOrbiting;

    public override void _Ready()
    {
        UpdateCameraPosition();
    }

    public override void _Process(double delta)
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        Vector3 right = GlobalTransform.Basis.X;

        forward.Y = 0.0f;
        right.Y = 0.0f;

        if (!forward.IsZeroApprox()) forward = forward.Normalized();
        if (!right.IsZeroApprox()) right = right.Normalized();

        Vector3 movement = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) movement += forward;
        if (Input.IsPhysicalKeyPressed(Key.S)) movement -= forward;
        if (Input.IsPhysicalKeyPressed(Key.A)) movement -= right;
        if (Input.IsPhysicalKeyPressed(Key.D)) movement += right;
        if (Input.IsPhysicalKeyPressed(Key.E)) movement += Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.Q)) movement += Vector3.Down;

        if (!movement.IsZeroApprox())
        {
            _targetPosition += movement.Normalized() * MoveSpeed * (float)delta;
            UpdateCameraPosition();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Middle:
                    _isPanning = mouseButton.Pressed;
                    break;
                case MouseButton.Right:
                    _isOrbiting = mouseButton.Pressed;
                    break;
                case MouseButton.WheelUp when mouseButton.Pressed:
                    _distance = Mathf.Clamp(_distance - ZoomSpeed, MinZoom, MaxZoom);
                    UpdateCameraPosition();
                    break;
                case MouseButton.WheelDown when mouseButton.Pressed:
                    _distance = Mathf.Clamp(_distance + ZoomSpeed, MinZoom, MaxZoom);
                    UpdateCameraPosition();
                    break;
            }

            return;
        }

        if (@event is not InputEventMouseMotion mouseMotion)
        {
            return;
        }

        if (_isPanning)
        {
            _targetPosition +=
                (-GlobalTransform.Basis.X * mouseMotion.Relative.X
                 + GlobalTransform.Basis.Y * mouseMotion.Relative.Y)
                * PanSensitivity;
        }

        if (_isOrbiting)
        {
            _yaw -= mouseMotion.Relative.X * RotationSpeed;
            _pitch = Mathf.Clamp(
                _pitch - mouseMotion.Relative.Y * RotationSpeed,
                -Mathf.Pi / 2.1f,
                Mathf.Pi / 2.1f);
        }

        if (_isPanning || _isOrbiting)
        {
            UpdateCameraPosition();
        }
    }

    private void UpdateCameraPosition()
    {
        Vector3 offset = new(
            _distance * Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            _distance * Mathf.Sin(_pitch),
            _distance * Mathf.Cos(_pitch) * Mathf.Cos(_yaw));

        GlobalPosition = _targetPosition + offset;
        LookAt(_targetPosition, Vector3.Up);
    }
}
