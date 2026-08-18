using Godot;
using System.Collections.Generic;
using TacticalSim.Core.Physiology;

namespace TacticalSim.GodotClient
{
    public partial class VoxelRenderer : Node3D
    {
        [Export]
        public NodePath SimulationManagerPath { get; set; } = null!;
        
        private SimulationManager _simulationManager = null!;
        private List<MeshInstance3D> _voxelMeshes = new();

        public override void _Ready()
        {
            _simulationManager = GetNode<SimulationManager>(SimulationManagerPath);
            CallDeferred(nameof(GenerateVoxelMeshes));
        }

        private void GenerateVoxelMeshes()
        {
            var dummy = _simulationManager.Dummy;
            if (dummy == null) return;

            foreach (var voxel in dummy.Physiology.RootBodyPart.Voxels)
            {
                var meshInstance = new MeshInstance3D();
                var boxMesh = new BoxMesh
                {
                    Size = new Godot.Vector3(voxel.Size, voxel.Size, voxel.Size)
                };
                meshInstance.Mesh = boxMesh;
                
                // Map C# System.Numerics.Vector3 to Godot Vector3
                meshInstance.Position = new Godot.Vector3(voxel.Center.X, voxel.Center.Y, voxel.Center.Z);

                var material = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = GetColorForOrgan(voxel.Organ)
                };
                
                meshInstance.MaterialOverride = material;
                
                // If voxel was destroyed in the simulation run, hide it
                if (voxel.IsDestroyed)
                {
                    meshInstance.Visible = false;
                }

                AddChild(meshInstance);
                _voxelMeshes.Add(meshInstance);
            }
        }

        private Color GetColorForOrgan(OrganType organ)
        {
            return organ switch
            {
                OrganType.Muscle => new Color(0.8f, 0.2f, 0.2f, 0.4f),
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
