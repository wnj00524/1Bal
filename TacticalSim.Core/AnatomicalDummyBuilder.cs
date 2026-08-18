using System.Numerics;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core
{
    public static class AnatomicalDummyBuilder
    {
        public static IActorPhysiology BuildDummy()
        {
            var physiology = new TacticalActorPhysiology();
            
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
            float voxelSize = 0.01f; // 1cm voxels for high-res physiological modeling
            
            // Bounding box for Torso
            for (float x = -0.22f; x <= 0.22f; x += voxelSize)
            {
                for (float y = 0.0f; y <= 0.5f; y += voxelSize)
                {
                    for (float z = -0.15f; z <= 0.15f; z += voxelSize)
                    {
                        var pos = new Vector3(x, y, z);
                        var (tissue, organ) = DetermineOrganAtPosition(pos);
                        if (tissue != null)
                        {
                            var voxel = new PhysiologicalVoxel(pos, voxelSize, tissue.Value, organ);
                            torso.Voxels.Add(voxel);
                        }
                    }
                }
            }
        }
        
        // --- Signed Distance Field (SDF) Math Functions ---
        private static float SdfEllipsoid(Vector3 p, Vector3 r)
        {
            float k0 = (p / r).Length();
            float k1 = (p / (r * r)).Length();
            return k0 * (k0 - 1.0f) / k1;
        }

        private static float SdfCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            Vector3 pa = p - a, ba = b - a;
            float h = MathF.Max(0f, MathF.Min(1f, Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba)));
            return (pa - ba * h).Length() - r;
        }

        private static (TissueProperties?, OrganType) DetermineOrganAtPosition(Vector3 pos)
        {
            // --- Torso Skin/Muscle Outline ---
            float dTorsoOuter = SdfEllipsoid(pos - new Vector3(0, 0.25f, 0), new Vector3(0.2f, 0.24f, 0.13f));
            if (dTorsoOuter > 0) return (null, OrganType.None); // Empty space outside dummy

            // --- Bones ---
            float dSpine = SdfCapsule(pos, new Vector3(0, 0.0f, -0.08f), new Vector3(0, 0.5f, -0.08f), 0.025f);
            
            // Ribcage (Hollow ellipsoid with horizontal periodic cuts)
            Vector3 chestCenter = new Vector3(0, 0.28f, 0.0f);
            float dOuterRibs = SdfEllipsoid(pos - chestCenter, new Vector3(0.16f, 0.18f, 0.11f));
            float dInnerRibs = SdfEllipsoid(pos - chestCenter, new Vector3(0.14f, 0.16f, 0.09f));
            float dRibShell = MathF.Max(dOuterRibs, -dInnerRibs); // Hollow shell
            float dRibCuts = MathF.Sin(pos.Y * 200f) - 0.2f; // Periodic cuts to make horizontal ribs
            float dRibs = MathF.Max(dRibShell, dRibCuts);
            
            // Sternum (solid front piece)
            float dSternum = SdfCapsule(pos, new Vector3(0, 0.15f, 0.11f), new Vector3(0, 0.40f, 0.11f), 0.02f);
            
            // Pelvis (Hollow bowl structure at the base)
            float dPelvisOuter = SdfEllipsoid(pos - new Vector3(0, 0.04f, -0.02f), new Vector3(0.14f, 0.06f, 0.10f));
            float dPelvisInner = SdfEllipsoid(pos - new Vector3(0, 0.04f, -0.02f), new Vector3(0.10f, 0.08f, 0.07f));
            float dPelvis = MathF.Max(dPelvisOuter, -dPelvisInner);

            float dBone = MathF.Min(MathF.Min(MathF.Min(dSpine, dRibs), dSternum), dPelvis);

            // --- Internal Organs ---
            // Heart (Left side of chest)
            float dHeart = SdfEllipsoid(pos - new Vector3(0.04f, 0.28f, 0.04f), new Vector3(0.05f, 0.06f, 0.05f));

            // Lungs (Wrapping around heart, inside ribcage)
            float dLungL = SdfEllipsoid(pos - new Vector3(0.08f, 0.28f, -0.02f), new Vector3(0.06f, 0.12f, 0.07f));
            float dLungR = SdfEllipsoid(pos - new Vector3(-0.08f, 0.28f, -0.02f), new Vector3(0.06f, 0.12f, 0.07f));
            float dLungs = MathF.Min(dLungL, dLungR);
            dLungs = MathF.Max(dLungs, -dHeart); // Lungs yield to heart

            // Liver (Lower right side, massive)
            float dLiver = SdfEllipsoid(pos - new Vector3(-0.06f, 0.16f, 0.02f), new Vector3(0.08f, 0.07f, 0.07f));

            // Stomach (Lower left side)
            float dStomach = SdfEllipsoid(pos - new Vector3(0.05f, 0.14f, 0.03f), new Vector3(0.05f, 0.05f, 0.05f));

            // Spleen (Far left, behind stomach)
            float dSpleen = SdfEllipsoid(pos - new Vector3(0.10f, 0.14f, -0.03f), new Vector3(0.03f, 0.04f, 0.03f));

            // Kidneys (Left and Right, posterior)
            float dKidneyL = SdfEllipsoid(pos - new Vector3(0.05f, 0.12f, -0.05f), new Vector3(0.03f, 0.05f, 0.02f));
            float dKidneyR = SdfEllipsoid(pos - new Vector3(-0.05f, 0.12f, -0.05f), new Vector3(0.03f, 0.05f, 0.02f));
            float dKidneys = MathF.Min(dKidneyL, dKidneyR);

            // Intestines (Lower abdomen, filling space)
            float dIntestines = SdfEllipsoid(pos - new Vector3(0.0f, 0.07f, 0.02f), new Vector3(0.11f, 0.06f, 0.08f));
            // Ensure intestines don't overlap with liver/stomach or pelvis
            dIntestines = MathF.Max(dIntestines, -dLiver);
            dIntestines = MathF.Max(dIntestines, -dStomach);
            dIntestines = MathF.Max(dIntestines, -dPelvis);

            // --- Evaluation Hierarchy (Inner to outer) ---
            if (dBone <= 0) return (TissueRegistry.Bone, OrganType.Bone);
            if (dHeart <= 0) return (TissueRegistry.Heart, OrganType.Heart);
            if (dLungs <= 0) return (TissueRegistry.Lung, OrganType.Lung);
            if (dLiver <= 0) return (TissueRegistry.Liver, OrganType.Liver);
            if (dSpleen <= 0) return (TissueRegistry.Spleen, OrganType.Spleen);
            if (dKidneys <= 0) return (TissueRegistry.Kidney, OrganType.Kidney);
            if (dStomach <= 0) return (TissueRegistry.Stomach, OrganType.Stomach);
            if (dIntestines <= 0) return (TissueRegistry.Intestines, OrganType.Intestines);

            // Default to bulk Muscle/Fat padding
            return (TissueRegistry.Muscle, OrganType.Muscle);
        }
    }
}
