using System.Numerics;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core
{
    public static class AnatomicalDummyBuilder
    {
        public static IActorPhysiology BuildDummy()
        {
            var physiology = new DummyPhysiology();
            
            var root = new BodyPart { Type = BodyPartType.Thorax };
            physiology.SetRoot(root);
            
            var head = new BodyPart { Type = BodyPartType.Head, Parent = root };
            var leftArm = new BodyPart { Type = BodyPartType.LeftArm, Parent = root };
            var rightArm = new BodyPart { Type = BodyPartType.RightArm, Parent = root };
            var leftLeg = new BodyPart { Type = BodyPartType.LeftLeg, Parent = root };
            var rightLeg = new BodyPart { Type = BodyPartType.RightLeg, Parent = root };
            
            root.Children.Add(head);
            root.Children.Add(leftArm);
            root.Children.Add(rightArm);
            root.Children.Add(leftLeg);
            root.Children.Add(rightLeg);
            
            PopulateTorsoVoxels(root);
            
            return physiology;
        }
        
        private static void PopulateTorsoVoxels(BodyPart torso)
        {
            float voxelSize = 0.05f; // 5cm voxels for scaffolding
            
            // Generate a simple grid for the Torso
            // X: -0.2 to 0.2, Y: 0.0 to 0.5, Z: -0.1 to 0.1
            for (float x = -0.2f; x <= 0.2f; x += voxelSize)
            {
                for (float y = 0.0f; y <= 0.5f; y += voxelSize)
                {
                    for (float z = -0.1f; z <= 0.1f; z += voxelSize)
                    {
                        var pos = new Vector3(x, y, z);
                        var (tissue, organ) = DetermineOrganAtPosition(pos);
                        var voxel = new PhysiologicalVoxel(pos, voxelSize, tissue, organ);
                        torso.Voxels.Add(voxel);
                    }
                }
            }
        }
        
        private static (TissueProperties, OrganType) DetermineOrganAtPosition(Vector3 pos)
        {
            // Simple geometric layout within the torso
            // Heart: left-center of upper chest
            if (pos.X > 0.0f && pos.X < 0.1f && pos.Y > 0.3f && pos.Y < 0.45f && pos.Z > -0.05f && pos.Z < 0.05f)
            {
                return (TissueRegistry.Heart, OrganType.Heart);
            }
            // Lungs: either side of the heart
            else if (MathF.Abs(pos.X) < 0.15f && pos.Y > 0.2f && pos.Y < 0.5f && pos.Z > -0.08f && pos.Z < 0.08f)
            {
                return (TissueRegistry.Lung, OrganType.Lung);
            }
            // Liver: lower right
            else if (pos.X < -0.05f && pos.X > -0.15f && pos.Y > 0.1f && pos.Y < 0.25f)
            {
                return (TissueRegistry.Liver, OrganType.Liver);
            }
            // Stomach: lower left
            else if (pos.X > 0.05f && pos.X < 0.15f && pos.Y > 0.1f && pos.Y < 0.2f)
            {
                return (TissueRegistry.Stomach, OrganType.Stomach);
            }
            
            // Default to Muscle/Tissue
            return (TissueRegistry.Muscle, OrganType.Muscle);
        }
    }
    
    // Scaffolding implementation of IActorPhysiology
    public class DummyPhysiology : IActorPhysiology
    {
        public BodyPart RootBodyPart { get; private set; } = null!;
        public float TotalBloodVolume { get; private set; } = 5000f; // 5L
        public float ConsciousnessLevel { get; private set; } = 1.0f;
        
        public void SetRoot(BodyPart root) => RootBodyPart = root;

        public void TickPhysiology(float dt)
        {
            float totalBleedRate = CalculateBleedRate(RootBodyPart);
            if (totalBleedRate > 0)
            {
                TotalBloodVolume -= totalBleedRate * dt;
                
                // Simplified MARCH triage consciousness model
                if (TotalBloodVolume < 4000f)
                    ConsciousnessLevel = MathF.Max(0, (TotalBloodVolume - 2500f) / 1500f);
            }
        }
        
        private float CalculateBleedRate(BodyPart part)
        {
            float rate = part.CurrentBleedRate;
            foreach (var child in part.Children)
                rate += CalculateBleedRate(child);
            return rate;
        }

        public void ProcessImpact(Vector3 trajectory, float kineticEnergy, Vector3 hitPoint)
        {
            RootBodyPart.ApplyTrauma(hitPoint, kineticEnergy);
        }
    }
}
