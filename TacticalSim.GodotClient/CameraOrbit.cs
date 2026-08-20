using Godot;

namespace TacticalSim.GodotClient;

public partial class CameraOrbit : Camera3D
{
    [Export]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export]
    public float PanSensitivity { get; set; } = 0.01f;

    [Export]
    public float OrbitSensitivity { get; set; } = 0.2f;

    [Export]
    public float ZoomStep { get; set; } = 0.75f;

    private bool _isPanning;
    private bool _isOrbiting;

    public override void _Process(double delta)
    {
        Vector3 movement = Vector3.Zero;
        Vector3 forward = -GlobalTransform.Basis.Z;
        Vector3 right = GlobalTransform.Basis.X;

        forward.Y = 0.0f;
        right.Y = 0.0f;

        if (!forward.IsZeroApprox())
        {
            forward = forward.Normalized();
        }

        if (!right.IsZeroApprox())
        {
            right = right.Normalized();
        }

        if (Input.IsPhysicalKeyPressed(Key.W)) movement += forward;
        if (Input.IsPhysicalKeyPressed(Key.S)) movement -= forward;
        if (Input.IsPhysicalKeyPressed(Key.A)) movement -= right;
        if (Input.IsPhysicalKeyPressed(Key.D)) movement += right;
        if (Input.IsPhysicalKeyPressed(Key.E)) movement += Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.Q)) movement += Vector3.Down;

        if (!movement.IsZeroApprox())
        {
            GlobalPosition += movement.Normalized() * MoveSpeed * (float)delta;
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
                    GlobalPosition -= GlobalTransform.Basis.Z * ZoomStep;
                    break;
                case MouseButton.WheelDown when mouseButton.Pressed:
                    GlobalPosition += GlobalTransform.Basis.Z * ZoomStep;
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
            Vector3 pan =
                (-GlobalTransform.Basis.X * mouseMotion.Relative.X
                 + GlobalTransform.Basis.Y * mouseMotion.Relative.Y)
                * PanSensitivity;

            GlobalPosition += pan;
        }

        if (_isOrbiting)
        {
            Vector3 rotation = RotationDegrees;
            rotation.X = Mathf.Clamp(
                rotation.X - mouseMotion.Relative.Y * OrbitSensitivity,
                -89.0f,
                89.0f);
            rotation.Y -= mouseMotion.Relative.X * OrbitSensitivity;
            rotation.Z = 0.0f;
            RotationDegrees = rotation;
        }
    }
}
