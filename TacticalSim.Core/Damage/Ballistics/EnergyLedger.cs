using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// Identifies an ordered energy transfer to one anatomical structure.
/// <see cref="StructureId"/> is a stable anatomy identifier, not a display name or
/// voxel index. Multiple entries may reference the same structure when a path
/// intersects it more than once.
/// </summary>
public readonly record struct EnergyDeposit
{
    [JsonConstructor]
    public EnergyDeposit(int sequence, string structureId, Energy depositedEnergy)
    {
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Deposit sequence must be non-negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(structureId);
        EnergyContractGuards.RequireNonNegative(depositedEnergy, nameof(depositedEnergy));

        Sequence = sequence;
        StructureId = structureId;
        DepositedEnergy = depositedEnergy;
    }

    public int Sequence { get; }
    public string StructureId { get; }
    public Energy DepositedEnergy { get; }
}

/// <summary>
/// Mixed absolute/relative tolerance used for projectile-energy conservation.
/// A ledger is conserved when the absolute residual is no greater than
/// <c>max(Absolute, Relative * max(abs(incoming), abs(allocated)))</c>.
/// </summary>
public readonly record struct EnergyConservationTolerance
{
    /// <summary>
    /// Provisional M5 floating-point guard: 0.0001 J absolute and one part per
    /// million relative. These values identify numerical/accounting defects; they
    /// are not a physiological or gameplay tuning parameter and are to be reviewed
    /// during M12 calibration.
    /// </summary>
    public static EnergyConservationTolerance Default { get; } =
        new(Energy.FromJoules(0.0001f), 0.000001f);

    [JsonConstructor]
    public EnergyConservationTolerance(Energy absolute, float relative)
    {
        EnergyContractGuards.RequireNonNegative(absolute, nameof(absolute));
        if (!float.IsFinite(relative) || relative < 0f)
            throw new ArgumentOutOfRangeException(nameof(relative), "Relative tolerance must be finite and non-negative.");

        Absolute = absolute;
        Relative = relative;
    }

    public Energy Absolute { get; }
    public float Relative { get; }

    internal Energy AllowedResidual(Energy incoming, Energy allocated)
    {
        double scale = Math.Max(Math.Abs((double)incoming.Joules), Math.Abs((double)allocated.Joules));
        double allowed = Math.Max(Absolute.Joules, Relative * scale);
        return Energy.FromJoules((float)allowed);
    }
}

/// <summary>Result of validating an <see cref="EnergyLedger"/>.</summary>
public readonly record struct EnergyConservationValidation(
    bool IsConserved,
    Energy ResidualMagnitude,
    Energy AllowedResidual,
    string? Warning);

/// <summary>
/// Immutable accounting record for one projectile interaction.
/// </summary>
/// <remarks>
/// The signed numerical residual is calculated as incoming energy minus outgoing,
/// structure-deposited, deformation, and fragmentation energy. A positive value is
/// unallocated energy; a negative value means the interaction allocated too much.
/// Conservation failures remain representable so diagnostics and replay data are
/// not lost; inspect <see cref="IsConserved"/> or <see cref="ConservationWarning"/>.
/// </remarks>
public sealed class EnergyLedger
{
    private readonly ReadOnlyCollection<EnergyDeposit> _structureDeposits;

    /// <summary>
    /// Creates a ledger and defensively copies the ordered deposits.
    /// </summary>
    [JsonConstructor]
    public EnergyLedger(
        Energy incomingEnergy,
        Energy outgoingEnergy,
        IReadOnlyList<EnergyDeposit>? structureDeposits,
        Energy deformationEnergy,
        Energy fragmentationEnergy,
        EnergyConservationTolerance conservationTolerance)
    {
        EnergyContractGuards.RequireNonNegative(incomingEnergy, nameof(incomingEnergy));
        EnergyContractGuards.RequireNonNegative(outgoingEnergy, nameof(outgoingEnergy));
        EnergyContractGuards.RequireNonNegative(deformationEnergy, nameof(deformationEnergy));
        EnergyContractGuards.RequireNonNegative(fragmentationEnergy, nameof(fragmentationEnergy));

        EnergyDeposit[] deposits = structureDeposits?.ToArray() ?? [];
        ValidateOrdered(deposits);

        IncomingEnergy = incomingEnergy;
        OutgoingEnergy = outgoingEnergy;
        _structureDeposits = Array.AsReadOnly(deposits);
        DeformationEnergy = deformationEnergy;
        FragmentationEnergy = fragmentationEnergy;
        ConservationTolerance = conservationTolerance;

        double depositedJoules = deposits.Sum(static deposit => (double)deposit.DepositedEnergy.Joules);
        TotalStructureDepositedEnergy = Energy.FromJoules((float)depositedJoules);

        double allocatedJoules = outgoingEnergy.Joules
            + depositedJoules
            + deformationEnergy.Joules
            + fragmentationEnergy.Joules;
        AllocatedEnergy = Energy.FromJoules((float)allocatedJoules);
        NumericalResidual = Energy.FromJoules((float)(incomingEnergy.Joules - allocatedJoules));

        EnergyConservationValidation validation = ValidateConservation();
        IsConserved = validation.IsConserved;
        ConservationWarning = validation.Warning;
    }

    /// <summary>
    /// Convenience overload for callers producing a lazy or otherwise enumerable
    /// deposit sequence. The sequence is enumerated exactly once and copied.
    /// </summary>
    public EnergyLedger(
        Energy incomingEnergy,
        Energy outgoingEnergy,
        IEnumerable<EnergyDeposit>? structureDeposits,
        Energy deformationEnergy,
        Energy fragmentationEnergy,
        EnergyConservationTolerance? conservationTolerance = null)
        : this(
            incomingEnergy,
            outgoingEnergy,
            (IReadOnlyList<EnergyDeposit>)(structureDeposits?.ToArray() ?? []),
            deformationEnergy,
            fragmentationEnergy,
            conservationTolerance ?? EnergyConservationTolerance.Default)
    {
    }

    public Energy IncomingEnergy { get; }
    public Energy OutgoingEnergy { get; }
    public IReadOnlyList<EnergyDeposit> StructureDeposits => _structureDeposits;
    public Energy TotalStructureDepositedEnergy { get; }
    public Energy DeformationEnergy { get; }
    public Energy FragmentationEnergy { get; }
    public Energy AllocatedEnergy { get; }
    public Energy NumericalResidual { get; }
    public EnergyConservationTolerance ConservationTolerance { get; }
    public bool IsConserved { get; }
    public string? ConservationWarning { get; }

    public EnergyConservationValidation ValidateConservation()
    {
        Energy magnitude = Energy.FromJoules(MathF.Abs(NumericalResidual.Joules));
        Energy allowed = ConservationTolerance.AllowedResidual(IncomingEnergy, AllocatedEnergy);
        bool conserved = magnitude.Joules <= allowed.Joules;
        string? warning = conserved
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Projectile energy residual {NumericalResidual.Joules:R} J exceeds the allowed {allowed.Joules:R} J.");

        return new EnergyConservationValidation(conserved, magnitude, allowed, warning);
    }

    private static void ValidateOrdered(IReadOnlyList<EnergyDeposit> deposits)
    {
        for (int index = 0; index < deposits.Count; index++)
        {
            if (deposits[index].Sequence != index)
            {
                throw new ArgumentException(
                    "Energy deposits must be supplied in deterministic, contiguous sequence order starting at zero.",
                    nameof(deposits));
            }
        }
    }
}

internal static class EnergyContractGuards
{
    public static void RequireNonNegative(Energy value, string parameterName)
    {
        if (value.Joules < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Energy values must be non-negative.");
    }
}
