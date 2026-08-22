using System.Numerics;
using System.Text.Json.Serialization;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>Observable projectile outcome at the end of a wound-track segment.</summary>
public enum ProjectileStateChangeKind
{
    Unchanged = 0,
    Deformed = 1,
    Deflected = 2,
    Ricocheted = 3,
    Fragmented = 4,
    Retained = 5
}

/// <summary>
/// Immutable before/after projectile state recorded by the resolver. Positions are
/// body-local coordinates in meters; directions are unitless body-local vectors.
/// No presentation-engine vector types are permitted at this boundary.
/// </summary>
public sealed class ProjectileStateChange
{
    [JsonConstructor]
    public ProjectileStateChange(
        int sequence,
        ProjectileStateChangeKind kind,
        Vector3 position,
        Vector3 incomingDirection,
        Vector3 outgoingDirection,
        Energy incomingEnergy,
        Energy outgoingEnergy)
    {
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "State-change sequence must be non-negative.");
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        WoundTrackContractGuards.RequireFinite(position, nameof(position));
        WoundTrackContractGuards.RequireFinite(incomingDirection, nameof(incomingDirection));
        WoundTrackContractGuards.RequireFinite(outgoingDirection, nameof(outgoingDirection));
        EnergyContractGuards.RequireNonNegative(incomingEnergy, nameof(incomingEnergy));
        EnergyContractGuards.RequireNonNegative(outgoingEnergy, nameof(outgoingEnergy));
        if (outgoingEnergy.Joules > incomingEnergy.Joules)
            throw new ArgumentException("A projectile state change cannot increase kinetic energy.", nameof(outgoingEnergy));

        Sequence = sequence;
        Kind = kind;
        Position = position;
        IncomingDirection = incomingDirection;
        OutgoingDirection = outgoingDirection;
        IncomingEnergy = incomingEnergy;
        OutgoingEnergy = outgoingEnergy;
    }

    public int Sequence { get; }
    public ProjectileStateChangeKind Kind { get; }
    public Vector3 Position { get; }
    public Vector3 IncomingDirection { get; }
    public Vector3 OutgoingDirection { get; }
    public Energy IncomingEnergy { get; }
    public Energy OutgoingEnergy { get; }
}
