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
                
                // Map C# System.Numerics.Vector3 to Godot Vector3 and apply entity global position
                System.Numerics.Vector3 globalPos = dummy.Position + voxel.Center;
                meshInstance.Position = new Godot.Vector3(globalPos.X, globalPos.Y, globalPos.Z);

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

        public void RefreshVoxels(IActorPhysiology dummyPhysiology, List<(float Time, TacticalSim.Core.Physiology.CavitationEvent Cav)> cavEvents, float currentTime, System.Numerics.Vector3 entityPos)
        {
            var voxels = dummyPhysiology.RootBodyPart.Voxels;
            if (voxels.Count != _voxelMeshes.Count) return;

            float cavityLifetime = 0.005f; // 5ms

            for (int i = 0; i < voxels.Count; i++)
            {
                var voxel = voxels[i];
                if (voxel.IsDestroyed) 
                {
                    _voxelMeshes[i].Visible = false;
                    continue;
                }

                bool isTemporarilyDisplaced = false;
                System.Numerics.Vector3 globalVoxelCenter = entityPos + voxel.Center;

                foreach (var ev in cavEvents)
                {
                    float age = currentTime - ev.Time;
                    if (age < 0 || age > cavityLifetime) continue;

                    // Scale creates a smooth pulse out and back in
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

                _voxelMeshes[i].Visible = !isTemporarilyDisplaced;
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
