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
            
            var neck = new BodyPart { Type = BodyPartType.Neck, Parent = root };
            var head = new BodyPart { Type = BodyPartType.Head, Parent = neck };
            
            var leftArm = new BodyPart { Type = BodyPartType.LeftArm, Parent = root };
            var rightArm = new BodyPart { Type = BodyPartType.RightArm, Parent = root };
            var leftLeg = new BodyPart { Type = BodyPartType.LeftLeg, Parent = root };
            var rightLeg = new BodyPart { Type = BodyPartType.RightLeg, Parent = root };
            
            root.Children.Add(neck);
            neck.Children.Add(head);
            
            root.Children.Add(leftArm);
            root.Children.Add(rightArm);
            root.Children.Add(leftLeg);
            root.Children.Add(rightLeg);
            
            PopulateTorsoVoxels(root);
            PopulateHeadNeckVoxels(neck, head);
            PopulateLimbVoxels(leftArm, new Vector3(-0.3f, 0.25f, 0f), new Vector3(0.1f, 0.25f, 0.1f)); // Left Arm
            PopulateLimbVoxels(rightArm, new Vector3(0.3f, 0.25f, 0f), new Vector3(0.1f, 0.25f, 0.1f)); // Right Arm
            PopulateLimbVoxels(leftLeg, new Vector3(-0.1f, -0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f)); // Left Leg
            PopulateLimbVoxels(rightLeg, new Vector3(0.1f, -0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f)); // Right Leg
            
            return physiology;
        }

        private static void PopulateLimbVoxels(BodyPart limb, Vector3 center, Vector3 extents)
        {
            float voxelSize = 0.01f;
            
            for (float x = center.X - extents.X; x <= center.X + extents.X; x += voxelSize)
            {
                for (float y = center.Y - extents.Y; y <= center.Y + extents.Y; y += voxelSize)
                {
                    for (float z = center.Z - extents.Z; z <= center.Z + extents.Z; z += voxelSize)
                    {
                        var pos = new Vector3(x, y, z);
                        
                        // Simple SDF cylinder/capsule for limbs
                        float radius = MathF.Min(extents.X, extents.Z);
                        float dLimb = SdfCapsule(pos, new Vector3(center.X, center.Y - extents.Y, center.Z), new Vector3(center.X, center.Y + extents.Y, center.Z), radius);
                        
                        if (dLimb <= 0)
                        {
                            // Inner core is bone, outer shell is muscle
                            float dBone = dLimb + (radius * 0.7f); // Bone is inner 30% of radius
                            
                            OrganType organ;
                            TissueProperties tissue;
                            
                            if (dBone <= 0)
                            {
                                organ = OrganType.Bone;
                                tissue = TissueRegistry.Bone;
                            }
                            else
                            {
                                organ = OrganType.Muscle;
                                tissue = TissueRegistry.Muscle;
                            }
                            
                            limb.Voxels.Add(new PhysiologicalVoxel(pos, voxelSize, tissue, organ));
                        }
                    }
                }
            }
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

        private static void PopulateHeadNeckVoxels(BodyPart neck, BodyPart head)
        {
            float voxelSize = 0.01f;
            
            // Bounding box for Neck and Head: Y = 0.5f to 0.9f
            for (float x = -0.15f; x <= 0.15f; x += voxelSize)
            {
                for (float y = 0.5f; y <= 0.9f; y += voxelSize)
                {
                    for (float z = -0.15f; z <= 0.15f; z += voxelSize)
                    {
                        var pos = new Vector3(x, y, z);
                        var (tissue, organ) = DetermineHeadNeckOrgan(pos);
                        
                        if (tissue != null)
                        {
                            var voxel = new PhysiologicalVoxel(pos, voxelSize, tissue.Value, organ);
                            
                            if (y < 0.65f)
                                neck.Voxels.Add(voxel);
                            else
                                head.Voxels.Add(voxel);
                        }
                    }
                }
            }
        }
        
        private static (TissueProperties?, OrganType) DetermineHeadNeckOrgan(Vector3 pos)
        {
            // --- Head Neck Outer Skin/Muscle ---
            float dNeck = SdfCapsule(pos, new Vector3(0, 0.5f, -0.05f), new Vector3(0, 0.65f, -0.05f), 0.06f);
            float dHead = SdfEllipsoid(pos - new Vector3(0, 0.76f, -0.02f), new Vector3(0.08f, 0.11f, 0.10f));
            
            float dSkin = MathF.Min(dNeck, dHead);
            if (dSkin > 0) return (null, OrganType.None); // Empty space

            // --- Bones ---
            float dCervicalSpine = SdfCapsule(pos, new Vector3(0, 0.5f, -0.08f), new Vector3(0, 0.65f, -0.08f), 0.025f);
            float dSkullInner = SdfEllipsoid(pos - new Vector3(0, 0.77f, -0.03f), new Vector3(0.065f, 0.09f, 0.08f));
            float dSkullOuter = SdfEllipsoid(pos - new Vector3(0, 0.77f, -0.03f), new Vector3(0.075f, 0.10f, 0.09f));
            float dSkullShell = MathF.Max(dSkullOuter, -dSkullInner);
            
            float dJaw = SdfCapsule(pos, new Vector3(-0.05f, 0.68f, 0.03f), new Vector3(0.05f, 0.68f, 0.03f), 0.02f);
            float dBones = MathF.Min(MathF.Min(dCervicalSpine, dSkullShell), dJaw);

            // --- Brain ---
            float dBrain = dSkullInner; // Brain fills inside of the skull

            // --- Eyes ---
            float dEyeL = SdfEllipsoid(pos - new Vector3(0.03f, 0.76f, 0.06f), new Vector3(0.015f, 0.015f, 0.015f));
            float dEyeR = SdfEllipsoid(pos - new Vector3(-0.03f, 0.76f, 0.06f), new Vector3(0.015f, 0.015f, 0.015f));
            float dEyes = MathF.Min(dEyeL, dEyeR);

            // --- Mouth / Oral Cavity ---
            float dMouth = SdfEllipsoid(pos - new Vector3(0, 0.68f, 0.06f), new Vector3(0.03f, 0.02f, 0.04f));

            // --- Airway (Trachea) ---
            float dAirway = SdfCapsule(pos, new Vector3(0, 0.5f, -0.03f), new Vector3(0, 0.66f, -0.02f), 0.015f);

            // --- Evaluation Hierarchy (Inner to outer) ---
            if (dBones <= 0) return (TissueRegistry.Bone, OrganType.Bone);
            if (dBrain <= 0) return (TissueRegistry.Brain, OrganType.Brain);
            if (dEyes <= 0) return (TissueRegistry.Eye, OrganType.Eye);
            if (dAirway <= 0) return (TissueRegistry.Airway, OrganType.Airway);
            if (dMouth <= 0) return (TissueRegistry.Mouth, OrganType.Mouth);

            return (TissueRegistry.Muscle, OrganType.Muscle); // Soft tissue padding for face/neck
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
