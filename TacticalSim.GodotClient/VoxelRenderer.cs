using Godot;
using System.Collections.Generic;
using TacticalSim.Core.Physiology;

namespace TacticalSim.GodotClient
{
    public enum VisualRenderMode
    {
        FullVoxels,
        Abstract
    }

    public partial class VoxelRenderer : Node3D
    {
        [Export]
        public NodePath SimulationManagerPath { get; set; } = null!;
        [Export] 
        public bool DebugHighlightDestroyed { get; set; } = true;
        [Export]
        public VisualRenderMode RenderMode { get; set; } = VisualRenderMode.Abstract;
        
        private SimulationManager _simulationManager = null!;
        private MultiMeshInstance3D _multiMeshInstance = null!;
        private MultiMesh _multiMesh = null!;
        private MeshInstance3D _abstractBodyProxy = null!;

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            CallDeferred(nameof(GenerateVoxelMeshes));
        }

        private void GenerateVoxelMeshes()
        {
            var dummy = _simulationManager.Dummy;
            if (dummy == null) return;
            var voxels = dummy.Physiology.RootBodyPart.Voxels;
            if (voxels.Count == 0) return;

            // 1. Create the Abstract Body Proxy (Capsule)
            _abstractBodyProxy = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.25f, Height = 0.8f }, // Approximate torso dimensions
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.3f, 0.5f, 0.8f, 0.2f), // Translucent blue
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                },
                Position = new Godot.Vector3(dummy.Position.X, dummy.Position.Y + 0.4f, dummy.Position.Z) // Center torso
            };
            
            if (RenderMode == VisualRenderMode.Abstract)
            {
                AddChild(_abstractBodyProxy);
            }

            // 2. Setup Voxel MultiMesh
            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                InstanceCount = voxels.Count
            };

            float visualSize = voxels[0].Size * 0.9f;
            _multiMesh.Mesh = new BoxMesh { Size = new Godot.Vector3(visualSize, visualSize, visualSize) };

            for (int i = 0; i < voxels.Count; i++)
            {
                var voxel = voxels[i];
                System.Numerics.Vector3 globalPos = dummy.Position + voxel.Center;
                
                // If abstract, hide all voxels initially by scaling to zero
                var initialScale = RenderMode == VisualRenderMode.Abstract ? Godot.Vector3.Zero : Godot.Vector3.One;
                
                _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(initialScale), new Godot.Vector3(globalPos.X, globalPos.Y, globalPos.Z)));
                _multiMesh.SetInstanceColor(i, GetColorForOrgan(voxel.Organ));
            }

            _multiMeshInstance = new MultiMeshInstance3D
            {
                Multimesh = _multiMesh,
                MaterialOverride = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    VertexColorUseAsAlbedo = true
                }
            };

            AddChild(_multiMeshInstance);
        }

        public void RefreshVoxels(IActorPhysiology dummyPhysiology, List<(float Time, TacticalSim.Core.Physiology.CavitationEvent Cav)> cavEvents, float currentTime, System.Numerics.Vector3 entityPos)
        {
            var voxels = dummyPhysiology.RootBodyPart.Voxels;
            if (_multiMesh == null || voxels.Count != _multiMesh.InstanceCount) return;

            float cavityLifetime = 0.005f; // 5ms

            for (int i = 0; i < voxels.Count; i++)
            {
                var voxel = voxels[i];
                var baseColor = GetColorForOrgan(voxel.Organ);

                if (voxel.IsDestroyed) 
                {
                    if (DebugHighlightDestroyed)
                    {
                        _multiMesh.SetInstanceColor(i, new Color(1.0f, 1.0f, 0.0f, 1.0f)); // Solid yellow
                        System.Numerics.Vector3 pos = entityPos + voxel.Center;
                        _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Godot.Vector3(pos.X, pos.Y, pos.Z)));
                    }
                    else
                    {
                        _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Godot.Vector3.Zero), Godot.Vector3.Zero));
                    }
                    continue;
                }

                _multiMesh.SetInstanceColor(i, baseColor);

                bool isTemporarilyDisplaced = false;
                System.Numerics.Vector3 globalVoxelCenter = entityPos + voxel.Center;

                foreach (var ev in cavEvents)
                {
                    float age = currentTime - ev.Time;
                    if (age < 0 || age > cavityLifetime) continue;

                    float scale = System.MathF.Sin((age / cavityLifetime) * System.MathF.PI);
                    float currentRadius = ev.Cav.Radius * scale;
                    
                    System.Numerics.Vector3 globalOrigin = entityPos + ev.Cav.Origin;
                    float distSq = (globalVoxelCenter - globalOrigin).LengthSquared();
                    
                    if (distSq < (currentRadius * currentRadius))
                    {
                        isTemporarilyDisplaced = true;
                        break;
                    }
                }

                if (isTemporarilyDisplaced)
                {
                    // Hide during temporary displacement
                    _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Godot.Vector3.Zero), Godot.Vector3.Zero));
                }
                else
                {
                    // Restore to normal (if FullVoxels, scale to One; if Abstract, scale to Zero to stay hidden)
                    var targetScale = RenderMode == VisualRenderMode.Abstract ? Godot.Vector3.Zero : Godot.Vector3.One;
                    System.Numerics.Vector3 pos = entityPos + voxel.Center;
                    _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(targetScale), new Godot.Vector3(pos.X, pos.Y, pos.Z)));
                }
            }
        }

        private Color GetColorForOrgan(OrganType organ)
        {
            return organ switch
            {
                OrganType.Muscle => new Color(0.8f, 0.2f, 0.2f, 0.2f),
                OrganType.Lung => new Color(0.9f, 0.6f, 0.6f, 0.5f),
                OrganType.Heart => new Color(0.9f, 0.1f, 0.1f, 0.9f),
                OrganType.Liver => new Color(0.6f, 0.1f, 0.1f, 0.9f),
                OrganType.Stomach => new Color(0.8f, 0.8f, 0.3f, 0.7f),
                OrganType.Bone => new Color(0.9f, 0.9f, 0.9f, 1.0f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.5f)
            };
        }
    }
}
