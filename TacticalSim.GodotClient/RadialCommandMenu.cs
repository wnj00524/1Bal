using Godot;
using System;

namespace TacticalSim.GodotClient;

/// <summary>A compact contextual wheel which requires an explicit command selection.</summary>
public partial class RadialCommandMenu : Control
{
    private const float OuterRadius = 76f;
    private const float InnerRadius = 22f;

    public event Action? MoveSelected;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(OuterRadius * 2f, OuterRadius * 2f);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2f;
        DrawCircle(center, OuterRadius, new Color("263449"));
        DrawCircle(center, OuterRadius - 4f, new Color("3f70a8"));
        DrawCircle(center, InnerRadius, new Color("17202d"));

        Font font = ThemeDB.FallbackFont;
        const string label = "MOVE";
        Vector2 textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, 18);
        DrawString(font, center - textSize / 2f + new Vector2(0, textSize.Y), label,
            HorizontalAlignment.Left, -1, 18, Colors.White);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            } click)
            return;

        float distance = click.Position.DistanceTo(Size / 2f);
        if (distance > OuterRadius)
            return;

        MoveSelected?.Invoke();
        AcceptEvent();
    }
}
