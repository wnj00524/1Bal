using System.Numerics;
using TacticalSim.Core;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class MedicalAssessorTests
{
    [Fact]
    public void PersistentLesion_IsReportedEvenWhenNoVoxelIsDestroyed()
    {
        var physiology = (TacticalActorPhysiology)AnatomicalDummyBuilder.BuildDummy();
        physiology.LesionRepository.AddRange([
            new TissueLesion(
                "lesion/impact-1/0000/lung",
                "lung.right",
                "impact-1",
                LesionKind.ParenchymalInjury,
                0.4f,
                new(Vector3.Zero, Vector3.UnitZ, Distance.FromMeters(0.02f), Distance.FromMeters(0.003f)),
                LesionTreatmentState.Untreated,
                DateTimeOffset.UnixEpoch)
        ]);

        MedicalReport report = MedicalAssessor.AssessTrauma(physiology);

        Assert.Equal(1, report.PersistentLesionCount);
        Assert.Contains("PERSISTENT STRUCTURAL INJURY", report.AssessmentText);
        Assert.Contains("ParenchymalInjury at lung.right", report.AssessmentText);
        Assert.DoesNotContain("No significant tissue destruction detected.", report.AssessmentText);
        Assert.Empty(report.DestroyedVolumeCc);
    }
}
