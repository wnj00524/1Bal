namespace ProxyState.Simulation;

/// <summary>
/// Deterministic operation counts for comparing simulation implementations.
/// These counts deliberately describe visited/evaluated work rather than time.
/// </summary>
public sealed class SimulationWorkDiagnostics
{
    public long DecisionPasses { get; private set; }
    public long CandidateEvaluations { get; private set; }
    public long TargetPopulationVisits { get; private set; }
    public long EdgeVisits { get; private set; }
    public long TransientOperations { get; private set; }

    public SimulationWorkSnapshot Snapshot() => new(DecisionPasses, CandidateEvaluations,
        TargetPopulationVisits, EdgeVisits, TransientOperations);

    public void Reset()
    {
        DecisionPasses = 0;
        CandidateEvaluations = 0;
        TargetPopulationVisits = 0;
        EdgeVisits = 0;
        TransientOperations = 0;
    }

    internal void RecordDecisionPass() => DecisionPasses++;
    internal void RecordCandidateEvaluation() => CandidateEvaluations++;
    internal void RecordTargetPopulationVisit() => TargetPopulationVisits++;
    internal void RecordEdgeVisit() => EdgeVisits++;
    internal void RecordTransientOperation(long count = 1) => TransientOperations += count;
}

public readonly record struct SimulationWorkSnapshot(
    long DecisionPasses,
    long CandidateEvaluations,
    long TargetPopulationVisits,
    long EdgeVisits,
    long TransientOperations);
