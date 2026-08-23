using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using NumericsVector3 = System.Numerics.Vector3;

namespace TacticalSim.GodotClient;

/// <summary>Data-driven orthographic projection of an actor's anatomical volumes.</summary>
public partial class TargetSilhouetteMenu : Control
{
    [Signal] public delegate void TargetSelectedEventHandler(string target);

    public sealed record AnatomicalTarget(Guid EntityId, BodyPartType Region,
        NumericsVector3 BodyLocalPoint, IReadOnlyList<string> StructureIds);

    private sealed record TargetRegion(BodyPartType Region, string Label, Vector2[] Polygon,
        NumericsVector3 LocalCenter, IReadOnlyList<string> StructureIds);

    [Export] public NodePath? SimulationManagerPath { get; set; }
    [Export(PropertyHint.Range, "0,32,1")] public float PolygonPaddingPixels { get; set; } = 5f;

    public event Action<AnatomicalTarget>? AnatomicalTargetSelected;

    private readonly List<TargetRegion> _regions = new();
    private SimulationManager? _simulationManager;
    private TacticalEntity? _target;
    private BodyPartType? _hoveredRegion;
    private bool _locked;

    public bool IsInputLocked => _locked;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(220, 360);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += OnMouseExited;
        SimulationManager? manager = SimulationManagerPath is not null && !SimulationManagerPath.IsEmpty
            ? GetNodeOrNull<SimulationManager>(SimulationManagerPath)
            : GetTree().Root.FindChild("SimulationManager", true, false) as SimulationManager;
        BindSimulationManager(manager);
        if (manager?.Dummy is not null) SetTarget(manager.Dummy);
    }

    public void SetTarget(TacticalEntity actor)
    {
        _target = actor ?? throw new ArgumentNullException(nameof(actor));
        RebuildRegions(actor.Physiology);
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

    public override void _ExitTree()
    {
        MouseExited -= OnMouseExited;
        UnbindSimulationManager();
    }

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
    private void OnMouseExited() => SetHoveredRegion(null);

    private void SetLocked(bool value)
    {
        _locked = value;
        MouseFilter = value ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        MouseDefaultCursorShape = value ? CursorShape.Forbidden : CursorShape.PointingHand;
        QueueRedraw();
    }

    private void RebuildRegions(IActorPhysiology physiology)
    {
        ArgumentNullException.ThrowIfNull(physiology);
        var samples = new List<(BodyPartType Region, NumericsVector3 Center, float Radius)>();
        CollectVoxels(physiology.RootBodyPart, samples);

        IAnatomicalStructureCatalog? anatomy = (physiology as TacticalActorPhysiology)?.Anatomy;
        if (anatomy is not null)
            samples.AddRange(anatomy.Structures.SelectMany(s => new[]
            {
                (s.Region, s.Start, s.Radius.Meters), (s.Region, s.End, s.Radius.Meters)
            }));
        if (samples.Count == 0)
            throw new InvalidOperationException("The target physiology contains no voxel or structural geometry.");

        float minX = samples.Min(s => s.Center.X - s.Radius), maxX = samples.Max(s => s.Center.X + s.Radius);
        float minY = samples.Min(s => s.Center.Y - s.Radius), maxY = samples.Max(s => s.Center.Y + s.Radius);
        const float margin = 12f;
        float scale = MathF.Min((220f - margin * 2f) / MathF.Max(maxX - minX, .001f),
            (336f - margin * 2f) / MathF.Max(maxY - minY, .001f));

        _regions.Clear();
        foreach (var group in samples.GroupBy(s => s.Region).OrderBy(g => g.Key))
        {
            var points = new List<Vector2>();
            foreach (var sample in group)
            {
                Vector2 p = Project(sample.Center, minX, maxY, scale, margin);
                float radius = MathF.Max(PolygonPaddingPixels, sample.Radius * scale + PolygonPaddingPixels);
                points.AddRange(new[] { p + new Vector2(-radius, -radius), p + new Vector2(radius, -radius),
                    p + new Vector2(radius, radius), p + new Vector2(-radius, radius) });
            }
            Vector2[] hull = ConvexHull(points);
            NumericsVector3 center = new(group.Average(s => s.Center.X), group.Average(s => s.Center.Y),
                group.Average(s => s.Center.Z));
            string[] ids = anatomy?.Structures.Where(s => s.Region == group.Key)
                .Select(s => s.Id).Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
            _regions.Add(new TargetRegion(group.Key, DisplayName(group.Key), hull, center, ids));
        }
        QueueRedraw();
    }

    private static void CollectVoxels(BodyPart part,
        ICollection<(BodyPartType Region, NumericsVector3 Center, float Radius)> samples)
    {
        foreach (PhysiologicalVoxel voxel in part.Voxels)
            samples.Add((part.Type, voxel.Center, voxel.Size / 2f));
        foreach (BodyPart child in part.Children) CollectVoxels(child, samples);
    }

    private static Vector2 Project(NumericsVector3 p, float minX, float maxY, float scale, float margin) =>
        new(margin + (p.X - minX) * scale, margin + (maxY - p.Y) * scale);

    private static Vector2[] ConvexHull(IEnumerable<Vector2> source)
    {
        Vector2[] points = source.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (points.Length <= 2) return points;
        var hull = new List<Vector2>();
        foreach (Vector2 p in points)
        {
            while (hull.Count >= 2 && Cross(hull[^1] - hull[^2], p - hull[^1]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lower = hull.Count;
        for (int i = points.Length - 2; i >= 0; i--)
        {
            Vector2 p = points[i];
            while (hull.Count > lower && Cross(hull[^1] - hull[^2], p - hull[^1]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    public override void _GuiInput(InputEvent @event)
    {
        if (_locked) return;
        if (@event is InputEventMouseMotion motion) { SetHoveredRegion(RegionAt(motion.Position)?.Region); return; }
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) return;
        TargetRegion? region = RegionAt(click.Position);
        if (region is null || _target is null) return;
        AnatomicalTargetSelected?.Invoke(new AnatomicalTarget(_target.Id, region.Region,
            region.LocalCenter, region.StructureIds));
        EmitSignal(SignalName.TargetSelected, region.Label);
        AcceptEvent();
    }

    public override void _Draw()
    {
        foreach (TargetRegion region in _regions)
        {
            Color fill = _locked ? new Color("59616c") : region.Region == _hoveredRegion
                ? new Color("dc9d45") : new Color("45647a");
            DrawColoredPolygon(region.Polygon, fill);
            DrawPolyline(region.Polygon.Append(region.Polygon[0]).ToArray(), new Color("b9d2df"), 2f);
        }
        string instruction = _locked ? "Resolving turn…" : _regions.FirstOrDefault(r => r.Region == _hoveredRegion)?.Label ?? "Choose target area";
        DrawString(ThemeDB.FallbackFont, new Vector2(5, 355), instruction,
            HorizontalAlignment.Center, 210, 16, new Color("e8f1f5"));
    }

    private TargetRegion? RegionAt(Vector2 position) =>
        _regions.AsEnumerable().Reverse().FirstOrDefault(r => Geometry2D.IsPointInPolygon(position, r.Polygon));

    private void SetHoveredRegion(BodyPartType? region)
    {
        if (_hoveredRegion == region) return;
        _hoveredRegion = region;
        QueueRedraw();
    }

    private static string DisplayName(BodyPartType region) => region switch
    {
        BodyPartType.Thorax => "Chest",
        BodyPartType.LeftArm => "Left Arm",
        BodyPartType.RightArm => "Right Arm",
        BodyPartType.LeftLeg => "Left Leg",
        BodyPartType.RightLeg => "Right Leg",
        _ => region.ToString()
    };
}
