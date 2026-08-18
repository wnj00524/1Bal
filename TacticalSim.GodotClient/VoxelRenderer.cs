using Godot;
using System.Collections.Generic;
using TacticalSim.Core.Physiology;

namespace TacticalSim.GodotClient
{
    public partial class VoxelRenderer : Node3D
    {
        [Export]
        public NodePath SimulationManagerPath { get; set; } = null!;
        [Export] 
        public bool DebugHighlightDestroyed { get; set; } = true;
        
        private SimulationManager _simulationManager = null!;
        private MultiMeshInstance3D _multiMeshInstance = null!;
        private MultiMesh _multiMesh = null!;

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

            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                InstanceCount = voxels.Count
            };

            float visualSize = voxels[0].Size * 0.9f; // 10% gap
            _multiMesh.Mesh = new BoxMesh { Size = new Godot.Vector3(visualSize, visualSize, visualSize) };

            for (int i = 0; i < voxels.Count; i++)
            {
                var voxel = voxels[i];
                System.Numerics.Vector3 globalPos = dummy.Position + voxel.Center;
                
                _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Godot.Vector3(globalPos.X, globalPos.Y, globalPos.Z)));
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
                        // Hide by scaling to zero
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
                    _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Godot.Vector3.Zero), Godot.Vector3.Zero));
                }
                else
                {
                    System.Numerics.Vector3 pos = entityPos + voxel.Center;
                    _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Godot.Vector3(pos.X, pos.Y, pos.Z)));
                }
            }
        }

        private Color GetColorForOrgan(OrganType organ)
        {
            return organ switch
            {
                OrganType.Muscle => new Color(0.8f, 0.2f, 0.2f, 0.2f), // Slightly more transparent so we can see inside!
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
