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
            
            var allVoxels = new List<PhysiologicalVoxel>();
            void Collect(BodyPart p) {
                allVoxels.AddRange(p.Voxels);
                foreach (var c in p.Children) Collect(c);
            }
            Collect(dummy.RootBodyPart);

            var voxels = allVoxels;

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
                sb.AppendLine();
                sb.AppendLine("--- VITALS ---");
                sb.AppendLine($"Blood Volume: {(dummy.TotalBloodVolume/1000f):F2} L");
                sb.AppendLine($"Blood Pressure (MAP): {dummy.MeanArterialPressureMmhg:F0} mmHg");
                sb.AppendLine($"Heart Rate: {dummy.HeartRateBpm:F0} BPM");
                sb.AppendLine($"Consciousness: {(dummy.ConsciousnessLevel*100f):F0}%");
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

            if (dummy.AirwayObstruction > 0.1f)
            {
                if (dummy.AirwayObstruction >= 1.0f)
                    sb.AppendLine(">>> ASPHYXIATION ALARM: COMPLETE AIRWAY OBSTRUCTION <<<");
                else
                    sb.AppendLine($">>> AIRWAY COMPROMISED: {(dummy.AirwayObstruction*100f):F0}% OBSTRUCTED <<<");
            }
            
            if (dummy.BloodOxygenation < 0.85f)
                sb.AppendLine($">>> HYPOXIA ALARM: SpO2 DANGEROUSLY LOW ({(dummy.BloodOxygenation*100f):F0}%) <<<");

            if (report.LungCapacityLostPercentage >= 15f)
            {
                if (!dummy.HasChestSeal)
                    sb.AppendLine(">>> PROGNOSIS: FATAL WITHIN 5 MINUTES WITHOUT FIRST AID <<<");
                else if (dummy.TensionPneumothoraxLevel > 0f)
                    sb.AppendLine(">>> PROGNOSIS: DETERIORATION HALTED; NEEDLE DECOMPRESSION REQUIRED <<<");
                else
                    sb.AppendLine(">>> PROGNOSIS: STABILIZED BY FIRST AID <<<");
            }

            if (report.DestroyedVolumeCc.ContainsKey(OrganType.Heart) || report.LungCapacityLostPercentage > 5f || dummy.AirwayObstruction > 0.1f || dummy.BloodOxygenation < 0.85f)
                sb.AppendLine();

            sb.AppendLine($"SYSTEMIC BLEED RATE: {report.TotalBleedRateMlPerMin:F0} ml/min");
            
            if (dummy.AlveolarBloodAccumulation > 0f)
                sb.AppendLine($"ALVEOLAR BLOOD ACCUMULATION: {dummy.AlveolarBloodAccumulation:F1} ml");

            if (etuMin < 1.0f)
                sb.AppendLine($"ESTIMATED TIME TO UNCONSCIOUSNESS: < 1 minute ({(etuMin*60f):F0} seconds)");
            else if (etuMin == float.PositiveInfinity && dummy.BloodOxygenation >= 0.85f)
                sb.AppendLine("ESTIMATED TIME TO UNCONSCIOUSNESS: Stable");
            else if (etuMin != float.PositiveInfinity)
                sb.AppendLine($"ESTIMATED TIME TO UNCONSCIOUSNESS: {etuMin:F1} minutes");

            sb.AppendLine();
            sb.AppendLine("--- LIVE VITALS ---");
            sb.AppendLine($"Blood Volume: {(dummy.TotalBloodVolume/1000f):F2} L / 5.00 L");
            sb.AppendLine($"Blood Pressure (MAP): {dummy.MeanArterialPressureMmhg:F0} mmHg");
            sb.AppendLine($"Heart Rate: {dummy.HeartRateBpm:F0} BPM");
            sb.AppendLine($"Hemorrhage Class: {dummy.CurrentHemorrhageClass}");
            sb.AppendLine($"SpO2 (Oxygenation): {(dummy.BloodOxygenation*100f):F0}%");
            
            sb.AppendLine($"Pain Level: {(dummy.PainLevel*100f):F0}%");
            if (dummy.ShockLevel > 0f)
                sb.AppendLine($"Shock Level: {(dummy.ShockLevel*100f):F0}%");
                
            if (dummy.MobilityLevel < 1.0f)
                sb.AppendLine($"Mobility Level: {(dummy.MobilityLevel*100f):F0}%");
            if (dummy.WeaponHandlingLevel < 1.0f)
                sb.AppendLine($"Weapon Handling Level: {(dummy.WeaponHandlingLevel*100f):F0}%");
                
            if (dummy.PainLevel > 0.8f)
                sb.AppendLine(">>> NEUROLOGICAL ALARM: SEVERE AGONY (Accuracy Degraded) <<<");
            if (dummy.ShockLevel > 0.5f)
                sb.AppendLine(">>> NEUROLOGICAL ALARM: SEVERE SHOCK DETECTED <<<");
                
            if (dummy.MobilityLevel < 0.5f)
                sb.AppendLine(">>> MOTOR ALARM: SEVERE LEG TRAUMA (Mobility Impaired) <<<");
            if (dummy.WeaponHandlingLevel < 0.5f)
                sb.AppendLine(">>> MOTOR ALARM: SEVERE ARM TRAUMA (Weapon Handling Impaired) <<<");
            
            if (dummy.ConsciousnessLevel <= 0f)
            {
                sb.AppendLine("Consciousness: [UNCONSCIOUS]");
            }
            else
            {
                sb.AppendLine($"Consciousness: {(dummy.ConsciousnessLevel*100f):F0}%");
            }

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
