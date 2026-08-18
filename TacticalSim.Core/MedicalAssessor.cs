using System;
using System.Collections.Generic;
using System.Text;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core
{
    public class MedicalReport
    {
        public float TotalBleedRateMlPerMin { get; set; }
        public float LungCapacityLostPercentage { get; set; }
        public Dictionary<OrganType, float> DestroyedVolumeCc { get; set; } = new();
        public string AssessmentText { get; set; } = string.Empty;
    }

    public static class MedicalAssessor
    {
        public static MedicalReport AssessTrauma(IActorPhysiology dummy)
        {
            var report = new MedicalReport();
            var voxels = dummy.RootBodyPart.Voxels;

            float totalLungVolume = 0;
            float destroyedLungVolume = 0;
            float totalBleedRateSec = 0;

            foreach (var voxel in voxels)
            {
                float volCc = voxel.Size * voxel.Size * voxel.Size * 1_000_000f; // m^3 to cm^3
                
                if (voxel.Organ == OrganType.Lung)
                {
                    totalLungVolume += volCc;
                }

                if (voxel.IsDestroyed)
                {
                    if (!report.DestroyedVolumeCc.ContainsKey(voxel.Organ))
                        report.DestroyedVolumeCc[voxel.Organ] = 0;
                    
                    report.DestroyedVolumeCc[voxel.Organ] += volCc;

                    if (voxel.Organ == OrganType.Lung)
                    {
                        destroyedLungVolume += volCc;
                    }
                }
            }
            
            totalBleedRateSec = GetTotalActiveBleedRate(dummy.RootBodyPart);

            report.TotalBleedRateMlPerMin = totalBleedRateSec * 60f;
            report.LungCapacityLostPercentage = totalLungVolume > 0 ? (destroyedLungVolume / totalLungVolume) * 100f : 0;

            // Estimate Time to Unconsciousness
            // Hypovolemic Shock (Class III hemorrhage) starts around 1500ml blood loss
            float timeToShockMin = report.TotalBleedRateMlPerMin > 0 ? 1500f / report.TotalBleedRateMlPerMin : float.PositiveInfinity;
            
            // Hypoxia from Tension Pneumothorax
            float timeToHypoxiaMin = report.LungCapacityLostPercentage > 10f ? 
                (30f / report.LungCapacityLostPercentage) : float.PositiveInfinity;

            float etuMin = MathF.Min(timeToShockMin, timeToHypoxiaMin);

            // Generate Text Report
            var sb = new StringBuilder();
            sb.AppendLine("--- IMMEDIATE POST-IMPACT MEDICAL ASSESSMENT ---");
            sb.AppendLine();
            
            if (report.DestroyedVolumeCc.Count == 0)
            {
                sb.AppendLine("No significant tissue destruction detected.");
                report.AssessmentText = sb.ToString();
                return report;
            }

            sb.AppendLine("ORGAN DAMAGE (Volume Destroyed):");
            foreach (var kvp in report.DestroyedVolumeCc)
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value:F1} cc");
            }
            sb.AppendLine();
            
            if (report.DestroyedVolumeCc.ContainsKey(OrganType.Heart))
                sb.AppendLine(">>> CRITICAL ALARM: CATASTROPHIC CARDIAC HEMORRHAGE DETECTED <<<");
                
            if (report.LungCapacityLostPercentage > 5f)
                sb.AppendLine($">>> RESPIRATORY ALARM: {report.LungCapacityLostPercentage:F1}% LUNG CAPACITY LOST (TENSION PNEUMOTHORAX RISK) <<<");

            if (report.DestroyedVolumeCc.ContainsKey(OrganType.Heart) || report.LungCapacityLostPercentage > 5f)
                sb.AppendLine();

            sb.AppendLine($"SYSTEMIC BLEED RATE: {report.TotalBleedRateMlPerMin:F0} ml/min");
            
            if (etuMin < 1.0f)
                sb.AppendLine($"ESTIMATED TIME TO UNCONSCIOUSNESS: < 1 minute ({(etuMin*60f):F0} seconds)");
            else if (etuMin == float.PositiveInfinity)
                sb.AppendLine("ESTIMATED TIME TO UNCONSCIOUSNESS: Stable");
            else
                sb.AppendLine($"ESTIMATED TIME TO UNCONSCIOUSNESS: {etuMin:F1} minutes");

            report.AssessmentText = sb.ToString();
            return report;
        }

        private static float GetTotalActiveBleedRate(BodyPart part)
        {
            float rate = part.GetActiveBleedRate();
            foreach (var child in part.Children)
                rate += GetTotalActiveBleedRate(child);
            return rate;
        }
    }
}
