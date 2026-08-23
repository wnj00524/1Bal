using System;
using Godot;
using TacticalSim.Core.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace TacticalSim.GodotClient;

/// <summary>
/// Orthographic tactical camera. Input changes only the focal point, yaw, and
/// orthographic size; the elevation angle is invariant, so perspective
/// distortion can never be introduced by orbiting.
/// </summary>
public partial class CameraOrbit : Camera3D
{
    private const float IsometricElevation = 0.6154797086703874f; // asin(1 / sqrt(3))
    private const double ParallelEpsilon = 1e-10;

    [Export] public float MoveSpeed { get; set; } = 8.0f;
    [Export] public float PanSensitivity { get; set; } = 0.0025f;
    [Export] public float RotationSpeed { get; set; } = 0.005f;
    [Export] public float ZoomSpeed { get; set; } = 0.12f;
    [Export] public float MinZoom { get; set; } = 2.0f;
    [Export] public float MaxZoom { get; set; } = 80.0f;
    [Export] public float OrbitDistance { get; set; } = 100.0f;
    [Export] public float GridCellSize { get; set; } = 1.0f;
    [Export] public Vector3 BoundsMinimum { get; set; } = new(-50.0f, 0.0f, -50.0f);
    [Export] public Vector3 BoundsMaximum { get; set; } = new(50.0f, 30.0f, 50.0f);

    private WorldBounds _worldBounds;
    private Vector3 _focus;
    private float _yaw = Mathf.Pi / 4.0f;
    private bool _isPanning;

    public WorldBounds ActiveBounds => _worldBounds;
    public Vector3 FocalPoint => _focus;

    public override void _Ready()
    {
        Projection = ProjectionType.Orthogonal;
        Size = Mathf.Clamp(Size, MinZoom, MaxZoom);
        ConfigureBounds(CreateBounds(BoundsMinimum, BoundsMaximum));
    }

    public void ConfigureBounds(in WorldBounds bounds)
    {
        _worldBounds = bounds;
        _focus = ToGodot(bounds.Centre);
        _focus.Y = bounds.Min.Y;

        // Tight clipping improves depth precision while retaining every point in
        // the tactical AABB for every allowed yaw.
        float diagonal = ToGodot(bounds.Size).Length();
        OrbitDistance = Mathf.Max(OrbitDistance, diagonal + 1.0f);
        Near = 0.05f;
        Far = (OrbitDistance + diagonal) * 2.0f;
        UpdateCameraTransform();
    }

    public override void _Process(double delta)
    {
        Vector3 forward = -GlobalBasis.Z;
        Vector3 right = GlobalBasis.X;
        forward.Y = 0.0f;
        right.Y = 0.0f;
        forward = forward.Normalized();
        right = right.Normalized();

        Vector3 movement = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) movement += forward;
        if (Input.IsPhysicalKeyPressed(Key.S)) movement -= forward;
        if (Input.IsPhysicalKeyPressed(Key.A)) movement -= right;
        if (Input.IsPhysicalKeyPressed(Key.D)) movement += right;

        if (!movement.IsZeroApprox())
        {
            MoveFocus(movement.Normalized() * MoveSpeed * (float)delta);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle)
                _isPanning = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                ApplyZoom(-1.0f);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                ApplyZoom(1.0f);
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Bracketleft) ApplyZoom(1.0f);
            else if (key.Keycode == Key.Bracketright) ApplyZoom(-1.0f);
            return;
        }

        if (@event is not InputEventMouseMotion motion)
            return;

        if (_isPanning)
        {
            // Size converts pixels to a viewport-independent world-space scale.
            float scale = Size * PanSensitivity;
            Vector3 delta = (-GlobalBasis.X * motion.Relative.X + GlobalBasis.Y * motion.Relative.Y) * scale;
            delta.Y = 0.0f;
            MoveFocus(delta);
        }

        if (motion.AltPressed)
        {
            _yaw = Mathf.PosMod(_yaw - motion.Relative.X * RotationSpeed, Mathf.Tau);
            UpdateCameraTransform();
        }
    }

    /// <summary>
    /// Pure screen-to-grid query against a horizontal topological plane. Neither
    /// the camera nor the supplied immutable bounds are modified.
    /// </summary>
    public bool TryScreenToGrid(Vector2 screenPosition, out Vector3 worldPosition,
        out Vector3I gridIndex) =>
        TryScreenToGrid(screenPosition, _worldBounds.Min.Y, GridCellSize,
            out worldPosition, out gridIndex);

    public bool TryScreenToGrid(Vector2 screenPosition, float planeY, float cellSize,
        out Vector3 worldPosition, out Vector3I gridIndex)
    {
        Vector3 rayOrigin = ProjectRayOrigin(screenPosition);
        Vector3 rayDirection = ProjectRayNormal(screenPosition);
        return TryRayToGrid(rayOrigin, rayDirection, planeY, cellSize, _worldBounds,
            out worldPosition, out gridIndex);
    }

    public static bool TryRayToGrid(Vector3 rayOrigin, Vector3 rayDirection, float planeY,
        float cellSize, in WorldBounds bounds, out Vector3 worldPosition, out Vector3I gridIndex)
    {
        worldPosition = default;
        gridIndex = default;
        if (!(cellSize > 0.0f) || !float.IsFinite(cellSize) ||
            !IsFinite(rayOrigin) || !IsFinite(rayDirection) || !float.IsFinite(planeY))
            return false;

        double denominator = rayDirection.Y;
        if (Math.Abs(denominator) <= ParallelEpsilon)
            return false;

        double t = ((double)planeY - rayOrigin.Y) / denominator;
        if (t < 0.0 || !double.IsFinite(t))
            return false;

        double x = rayOrigin.X + rayDirection.X * t;
        double z = rayOrigin.Z + rayDirection.Z * t;
        double tolerance = Math.Max(cellSize * 1e-6, 1e-7);
        if (x < bounds.Min.X - tolerance || x > bounds.Max.X + tolerance ||
            z < bounds.Min.Z - tolerance || z > bounds.Max.Z + tolerance ||
            planeY < bounds.Min.Y - tolerance || planeY > bounds.Max.Y + tolerance)
            return false;

        x = Math.Clamp(x, bounds.Min.X, bounds.Max.X);
        z = Math.Clamp(z, bounds.Min.Z, bounds.Max.Z);
        int countX = Math.Max(1, (int)Math.Ceiling(bounds.Size.X / cellSize));
        int countZ = Math.Max(1, (int)Math.Ceiling(bounds.Size.Z / cellSize));
        int ix = Math.Clamp(StableFloor((x - bounds.Min.X) / cellSize), 0, countX - 1);
        int iz = Math.Clamp(StableFloor((z - bounds.Min.Z) / cellSize), 0, countZ - 1);

        worldPosition = new Vector3((float)x, planeY, (float)z);
        gridIndex = new Vector3I(ix, 0, iz);
        return true;
    }

    private static int StableFloor(double coordinate)
    {
        double nearest = Math.Round(coordinate);
        if (Math.Abs(coordinate - nearest) <= 1e-7 * Math.Max(1.0, Math.Abs(coordinate)))
            coordinate = nearest;
        return checked((int)Math.Floor(coordinate));
    }

    private void MoveFocus(Vector3 delta)
    {
        NumericsVector3 moved = _worldBounds.Clamp(ToNumerics(_focus + delta));
        _focus = ToGodot(moved);
        _focus.Y = _worldBounds.Min.Y;
        UpdateCameraTransform();
    }

    private void ApplyZoom(float direction)
    {
        Size = Mathf.Clamp(Size * Mathf.Exp(direction * ZoomSpeed), MinZoom, MaxZoom);
    }

    private void UpdateCameraTransform()
    {
        float horizontal = OrbitDistance * Mathf.Cos(IsometricElevation);
        Vector3 offset = new(
            horizontal * Mathf.Sin(_yaw),
            OrbitDistance * Mathf.Sin(IsometricElevation),
            horizontal * Mathf.Cos(_yaw));
        GlobalPosition = _focus + offset;
        LookAt(_focus, Vector3.Up);
    }

    private static WorldBounds CreateBounds(Vector3 min, Vector3 max) =>
        new(ToNumerics(min), ToNumerics(max));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 ToGodot(NumericsVector3 value) => new(value.X, value.Y, value.Z);
}
