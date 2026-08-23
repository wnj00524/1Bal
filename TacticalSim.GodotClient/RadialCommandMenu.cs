using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NumericsVector3 = System.Numerics.Vector3;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;

namespace TacticalSim.GodotClient;

/// <summary>
/// Converts a screen-space command choice into a core command payload.  The menu
/// never changes an entity or world; its only output is <see cref="ActionDispatched"/>.
/// </summary>
public partial class RadialCommandMenu : Control
{
    public enum Command { Move, Shoot, Aim, Wait, Medical }

    public sealed record CommandContext(
        IEntity Actor,
        NumericsVector3 ActorPosition,
        NumericsVector3 GridPosition,
        Guid? TargetEntityId = null,
        float MoveSpeedMetersPerSecond = 1.4f,
        float AimTuCost = 5f,
        float WaitTuCost = 5f,
        Func<CommandContext, ShootTacticalAction>? CreateShot = null);

    private const float OuterRadius = 76f;
    private const float InnerRadius = 22f;
    private static readonly Color LockedTint = new("657080");

    public event Action? MoveSelected;       // Compatibility notifications for existing scenes.
    public event Action? ShootSelected;
    public event Action? MedicalSelected;
    public event Action<TacticalAction>? ActionDispatched;

    [Export] public NodePath? SimulationManagerPath { get; set; }

    private IReadOnlyList<Command> _availableCommands = new[] { Command.Move };
    private CommandContext? _context;
    private SimulationManager? _simulationManager;
    private bool _locked;

    public Command ActiveCommand { get; private set; } = Command.Move;
    public bool IsInputLocked => _locked;

    public void SetContext(CommandContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public void ShowCommand(Command command) => ShowCommands(new[] { command });

    public void ShowCommands(IEnumerable<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
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
        SimulationManager? manager = SimulationManagerPath is not null && !SimulationManagerPath.IsEmpty
            ? GetNodeOrNull<SimulationManager>(SimulationManagerPath)
            : GetTree().Root.FindChild("SimulationManager", true, false) as SimulationManager;
        BindSimulationManager(manager);
        QueueRedraw();
    }

    public void BindSimulationManager(SimulationManager? manager)
    {
        UnbindSimulationManager();
        _simulationManager = manager;
        if (manager is null) return;
        manager.ActionStarted += OnResolutionStarted;
        manager.ActionCompleted += OnResolutionEnded;
        manager.ActionCancelled += OnResolutionEnded;
        manager.ActionFailed += OnResolutionFailed;
    }

    public override void _ExitTree() => UnbindSimulationManager();

    private void UnbindSimulationManager()
    {
        if (_simulationManager is null) return;
        _simulationManager.ActionStarted -= OnResolutionStarted;
        _simulationManager.ActionCompleted -= OnResolutionEnded;
        _simulationManager.ActionCancelled -= OnResolutionEnded;
        _simulationManager.ActionFailed -= OnResolutionFailed;
        _simulationManager = null;
    }

    private void OnResolutionStarted(string _, float __) => SetLocked(true);
    private void OnResolutionEnded(string _, float __) => SetLocked(false);
    private void OnResolutionFailed(string _, string __, float ___) => SetLocked(false);

    private void SetLocked(bool value)
    {
        if (_locked == value) return;
        _locked = value;
        MouseFilter = value ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        MouseDefaultCursorShape = value ? CursorShape.Forbidden : CursorShape.PointingHand;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2f;
        int count = _availableCommands.Count;
        for (int i = 0; i < count; i++)
        {
            float from = -Mathf.Pi / 2f + Mathf.Tau * i / count;
            float to = -Mathf.Pi / 2f + Mathf.Tau * (i + 1) / count;
            DrawColoredPolygon(BuildSector(center, from, to), _locked ? LockedTint : new Color("3f70a8"));
            float middle = (from + to) / 2f;
            string label = _availableCommands[i].ToString().ToUpperInvariant();
            Vector2 labelCenter = center + Vector2.FromAngle(middle) * ((InnerRadius + OuterRadius) / 2f);
            Vector2 textSize = ThemeDB.FallbackFont.GetStringSize(label, HorizontalAlignment.Left, -1, 14);
            DrawString(ThemeDB.FallbackFont, labelCenter - new Vector2(textSize.X / 2f, -textSize.Y / 3f),
                label, HorizontalAlignment.Left, -1, 14, Colors.White);
        }
        DrawCircle(center, InnerRadius, new Color("17202d"));
        if (_locked)
            DrawString(ThemeDB.FallbackFont, center + new Vector2(-24, 5), "LOCKED", HorizontalAlignment.Left, -1, 11, Colors.White);
    }

    private static Vector2[] BuildSector(Vector2 center, float from, float to)
    {
        const int steps = 12;
        var points = new List<Vector2>((steps + 1) * 2);
        for (int i = 0; i <= steps; i++) points.Add(center + Vector2.FromAngle(Mathf.Lerp(from, to, i / (float)steps)) * OuterRadius);
        for (int i = steps; i >= 0; i--) points.Add(center + Vector2.FromAngle(Mathf.Lerp(from, to, i / (float)steps)) * InnerRadius);
        return points.ToArray();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_locked || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;
        Vector2 offset = click.Position - Size / 2f;
        float distance = offset.Length();
        if (distance < InnerRadius || distance > OuterRadius) return;

        float clockwise = Mathf.PosMod(offset.Angle() + Mathf.Pi / 2f, Mathf.Tau);
        int index = Math.Min((int)(clockwise / Mathf.Tau * _availableCommands.Count), _availableCommands.Count - 1);
        ActiveCommand = _availableCommands[index];
        DispatchSelection();
        AcceptEvent();
    }

    private void DispatchSelection()
    {
        // Legacy scene notifications open follow-up UI. Once a complete context is
        // supplied, commands instead leave this node exclusively as core payloads.
        if (_context is null)
        {
            if (ActiveCommand == Command.Move) MoveSelected?.Invoke();
            else if (ActiveCommand == Command.Shoot) ShootSelected?.Invoke();
            else if (ActiveCommand == Command.Medical) MedicalSelected?.Invoke();
            return;
        }

        TacticalAction action = ActiveCommand switch
        {
            Command.Move => new MoveTacticalAction(_context.Actor, _context.ActorPosition,
                _context.GridPosition, _context.MoveSpeedMetersPerSecond, computeCostFromSpeed: true),
            Command.Aim when _context.TargetEntityId is Guid targetId =>
                new AimTacticalAction(_context.Actor.Id, targetId, _context.AimTuCost),
            Command.Wait => new WaitTacticalAction(_context.Actor.Id, _context.WaitTuCost),
            Command.Shoot when _context.CreateShot is not null => _context.CreateShot(_context),
            Command.Medical => throw new InvalidOperationException("Medical commands require a TacticalAction factory."),
            _ => throw new InvalidOperationException($"The {ActiveCommand} command lacks required target context.")
        };
        SetLocked(true); // Close the race window before the resolver's started signal arrives.
        ActionDispatched?.Invoke(action);
    }
}
