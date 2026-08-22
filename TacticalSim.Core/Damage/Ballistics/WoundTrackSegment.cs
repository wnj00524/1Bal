using System.Numerics;
using System.Text.Json.Serialization;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// One ordered intersection between a projectile path and a stable anatomical
/// structure. Entry/end points are body-local coordinates in meters and path length
/// uses the canonical typed distance.
/// </summary>
public sealed class WoundTrackSegment
{
    [JsonConstructor]
    public WoundTrackSegment(
        int sequence,
        string structureId,
        string bodyRegion,
        string structureType,
        Vector3 entryPoint,
        Vector3 endPoint,
        Distance pathLength,
        Energy incomingEnergy,
        Energy transferredEnergy,
        Energy outgoingEnergy,
        ProjectileStateChange projectileStateChange)
    {
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Segment sequence must be non-negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(structureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyRegion);
        ArgumentException.ThrowIfNullOrWhiteSpace(structureType);
        ArgumentNullException.ThrowIfNull(projectileStateChange);
        WoundTrackContractGuards.RequireFinite(entryPoint, nameof(entryPoint));
        WoundTrackContractGuards.RequireFinite(endPoint, nameof(endPoint));
        WoundTrackContractGuards.RequireNonNegative(pathLength, nameof(pathLength));
        EnergyContractGuards.RequireNonNegative(incomingEnergy, nameof(incomingEnergy));
        EnergyContractGuards.RequireNonNegative(transferredEnergy, nameof(transferredEnergy));
        EnergyContractGuards.RequireNonNegative(outgoingEnergy, nameof(outgoingEnergy));

        if (outgoingEnergy.Joules > incomingEnergy.Joules)
            throw new ArgumentException("A segment cannot increase projectile kinetic energy.", nameof(outgoingEnergy));
        if (transferredEnergy.Joules > incomingEnergy.Joules)
            throw new ArgumentException("A segment cannot transfer more energy than entered it.", nameof(transferredEnergy));
        if (projectileStateChange.Sequence != sequence)
            throw new ArgumentException("The projectile state-change sequence must match its segment sequence.", nameof(projectileStateChange));
        if (projectileStateChange.Position != endPoint
            || projectileStateChange.IncomingEnergy != incomingEnergy
            || projectileStateChange.OutgoingEnergy != outgoingEnergy)
        {
            throw new ArgumentException(
                "The projectile state change must record the segment end point and matching incoming/outgoing energy.",
                nameof(projectileStateChange));
        }

        float geometricLengthMeters = Vector3.Distance(entryPoint, endPoint);
        float allowedLengthErrorMeters = MathF.Max(1e-6f, geometricLengthMeters * 1e-5f);
        if (MathF.Abs(pathLength.Meters - geometricLengthMeters) > allowedLengthErrorMeters)
        {
            throw new ArgumentException(
                "Segment path length must match the distance between its body-local endpoints.",
                nameof(pathLength));
        }

        Sequence = sequence;
        StructureId = structureId;
        BodyRegion = bodyRegion;
        StructureType = structureType;
        EntryPoint = entryPoint;
        EndPoint = endPoint;
        PathLength = pathLength;
        IncomingEnergy = incomingEnergy;
        TransferredEnergy = transferredEnergy;
        OutgoingEnergy = outgoingEnergy;
        ProjectileStateChange = projectileStateChange;
    }

    public int Sequence { get; }

    /// <summary>Stable anatomy identifier; never a transient voxel/list index.</summary>
    public string StructureId { get; }

    /// <summary>Stable body-region identifier retained for debug display.</summary>
    public string BodyRegion { get; }

    /// <summary>Stable structure-type identifier retained for debug display.</summary>
    public string StructureType { get; }

    public Vector3 EntryPoint { get; }
    public Vector3 EndPoint { get; }
    public Distance PathLength { get; }
    public Energy IncomingEnergy { get; }
    public Energy TransferredEnergy { get; }
    public Energy OutgoingEnergy { get; }
    public ProjectileStateChange ProjectileStateChange { get; }
}
