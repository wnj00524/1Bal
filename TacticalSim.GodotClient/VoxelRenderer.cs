using System;
using System.Collections.Generic;
using Godot;
using TacticalSim.Core;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace TacticalSim.GodotClient;

public enum VisualRenderMode
{
    FullVoxels,
    Abstract
}

/// <summary>
/// One draw-batched visualization for an arbitrary physiological voxel set.
/// Once configured, the process loop updates existing MultiMesh storage only;
/// it creates no nodes, meshes, materials, collections, or delegates.
/// </summary>
public partial class VoxelRenderer : Node3D
{
    private static readonly Color DestroyedColor = new(0.05f, 0.05f, 0.05f, 0.95f);

    [Export] public NodePath SimulationManagerPath { get; set; } = null!;
    [Export] public bool DebugHighlightDestroyed { get; set; } = true;
    [Export] public VisualRenderMode RenderMode { get; set; } = VisualRenderMode.Abstract;
    [Export(PropertyHint.Range, "0.1,1.0,0.01")]
    public float FillRatio { get; set; } = 0.9f;

    private MultiMeshInstance3D _instances = null!;
    private MultiMesh _multiMesh = null!;
    private IReadOnlyList<PhysiologicalVoxel>? _voxels;
    private NumericsVector3 _modelOrigin;
    private bool[] _destroyedStates = Array.Empty<bool>();

    public override void _Ready()
    {
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One }
        };
        _instances = new MultiMeshInstance3D
        {
            Multimesh = _multiMesh,
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true
            }
        };
        AddChild(_instances);

        if (!SimulationManagerPath.IsEmpty)
            CallDeferred(nameof(BindInitialSimulation));
    }

    /// <summary>Replaces the source set and uploads its structural data once.</summary>
    public void SetVoxels(IReadOnlyList<PhysiologicalVoxel> voxels, NumericsVector3 modelOrigin)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        _voxels = voxels;
        _modelOrigin = modelOrigin;

        int count = voxels.Count;
        if (_multiMesh.InstanceCount != count)
            _multiMesh.InstanceCount = count;
        _multiMesh.VisibleInstanceCount = count;
        if (_destroyedStates.Length != count)
            _destroyedStates = new bool[count];

        for (int i = 0; i < count; i++)
        {
            PhysiologicalVoxel voxel = voxels[i];
            ValidateVoxel(voxel, i);
            UploadTransform(i, voxel);
            _destroyedStates[i] = voxel.IsDestroyed;
            _multiMesh.SetInstanceColor(i, ResolveColor(voxel));
        }

        UpdateCustomBounds(voxels, modelOrigin);
    }

    /// <summary>
    /// Assigns a conservative world volume to Godot's visibility system. This
    /// lets the renderer reject the complete batch before submitting instances.
    /// </summary>
    public void SetActiveWorldBounds(in WorldBounds bounds)
    {
        NumericsVector3 size = bounds.Size;
        _instances.CustomAabb = new Aabb(
            new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new Vector3(size.X, size.Y, size.Z));
    }

    public override void _Process(double delta)
    {
        _ = delta;
        IReadOnlyList<PhysiologicalVoxel>? voxels = _voxels;
        if (voxels is null)
            return;

        // Damage is mutable core output, but rendering it only updates GPU-side
        // instance colors. Structural transforms remain unchanged.
        int count = Math.Min(voxels.Count, _destroyedStates.Length);
        for (int i = 0; i < count; i++)
        {
            PhysiologicalVoxel voxel = voxels[i];
            if (_destroyedStates[i] == voxel.IsDestroyed)
                continue;
            _destroyedStates[i] = voxel.IsDestroyed;
            UploadTransform(i, voxel);
            _multiMesh.SetInstanceColor(i, ResolveColor(voxel));
        }
    }

    private void BindInitialSimulation()
    {
        SimulationManager manager = GetNode<SimulationManager>(SimulationManagerPath);
        if (manager.Dummy is null)
            return;
        SetVoxels(manager.Dummy.Physiology.RootBodyPart.Voxels, manager.Dummy.Position);
    }

    private void UploadTransform(int index, PhysiologicalVoxel voxel)
    {
        NumericsVector3 position = _modelOrigin + voxel.Center;
        float scale = voxel.Size * Mathf.Clamp(FillRatio, 0.1f, 1.0f);
        Vector3 visibleScale = RenderMode == VisualRenderMode.Abstract && !voxel.IsDestroyed
            ? Vector3.Zero
            : new Vector3(scale, scale, scale);
        _multiMesh.SetInstanceTransform(index,
            new Transform3D(Basis.Identity.Scaled(visibleScale),
                new Vector3(position.X, position.Y, position.Z)));
    }

    private void UpdateCustomBounds(IReadOnlyList<PhysiologicalVoxel> voxels, NumericsVector3 origin)
    {
        if (voxels.Count == 0)
        {
            _instances.CustomAabb = default;
            return;
        }

        NumericsVector3 min = origin + voxels[0].MinBounds;
        NumericsVector3 max = origin + voxels[0].MaxBounds;
        for (int i = 1; i < voxels.Count; i++)
        {
            min = NumericsVector3.Min(min, origin + voxels[i].MinBounds);
            max = NumericsVector3.Max(max, origin + voxels[i].MaxBounds);
        }
        NumericsVector3 size = max - min;
        _instances.CustomAabb = new Aabb(
            new Vector3(min.X, min.Y, min.Z), new Vector3(size.X, size.Y, size.Z));
    }

    private Color ResolveColor(PhysiologicalVoxel voxel)
    {
        if (voxel.IsDestroyed && DebugHighlightDestroyed)
            return DestroyedColor;
        return voxel.Organ switch
        {
            OrganType.Muscle => new Color(0.8f, 0.2f, 0.2f, 0.35f),
            OrganType.Lung => new Color(0.9f, 0.6f, 0.6f, 0.55f),
            OrganType.Heart => new Color(0.9f, 0.1f, 0.1f, 0.95f),
            OrganType.Liver => new Color(0.6f, 0.1f, 0.1f, 0.9f),
            OrganType.Stomach => new Color(0.8f, 0.8f, 0.3f, 0.75f),
            OrganType.Bone => new Color(0.9f, 0.9f, 0.9f, 1.0f),
            _ => new Color(0.5f, 0.5f, 0.5f, 0.5f)
        };
    }

    private static void ValidateVoxel(PhysiologicalVoxel voxel, int index)
    {
        if (voxel is null)
            throw new ArgumentException($"Voxel at index {index} is null.", nameof(voxel));
        if (!(voxel.Size > 0.0f) || !float.IsFinite(voxel.Size) ||
            !float.IsFinite(voxel.Center.X) || !float.IsFinite(voxel.Center.Y) ||
            !float.IsFinite(voxel.Center.Z))
            throw new ArgumentException($"Voxel at index {index} has invalid geometry.", nameof(voxel));
    }
}
