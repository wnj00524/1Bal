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
                var boxMesh = new BoxMesh();
                float visualSize = voxel.Size * 0.9f; // 10% gap to see internal organs
                boxMesh.Size = new Godot.Vector3(visualSize, visualSize, visualSize);
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

        public void RefreshVoxels(IActorPhysiology dummyPhysiology)
        {
            var voxels = dummyPhysiology.RootBodyPart.Voxels;
            if (voxels.Count != _voxelMeshes.Count) return;

            for (int i = 0; i < voxels.Count; i++)
            {
                _voxelMeshes[i].Visible = !voxels[i].IsDestroyed;
            }
        }

        private List<MeshInstance3D> _cavitySpheres = new List<MeshInstance3D>();

        public void DrawCavities(List<(float Time, TacticalSim.Core.Physiology.CavitationEvent Cav)> cavEvents, float currentTime)
        {
            // Clear old spheres
            foreach (var sphere in _cavitySpheres)
            {
                sphere.QueueFree();
            }
            _cavitySpheres.Clear();

            // Temporary stretch cavity lasts roughly 5-10ms (0.005 to 0.010 seconds)
            float cavityLifetime = 0.005f;

            foreach (var ev in cavEvents)
            {
                float age = currentTime - ev.Time;
                if (age < 0 || age > cavityLifetime) continue; // Skip if collapsed

                // Scale shrinks from 1.0 down to 0 over cavityLifetime
                float scale = 1.0f - (age / cavityLifetime);

                var meshInstance = new MeshInstance3D();
                var sphereMesh = new SphereMesh();
                
                float rad = ev.Cav.Radius * scale * 2.0f; // diameter
                sphereMesh.Radius = rad * 0.5f;
                sphereMesh.Height = rad;

                var material = new StandardMaterial3D();
                material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.3f); // White translucent pulse
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                
                sphereMesh.Material = material;
                meshInstance.Mesh = sphereMesh;
                
                meshInstance.Position = new Godot.Vector3(ev.Cav.Origin.X, ev.Cav.Origin.Y, ev.Cav.Origin.Z);
                AddChild(meshInstance);
                _cavitySpheres.Add(meshInstance);
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
