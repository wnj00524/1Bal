using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalSim.GodotClient;

/// <summary>A compact contextual wheel which requires an explicit command selection.</summary>
public partial class RadialCommandMenu : Control
{
    public enum Command
    {
        Move,
        Shoot,
        Medical
    }

    private const float OuterRadius = 76f;
    private const float InnerRadius = 22f;

    public event Action? MoveSelected;
    public event Action? ShootSelected;
    public event Action? MedicalSelected;

    private IReadOnlyList<Command> _availableCommands = new[] { Command.Move };

    public Command ActiveCommand { get; private set; } = Command.Move;

    public void ShowCommand(Command command)
    {
        ActiveCommand = command;
        _availableCommands = new[] { command };
        QueueRedraw();
    }

    public void ShowCommands(IEnumerable<Command> commands)
    {
        _availableCommands = commands.Distinct().ToArray();
        if (_availableCommands.Count == 0)
            throw new ArgumentException("At least one command is required.", nameof(commands));

        ActiveCommand = _availableCommands[0];
        QueueRedraw();
    }

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
        for (int index = 0; index < _availableCommands.Count; index++)
        {
            string label = _availableCommands[index].ToString().ToUpperInvariant();
            Vector2 textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, 18);
            float y = center.Y + ((index - ((_availableCommands.Count - 1) / 2f)) * 28f);
            DrawString(font, new Vector2(center.X - textSize.X / 2f, y + textSize.Y / 2f), label,
                HorizontalAlignment.Left, -1, 18, Colors.White);
        }
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

        int selectedIndex = _availableCommands.Count == 1
            ? 0
            : Math.Clamp((int)(click.Position.Y / Size.Y * _availableCommands.Count), 0, _availableCommands.Count - 1);
        ActiveCommand = _availableCommands[selectedIndex];
        if (ActiveCommand == Command.Shoot)
            ShootSelected?.Invoke();
        else if (ActiveCommand == Command.Medical)
            MedicalSelected?.Invoke();
        else
            MoveSelected?.Invoke();
        AcceptEvent();
    }
}
