using Godot;
using System.Collections.Generic;

namespace TacticalSim.GodotClient;

/// <summary>A compact anatomical picker used by the dummy's context menu.</summary>
public partial class TargetSilhouetteMenu : Control
{
    [Signal]
    public delegate void TargetSelectedEventHandler(string target);

    private sealed record TargetRegion(string Name, Rect2 Bounds);

    private readonly List<TargetRegion> _regions = new()
    {
        new("Head", new Rect2(85, 8, 50, 50)),
        new("Neck", new Rect2(99, 58, 22, 24)),
        new("Chest", new Rect2(67, 82, 86, 78)),
        new("Abdomen", new Rect2(75, 160, 70, 64)),
        new("Left Arm", new Rect2(31, 86, 36, 138)),
        new("Right Arm", new Rect2(153, 86, 36, 138)),
        new("Left Leg", new Rect2(76, 224, 34, 128)),
        new("Right Leg", new Rect2(110, 224, 34, 128))
    };

    private string? _hoveredTarget;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(220, 360);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () => SetHoveredTarget(null);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            SetHoveredTarget(RegionAt(motion.Position)?.Name);
            return;
        }

        if (@event is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            } click)
        {
            TargetRegion? region = RegionAt(click.Position);
            if (region != null)
            {
                EmitSignal(SignalName.TargetSelected, region.Name);
                AcceptEvent();
            }
        }
    }

    public override void _Draw()
    {
        Color normal = new("45647a");
        Color hovered = new("dc9d45");
        Color outline = new("b9d2df");

        foreach (TargetRegion region in _regions)
        {
            Color fill = region.Name == _hoveredTarget ? hovered : normal;
            if (region.Name == "Head")
            {
                DrawCircle(region.Bounds.GetCenter(), region.Bounds.Size.X / 2f, fill);
                DrawArc(region.Bounds.GetCenter(), region.Bounds.Size.X / 2f, 0, Mathf.Tau, 32, outline, 2f);
            }
            else
            {
                DrawRect(region.Bounds, fill, true);
                DrawRect(region.Bounds, outline, false, 2f);
            }
        }

        string instruction = _hoveredTarget ?? "Choose target area";
        DrawString(ThemeDB.FallbackFont, new Vector2(110, 359), instruction,
            HorizontalAlignment.Center, 210, 16, new Color("e8f1f5"));
    }

    private TargetRegion? RegionAt(Vector2 position)
    {
        foreach (TargetRegion region in _regions)
        {
            if (region.Bounds.HasPoint(position))
                return region;
        }

        return null;
    }

    private void SetHoveredTarget(string? target)
    {
        if (_hoveredTarget == target)
            return;

        _hoveredTarget = target;
        QueueRedraw();
    }
}
