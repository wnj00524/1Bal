using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using TacticalSim.Core.Damage.Ballistics;

namespace TacticalSim.Core.Damage.Scenarios;

/// <summary>Formats reference results as machine-readable JSON or concise invariant text.</summary>
public static class ReferenceImpactFormatter
{
    public static string ToJson(ReferenceImpactResult result, bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, DamageModelJson.CreateOptions(writeIndented));
    }

    public static string ToJson(ReferenceImpactComparisonResult result, bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, DamageModelJson.CreateOptions(writeIndented));
    }

    public static string ScenarioListToJson(
        IReadOnlyList<ReferenceImpactScenarioInput> scenarios,
        bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        return JsonSerializer.Serialize(scenarios, DamageModelJson.CreateOptions(writeIndented));
    }

    public static string ToText(ReferenceImpactResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PhysiologyTimelinePoint firstPhysiology = result.PhysiologyTimeline[0];
        PhysiologyTimelinePoint lastPhysiology = result.PhysiologyTimeline[^1];
        CapabilityTimelinePoint firstCapability = result.CapabilityTimeline[0];
        CapabilityTimelinePoint lastCapability = result.CapabilityTimeline[^1];
        var text = new StringBuilder();
        text.AppendLine($"Scenario: {result.ScenarioInput.ScenarioId} - {result.ScenarioInput.DisplayName}");
        text.AppendLine($"Model: {result.ModelIdentifier}");
        text.AppendLine($"Comparison key: {result.ComparisonKey}");
        text.AppendLine($"Seed: {result.Seed}");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Wound track: {result.WoundTrack.Disposition}, {result.WoundTrack.Segments.Count} segment(s)"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Final projectile: position {result.FinalProjectileState.PositionBodyLocalMeters}; "
            + $"speed {result.FinalProjectileState.VelocityMetersPerSecond.Length():R} m/s; "
            + $"time {result.FinalProjectileState.Elapsed.Seconds:R} s"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Energy: {result.EnergyLedger.IncomingEnergy.Joules:R} J in, "
            + $"{result.EnergyLedger.TotalStructureDepositedEnergy.Joules:R} J deposited, "
            + $"{result.EnergyLedger.OutgoingEnergy.Joules:R} J out, "
            + $"residual {result.EnergyLedger.NumericalResidual.Joules:R} J, "
            + $"conserved={result.EnergyLedger.IsConserved}"));
        text.AppendLine(
            $"Lesions: {result.Lesions.Items.Count} ({(result.Lesions.IsDeferred ? $"deferred to {result.Lesions.DeferredTo}" : "available")})");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Physiology: {result.PhysiologyTimeline.Count} samples; "
            + $"blood {firstPhysiology.State.BloodVolume.CubicCentimeters:R} -> "
            + $"{lastPhysiology.State.BloodVolume.CubicCentimeters:R} cc; "
            + $"consciousness {firstPhysiology.State.Consciousness:R} -> "
            + $"{lastPhysiology.State.Consciousness:R}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Capability: {result.CapabilityTimeline.Count} samples; "
            + $"mobility {firstCapability.State.Mobility:R} -> {lastCapability.State.Mobility:R}; "
            + $"weapon handling {firstCapability.State.WeaponHandling:R} -> {lastCapability.State.WeaponHandling:R}"));
        text.AppendLine(
            $"Random: root seed {result.RandomMetadata.RootSeed}; "
            + string.Join(", ", result.RandomMetadata.Streams.Select(
                stream => $"{stream.StreamName} ({stream.DrawCount} draws)")));
        if (result.NumericalWarnings.Count > 0)
            text.AppendLine($"Warnings: {string.Join(" | ", result.NumericalWarnings)}");
        text.Append($"SHA-256: {result.DeterministicHash}");
        return text.ToString();
    }

    public static string ToText(ReferenceImpactComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new StringBuilder();
        text.AppendLine($"Comparison: {result.ComparisonKey}");
        AppendModelLine(text, "Baseline", result.Baseline);
        AppendModelLine(text, "Candidate", result.Candidate);
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Delta (candidate - baseline): "
            + $"deposited {result.Delta.DepositedEnergy.Joules:R} J, "
            + $"outgoing {result.Delta.OutgoingEnergy.Joules:R} J, "
            + $"segments {result.Delta.WoundSegmentCount}; "
            + $"same disposition={result.Delta.SameDisposition}; "
            + $"conservation changed={result.Delta.ConservationStatusChanged}"));
        text.Append($"SHA-256: {result.DeterministicHash}");
        return text.ToString();
    }

    public static string ScenarioListToText(IReadOnlyList<ReferenceImpactScenarioInput> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        return string.Join(
            Environment.NewLine,
            scenarios.Select(scenario => $"{scenario.ScenarioId}\t{scenario.DisplayName}"));
    }

    private static void AppendModelLine(StringBuilder text, string label, ReferenceImpactResult result)
    {
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {result.ModelIdentifier}; "
            + $"{result.WoundTrack.Segments.Count} segment(s); "
            + $"{result.EnergyLedger.TotalStructureDepositedEnergy.Joules:R} J deposited; "
            + $"hash {result.DeterministicHash}"));
    }
}
