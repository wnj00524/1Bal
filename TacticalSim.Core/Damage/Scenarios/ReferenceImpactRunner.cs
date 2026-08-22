using System;
using System.Collections.Generic;
using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Core.Damage.Scenarios;

public interface IReferenceImpactRunner
{
    ReferenceImpactResult Run(ReferenceImpactRunRequest request);
    ReferenceImpactComparisonResult Compare(ReferenceImpactComparisonRequest request);
}

/// <summary>
/// Runs reference inputs only through <see cref="IProjectileInteractionService"/> and
/// samples the resulting physiology without duplicating projectile/body calculations.
/// </summary>
public sealed class ReferenceImpactRunner : IReferenceImpactRunner
{
    private readonly IProjectileInteractionService _interactionService;
    private readonly IReferenceImpactScenarioCatalog _catalog;

    public ReferenceImpactRunner(
        IProjectileInteractionService interactionService,
        IReferenceImpactScenarioCatalog catalog)
    {
        _interactionService = interactionService ?? throw new ArgumentNullException(nameof(interactionService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public ReferenceImpactResult Run(ReferenceImpactRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScenarioId);
        if (!Enum.IsDefined(request.ModelVersion))
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported damage-model version.");

        ReferenceImpactScenario scenario = _catalog.GetRequired(request.ScenarioId);
        ReferenceImpactScenarioInput input = scenario.Input;
        IActorPhysiology target = scenario.CreateFreshTarget();
        IDeterministicRandomStreamProvider randomStreams = BuildRandomStreams(request.Seed);
        BallisticProfile projectileProfile = CreateProjectileProfile(input.Projectile);
        string comparisonKey = CreateComparisonKey(input.ScenarioId, request.Seed);
        var interactionRequest = new ProjectileInteractionRequest(
            impactId: comparisonKey,
            projectileProfileId: input.Projectile.ProfileId,
            targetPhysiology: target,
            projectileState: new ProjectileState
            {
                Position = input.EntryPointBodyLocalMeters,
                Velocity = input.Direction * input.Projectile.MuzzleVelocityMetersPerSecond,
                Time = 0f
            },
            projectileProfile: projectileProfile,
            maximumTraversalDistance: input.MaximumTraversalDistance,
            modelVersion: request.ModelVersion,
            randomStreams: randomStreams);

        ProjectileInteractionResult interaction = _interactionService.Resolve(interactionRequest)
            ?? throw new InvalidOperationException(
                $"Reference scenario '{input.ScenarioId}' did not intersect the fresh target '{input.TargetProfileId}'.");

        BuildTimelines(
            target,
            interaction.DebugTrace,
            input.ObservationDuration,
            input.PhysiologyStep,
            out IReadOnlyList<PhysiologyTimelinePoint> physiologyTimeline,
            out IReadOnlyList<CapabilityTimelinePoint> capabilityTimeline);

        var lesions = new ReferenceLesionOutput(
            isDeferred: true,
            deferredTo: "M6: Anatomical structures and persistent lesions",
            items: interaction.DebugTrace.GeneratedLesions);
        return new ReferenceImpactResult(
            input,
            interaction.WoundTrack.ModelVersion,
            comparisonKey,
            request.Seed,
            interaction.WoundTrack,
            interaction.EnergyLedger,
            ReferenceProjectileStateSnapshot.From(interaction.FinalProjectileState),
            lesions,
            physiologyTimeline,
            capabilityTimeline,
            interaction.DebugTrace.RandomMetadata,
            interaction.DebugTrace.NumericalWarnings);
    }

    public ReferenceImpactComparisonResult Compare(ReferenceImpactComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BaselineModelVersion == request.CandidateModelVersion)
            throw new ArgumentException("Cross-model comparison requires two different model versions.", nameof(request));

        ReferenceImpactResult baseline = Run(new ReferenceImpactRunRequest(
            request.ScenarioId,
            request.BaselineModelVersion,
            request.Seed));
        ReferenceImpactResult candidate = Run(new ReferenceImpactRunRequest(
            request.ScenarioId,
            request.CandidateModelVersion,
            request.Seed));
        return new ReferenceImpactComparisonResult(baseline.ComparisonKey, baseline, candidate);
    }

    private static IDeterministicRandomStreamProvider BuildRandomStreams(ulong seed) =>
        new DeterministicRandomStreamProvider(new FixedRootSeedProvider(seed));

    private static BallisticProfile CreateProjectileProfile(ReferenceProjectileInput input)
    {
        if (!string.Equals(input.DragModelId, "standard-drag-curve-v1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported reference drag model '{input.DragModelId}'.");

        return new BallisticProfile
        {
            Mass = input.Mass.Kilograms,
            CrossSectionalArea = input.CrossSectionalArea.SquareMeters,
            DragModel = new StandardDragCurve(input.DragCoefficient)
        };
    }

    private static string CreateComparisonKey(string scenarioId, ulong seed) =>
        $"reference-impact-v1/{scenarioId}/seed-{seed}";

    private static void BuildTimelines(
        IActorPhysiology target,
        ImpactDebugTrace trace,
        SimulationTime observationDuration,
        SimulationTime physiologyStep,
        out IReadOnlyList<PhysiologyTimelinePoint> physiologyTimeline,
        out IReadOnlyList<CapabilityTimelinePoint> capabilityTimeline)
    {
        var physiology = new List<PhysiologyTimelinePoint>
        {
            new(SimulationTime.FromSeconds(0f), "before-impact", trace.PhysiologyBefore),
            new(SimulationTime.FromSeconds(0f), "after-impact", trace.PhysiologyAfter)
        };
        var capability = new List<CapabilityTimelinePoint>
        {
            new(SimulationTime.FromSeconds(0f), "before-impact", trace.CapabilityBefore),
            new(SimulationTime.FromSeconds(0f), "after-impact", trace.CapabilityAfter)
        };

        float elapsedSeconds = 0f;
        int sample = 0;
        while (elapsedSeconds < observationDuration.Seconds)
        {
            float deltaSeconds = MathF.Min(
                physiologyStep.Seconds,
                observationDuration.Seconds - elapsedSeconds);
            target.TickPhysiology(deltaSeconds);
            elapsedSeconds += deltaSeconds;
            sample++;
            string phase = $"post-impact-{sample:D2}";
            SimulationTime elapsed = SimulationTime.FromSeconds(elapsedSeconds);
            physiology.Add(new PhysiologyTimelinePoint(
                elapsed,
                phase,
                PhysiologyDebugSnapshot.Capture(target)));
            capability.Add(new CapabilityTimelinePoint(
                elapsed,
                phase,
                CapabilityDebugSnapshot.Capture(target)));
        }

        physiologyTimeline = physiology.AsReadOnly();
        capabilityTimeline = capability.AsReadOnly();
    }
}
