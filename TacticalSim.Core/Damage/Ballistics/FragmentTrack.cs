using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json.Serialization;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>Terminal disposition for a projectile or fragment inside the body.</summary>
public enum ProjectileDisposition
{
    Exited = 0,
    Retained = 1
}

/// <summary>
/// Immutable, deterministically ordered path of one generated fragment. All points
/// are in the same body-local, meter-based coordinate space as the parent track.
/// </summary>
public sealed class FragmentTrack
{
    private readonly ReadOnlyCollection<WoundTrackSegment> _segments;

    [JsonConstructor]
    public FragmentTrack(
        int sequence,
        string fragmentId,
        Vector3 entryPoint,
        ProjectileDisposition disposition,
        Vector3? exitPoint,
        Vector3? retainedPoint,
        Energy initialEnergy,
        Energy finalEnergy,
        IReadOnlyList<WoundTrackSegment>? segments)
    {
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Fragment sequence must be non-negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentId);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        WoundTrackContractGuards.RequireFinite(entryPoint, nameof(entryPoint));
        EnergyContractGuards.RequireNonNegative(initialEnergy, nameof(initialEnergy));
        EnergyContractGuards.RequireNonNegative(finalEnergy, nameof(finalEnergy));
        if (finalEnergy.Joules > initialEnergy.Joules)
            throw new ArgumentException("A fragment track cannot increase kinetic energy.", nameof(finalEnergy));

        WoundTrackSegment[] copiedSegments = segments?.ToArray() ?? [];
        WoundTrackContractGuards.ValidateSegments(copiedSegments, entryPoint, disposition, exitPoint, retainedPoint);
        if (copiedSegments[0].IncomingEnergy != initialEnergy
            || copiedSegments[^1].OutgoingEnergy != finalEnergy)
        {
            throw new ArgumentException(
                "Fragment initial/final energy must match its first and last segment states.",
                nameof(segments));
        }

        Sequence = sequence;
        FragmentId = fragmentId;
        EntryPoint = entryPoint;
        Disposition = disposition;
        ExitPoint = exitPoint;
        RetainedPoint = retainedPoint;
        InitialEnergy = initialEnergy;
        FinalEnergy = finalEnergy;
        _segments = Array.AsReadOnly(copiedSegments);
    }

    /// <summary>Stable fragment identifier supplied by the deterministic resolver.</summary>
    public string FragmentId { get; }
    public int Sequence { get; }
    public Vector3 EntryPoint { get; }
    public ProjectileDisposition Disposition { get; }
    public Vector3? ExitPoint { get; }
    public Vector3? RetainedPoint { get; }
    public bool IsRetained => Disposition == ProjectileDisposition.Retained;
    public Energy InitialEnergy { get; }
    public Energy FinalEnergy { get; }
    public IReadOnlyList<WoundTrackSegment> Segments => _segments;
}
