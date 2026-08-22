using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Core.Damage.Scenarios;

public sealed record ReferenceImpactRunRequest(
    string ScenarioId,
    DamageModelVersion ModelVersion,
    ulong Seed);

public sealed record ReferenceImpactComparisonRequest(
    string ScenarioId,
    DamageModelVersion BaselineModelVersion,
    DamageModelVersion CandidateModelVersion,
    ulong Seed);

public sealed record PhysiologyTimelinePoint(
    SimulationTime Elapsed,
    string Phase,
    PhysiologyDebugSnapshot State);

public sealed record CapabilityTimelinePoint(
    SimulationTime Elapsed,
    string Phase,
    CapabilityDebugSnapshot State);

/// <summary>Serializable terminal projectile state; unlike ProjectileState, this uses properties and typed time.</summary>
public sealed record ReferenceProjectileStateSnapshot(
    Vector3 PositionBodyLocalMeters,
    Vector3 VelocityMetersPerSecond,
    SimulationTime Elapsed)
{
    internal static ReferenceProjectileStateSnapshot From(ProjectileState state) => new(
        state.Position,
        state.Velocity,
        SimulationTime.FromSeconds(state.Time));
}

/// <summary>Serializable persistent-lesion output emitted by the M6 injury layer.</summary>
public sealed class ReferenceLesionOutput
{
    private readonly ReadOnlyCollection<string> _items;

    public ReferenceLesionOutput(bool isDeferred, string deferredTo, IEnumerable<string>? items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deferredTo);
        IsDeferred = isDeferred;
        DeferredTo = deferredTo;
        _items = Array.AsReadOnly(items?.ToArray() ?? []);
    }

    public bool IsDeferred { get; }
    public string DeferredTo { get; }
    public IReadOnlyList<string> Items => _items;
}

/// <summary>Machine-readable output from one authoritative reference impact.</summary>
public sealed class ReferenceImpactResult
{
    public const string CurrentOutputSchemaVersion = "reference-impact-result-v2";

    private readonly ReadOnlyCollection<PhysiologyTimelinePoint> _physiologyTimeline;
    private readonly ReadOnlyCollection<CapabilityTimelinePoint> _capabilityTimeline;
    private readonly ReadOnlyCollection<string> _numericalWarnings;

    internal ReferenceImpactResult(
        ReferenceImpactScenarioInput scenarioInput,
        DamageModelVersion modelVersion,
        string comparisonKey,
        ulong seed,
        WoundTrack woundTrack,
        EnergyLedger energyLedger,
        ReferenceProjectileStateSnapshot finalProjectileState,
        ReferenceLesionOutput lesions,
        IEnumerable<PhysiologyTimelinePoint> physiologyTimeline,
        IEnumerable<CapabilityTimelinePoint> capabilityTimeline,
        DeterministicRandomMetadataSnapshot randomMetadata,
        IEnumerable<string>? numericalWarnings)
    {
        ArgumentNullException.ThrowIfNull(scenarioInput);
        if (!Enum.IsDefined(modelVersion))
            throw new ArgumentOutOfRangeException(nameof(modelVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonKey);
        ArgumentNullException.ThrowIfNull(woundTrack);
        ArgumentNullException.ThrowIfNull(energyLedger);
        ArgumentNullException.ThrowIfNull(finalProjectileState);
        ArgumentNullException.ThrowIfNull(lesions);
        ArgumentNullException.ThrowIfNull(physiologyTimeline);
        ArgumentNullException.ThrowIfNull(capabilityTimeline);
        ArgumentNullException.ThrowIfNull(randomMetadata);

        OutputSchemaVersion = CurrentOutputSchemaVersion;
        ScenarioInput = scenarioInput;
        ModelVersion = modelVersion;
        ModelIdentifier = modelVersion.ToIdentifier();
        ComparisonKey = comparisonKey;
        Seed = seed;
        WoundTrack = woundTrack;
        EnergyLedger = energyLedger;
        FinalProjectileState = finalProjectileState;
        Lesions = lesions;
        _physiologyTimeline = Array.AsReadOnly(physiologyTimeline.ToArray());
        _capabilityTimeline = Array.AsReadOnly(capabilityTimeline.ToArray());
        RandomMetadata = randomMetadata;
        _numericalWarnings = Array.AsReadOnly(numericalWarnings?.ToArray() ?? []);

        var hashPayload = new ReferenceImpactHashPayload(
            OutputSchemaVersion,
            ScenarioInput,
            ModelVersion,
            ModelIdentifier,
            ComparisonKey,
            Seed,
            WoundTrack,
            EnergyLedger,
            FinalProjectileState,
            Lesions,
            _physiologyTimeline,
            _capabilityTimeline,
            RandomMetadata,
            _numericalWarnings);
        DeterministicHash = ReferenceImpactCanonicalHash.ComputeSha256(hashPayload);
    }

    public string OutputSchemaVersion { get; }
    public ReferenceImpactScenarioInput ScenarioInput { get; }
    public DamageModelVersion ModelVersion { get; }
    public string ModelIdentifier { get; }
    public string ComparisonKey { get; }
    public ulong Seed { get; }
    public WoundTrack WoundTrack { get; }
    public EnergyLedger EnergyLedger { get; }
    public ReferenceProjectileStateSnapshot FinalProjectileState { get; }
    public ReferenceLesionOutput Lesions { get; }
    public IReadOnlyList<PhysiologyTimelinePoint> PhysiologyTimeline => _physiologyTimeline;
    public IReadOnlyList<CapabilityTimelinePoint> CapabilityTimeline => _capabilityTimeline;
    public DeterministicRandomMetadataSnapshot RandomMetadata { get; }
    public IReadOnlyList<string> NumericalWarnings => _numericalWarnings;
    public string DeterministicHash { get; }
}

public sealed record ReferenceImpactComparisonDelta(
    Energy DepositedEnergy,
    Energy OutgoingEnergy,
    int WoundSegmentCount,
    bool SameDisposition,
    bool ConservationStatusChanged);

/// <summary>Cross-model output whose two runs always use independent fresh targets.</summary>
public sealed class ReferenceImpactComparisonResult
{
    public const string CurrentOutputSchemaVersion = "reference-impact-comparison-v2";

    internal ReferenceImpactComparisonResult(
        string comparisonKey,
        ReferenceImpactResult baseline,
        ReferenceImpactResult candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonKey);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!string.Equals(comparisonKey, baseline.ComparisonKey, StringComparison.Ordinal)
            || !string.Equals(comparisonKey, candidate.ComparisonKey, StringComparison.Ordinal))
        {
            throw new ArgumentException("Comparison results must share the supplied comparison key.", nameof(comparisonKey));
        }

        OutputSchemaVersion = CurrentOutputSchemaVersion;
        ComparisonKey = comparisonKey;
        Baseline = baseline;
        Candidate = candidate;
        Delta = new ReferenceImpactComparisonDelta(
            Energy.FromJoules(
                candidate.EnergyLedger.TotalStructureDepositedEnergy.Joules
                - baseline.EnergyLedger.TotalStructureDepositedEnergy.Joules),
            Energy.FromJoules(
                candidate.EnergyLedger.OutgoingEnergy.Joules
                - baseline.EnergyLedger.OutgoingEnergy.Joules),
            candidate.WoundTrack.Segments.Count - baseline.WoundTrack.Segments.Count,
            candidate.WoundTrack.Disposition == baseline.WoundTrack.Disposition,
            candidate.EnergyLedger.IsConserved != baseline.EnergyLedger.IsConserved);
        DeterministicHash = ReferenceImpactCanonicalHash.ComputeSha256(
            new ReferenceImpactComparisonHashPayload(
                OutputSchemaVersion,
                ComparisonKey,
                Baseline,
                Candidate,
                Delta));
    }

    public string OutputSchemaVersion { get; }
    public string ComparisonKey { get; }
    public ReferenceImpactResult Baseline { get; }
    public ReferenceImpactResult Candidate { get; }
    public ReferenceImpactComparisonDelta Delta { get; }
    public string DeterministicHash { get; }
}

internal sealed record ReferenceImpactHashPayload(
    string OutputSchemaVersion,
    ReferenceImpactScenarioInput ScenarioInput,
    DamageModelVersion ModelVersion,
    string ModelIdentifier,
    string ComparisonKey,
    ulong Seed,
    WoundTrack WoundTrack,
    EnergyLedger EnergyLedger,
    ReferenceProjectileStateSnapshot FinalProjectileState,
    ReferenceLesionOutput Lesions,
    IReadOnlyList<PhysiologyTimelinePoint> PhysiologyTimeline,
    IReadOnlyList<CapabilityTimelinePoint> CapabilityTimeline,
    DeterministicRandomMetadataSnapshot RandomMetadata,
    IReadOnlyList<string> NumericalWarnings);

internal sealed record ReferenceImpactComparisonHashPayload(
    string OutputSchemaVersion,
    string ComparisonKey,
    ReferenceImpactResult Baseline,
    ReferenceImpactResult Candidate,
    ReferenceImpactComparisonDelta Delta);

internal static class ReferenceImpactCanonicalHash
{
    internal static string ComputeSha256<T>(T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, DamageModelJson.CreateOptions());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                             static property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
