using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json.Serialization;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>Coordinate-space marker persisted with every canonical wound track.</summary>
public enum WoundTrackCoordinateSpace
{
    /// <summary>
    /// System.Numerics vectors whose position components are meters relative to the
    /// target body's anatomical origin. Direction vectors use the same local axes.
    /// </summary>
    BodyLocalMeters = 0
}

/// <summary>
/// Immutable canonical record of a primary projectile path and any fragment paths.
/// It contains enough body-local geometry and projectile state to render a debug
/// view without re-running intersection or terminal-ballistics calculations.
/// </summary>
public sealed class WoundTrack
{
    private readonly ReadOnlyCollection<WoundTrackSegment> _segments;
    private readonly ReadOnlyCollection<FragmentTrack> _fragmentTracks;

    [JsonConstructor]
    public WoundTrack(
        string trackId,
        DamageModelVersion modelVersion,
        WoundTrackCoordinateSpace coordinateSpace,
        Vector3 entryPoint,
        ProjectileDisposition disposition,
        Vector3? exitPoint,
        Vector3? retainedPoint,
        IReadOnlyList<WoundTrackSegment>? segments,
        IReadOnlyList<FragmentTrack>? fragmentTracks,
        EnergyLedger energyLedger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        if (!Enum.IsDefined(modelVersion))
            throw new ArgumentOutOfRangeException(nameof(modelVersion));
        if (coordinateSpace != WoundTrackCoordinateSpace.BodyLocalMeters)
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace));
        ArgumentNullException.ThrowIfNull(energyLedger);
        WoundTrackContractGuards.RequireFinite(entryPoint, nameof(entryPoint));

        WoundTrackSegment[] copiedSegments = segments?.ToArray() ?? [];
        FragmentTrack[] copiedFragments = fragmentTracks?.ToArray() ?? [];
        WoundTrackContractGuards.ValidateSegments(copiedSegments, entryPoint, disposition, exitPoint, retainedPoint);
        WoundTrackContractGuards.ValidateFragments(copiedFragments);
        WoundTrackContractGuards.ValidateEnergyAccounting(
            modelVersion,
            copiedSegments,
            copiedFragments,
            energyLedger);

        TrackId = trackId;
        ModelVersion = modelVersion;
        CoordinateSpace = coordinateSpace;
        EntryPoint = entryPoint;
        Disposition = disposition;
        ExitPoint = exitPoint;
        RetainedPoint = retainedPoint;
        _segments = Array.AsReadOnly(copiedSegments);
        _fragmentTracks = Array.AsReadOnly(copiedFragments);
        EnergyLedger = energyLedger;
    }

    /// <summary>Stable track identifier supplied by the deterministic resolver.</summary>
    public string TrackId { get; }
    public DamageModelVersion ModelVersion { get; }
    public WoundTrackCoordinateSpace CoordinateSpace { get; }
    public Vector3 EntryPoint { get; }
    public ProjectileDisposition Disposition { get; }
    public Vector3? ExitPoint { get; }
    public Vector3? RetainedPoint { get; }
    public bool IsRetained => Disposition == ProjectileDisposition.Retained;
    public IReadOnlyList<WoundTrackSegment> Segments => _segments;
    public IReadOnlyList<FragmentTrack> FragmentTracks => _fragmentTracks;
    public EnergyLedger EnergyLedger { get; }
}

internal static class WoundTrackContractGuards
{
    public static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }

    public static void RequireNonNegative(Units.Distance value, string parameterName)
    {
        if (value.Meters < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Distance values must be non-negative.");
    }

    public static void ValidateSegments(
        IReadOnlyList<WoundTrackSegment> segments,
        Vector3 entryPoint,
        ProjectileDisposition disposition,
        Vector3? exitPoint,
        Vector3? retainedPoint)
    {
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (segments.Count == 0)
            throw new ArgumentException("A wound or fragment track must contain at least one segment.", nameof(segments));

        switch (disposition)
        {
            case ProjectileDisposition.Exited when exitPoint is null || retainedPoint is not null:
                throw new ArgumentException("An exited track must have an exit point and no retained point.");
            case ProjectileDisposition.Retained when retainedPoint is null || exitPoint is not null:
                throw new ArgumentException("A retained track must have a retained point and no exit point.");
        }

        if (exitPoint is { } exit)
            RequireFinite(exit, nameof(exitPoint));
        if (retainedPoint is { } retained)
            RequireFinite(retained, nameof(retainedPoint));

        for (int index = 0; index < segments.Count; index++)
        {
            WoundTrackSegment segment = segments[index]
                ?? throw new ArgumentException("Track segments cannot contain null entries.", nameof(segments));
            if (segment.Sequence != index)
                throw new ArgumentException("Segments must be in contiguous deterministic sequence order starting at zero.", nameof(segments));
            if (index > 0 && !EnergyEquals(
                    segments[index - 1].OutgoingEnergy,
                    segment.IncomingEnergy,
                    EnergyConservationTolerance.Default))
            {
                throw new ArgumentException(
                    "Each segment's incoming energy must match the previous segment's outgoing energy.",
                    nameof(segments));
            }
        }

        if (segments[0].EntryPoint != entryPoint)
            throw new ArgumentException("Track entry point must match the first segment entry point.", nameof(entryPoint));

        Vector3 terminalPoint = disposition == ProjectileDisposition.Exited
            ? exitPoint!.Value
            : retainedPoint!.Value;
        if (segments[^1].EndPoint != terminalPoint)
            throw new ArgumentException("Track terminal point must match the final segment end point.");

        WoundTrackSegment terminalSegment = segments[^1];
        if (disposition == ProjectileDisposition.Retained
            && (terminalSegment.ProjectileStateChange.Kind != ProjectileStateChangeKind.Retained
                || terminalSegment.OutgoingEnergy.Joules
                    > EnergyConservationTolerance.Default.Absolute.Joules))
        {
            throw new ArgumentException(
                "A retained track must end with a retained, effectively zero-energy projectile state.",
                nameof(segments));
        }
        if (disposition == ProjectileDisposition.Exited
            && terminalSegment.ProjectileStateChange.Kind == ProjectileStateChangeKind.Retained)
        {
            throw new ArgumentException(
                "An exited track cannot end with a retained projectile state.",
                nameof(segments));
        }
    }

    public static void ValidateFragments(IReadOnlyList<FragmentTrack> fragments)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < fragments.Count; index++)
        {
            FragmentTrack fragment = fragments[index]
                ?? throw new ArgumentException("Fragment tracks cannot contain null entries.", nameof(fragments));
            if (fragment.Sequence != index)
                throw new ArgumentException("Fragments must be in contiguous deterministic sequence order starting at zero.", nameof(fragments));
            if (!identifiers.Add(fragment.FragmentId))
                throw new ArgumentException("Fragment identifiers must be unique within a wound track.", nameof(fragments));
        }
    }

    public static void ValidateEnergyAccounting(
        DamageModelVersion modelVersion,
        IReadOnlyList<WoundTrackSegment> primarySegments,
        IReadOnlyList<FragmentTrack> fragments,
        EnergyLedger ledger)
    {
        EnergyConservationTolerance tolerance = ledger.ConservationTolerance;
        if (!EnergyEquals(ledger.IncomingEnergy, primarySegments[0].IncomingEnergy, tolerance))
            throw new ArgumentException("The ledger incoming energy must match the wound track's first segment.", nameof(ledger));

        double aggregateOutgoingJoules = primarySegments[^1].OutgoingEnergy.Joules;
        foreach (FragmentTrack fragment in fragments)
            aggregateOutgoingJoules += fragment.FinalEnergy.Joules;
        Units.Energy aggregateOutgoing = Units.Energy.FromJoules((float)aggregateOutgoingJoules);
        if (!EnergyEquals(ledger.OutgoingEnergy, aggregateOutgoing, tolerance))
            throw new ArgumentException("The ledger outgoing energy must match all terminal projectile and fragment energy.", nameof(ledger));

        WoundTrackSegment[] depositedSegments = primarySegments
            .Concat(fragments.SelectMany(static fragment => fragment.Segments))
            .ToArray();
        if (ledger.StructureDeposits.Count != depositedSegments.Length)
            throw new ArgumentException("The ledger must contain exactly one ordered deposit for every wound-track segment.", nameof(ledger));

        for (int index = 0; index < depositedSegments.Length; index++)
        {
            WoundTrackSegment segment = depositedSegments[index];
            EnergyDeposit deposit = ledger.StructureDeposits[index];
            if (deposit.Sequence != index
                || !string.Equals(deposit.StructureId, segment.StructureId, StringComparison.Ordinal)
                || !EnergyEquals(deposit.DepositedEnergy, segment.TransferredEnergy, tolerance))
            {
                throw new ArgumentException(
                    "Each ordered ledger deposit must match its wound-track segment identifier and transferred energy.",
                    nameof(ledger));
            }
        }

        if (modelVersion is not (DamageModelVersion.FoundationsV2 or DamageModelVersion.IntegratedV3))
            return;

        foreach (WoundTrackSegment segment in depositedSegments)
        {
            float projectileLossJoules = segment.IncomingEnergy.Joules - segment.OutgoingEnergy.Joules;
            Units.Energy maximumTransfer = Units.Energy.FromJoules(MathF.Max(0f, projectileLossJoules));
            Units.Energy allowed = tolerance.AllowedResidual(segment.IncomingEnergy, segment.OutgoingEnergy);
            if (segment.TransferredEnergy.Joules > maximumTransfer.Joules + allowed.Joules)
            {
                throw new ArgumentException(
                    "An authoritative segment cannot transfer more energy than the projectile lost.",
                    nameof(primarySegments));
            }
        }

        // A non-conserved ledger remains representable for diagnostics. If the
        // aggregate ledger claims conservation, however, local primary/fragment
        // histories must also balance so unrelated contradictions cannot cancel.
        if (!ledger.IsConserved)
            return;

        foreach (FragmentTrack fragment in fragments)
        {
            double fragmentDepositsJoules = fragment.Segments.Sum(
                static segment => (double)segment.TransferredEnergy.Joules);
            Units.Energy fragmentAllocated = Units.Energy.FromJoules(
                (float)(fragment.FinalEnergy.Joules + fragmentDepositsJoules));
            if (!EnergyEquals(fragment.InitialEnergy, fragmentAllocated, tolerance))
            {
                throw new ArgumentException(
                    "A conserved track requires every fragment's initial energy to equal its final energy plus segment deposits.",
                    nameof(fragments));
            }
        }

        double primaryDepositsJoules = primarySegments.Sum(
            static segment => (double)segment.TransferredEnergy.Joules);
        double primaryAllocatedJoules = primarySegments[^1].OutgoingEnergy.Joules
            + primaryDepositsJoules
            + fragments.Sum(static fragment => (double)fragment.InitialEnergy.Joules)
            + ledger.DeformationEnergy.Joules
            + ledger.FragmentationEnergy.Joules;
        Units.Energy primaryAllocated = Units.Energy.FromJoules((float)primaryAllocatedJoules);
        if (!EnergyEquals(primarySegments[0].IncomingEnergy, primaryAllocated, tolerance))
        {
            throw new ArgumentException(
                "A conserved track requires primary incoming energy to fund its final state, deposits, fragment initial energy, deformation, and fragmentation.",
                nameof(primarySegments));
        }
    }

    private static bool EnergyEquals(
        Units.Energy left,
        Units.Energy right,
        EnergyConservationTolerance tolerance)
    {
        Units.Energy allowed = tolerance.AllowedResidual(left, right);
        return MathF.Abs(left.Joules - right.Joules) <= allowed.Joules;
    }
}
