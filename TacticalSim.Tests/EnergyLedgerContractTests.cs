using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class EnergyLedgerContractTests
{
    [Fact]
    public void Conservation_UsesInclusiveMixedAbsoluteAndRelativeTolerance()
    {
        var tolerance = new EnergyConservationTolerance(Energy.FromJoules(0.1f), 0.001f);

        EnergyLedger relativeBoundary = CreateLedger(
            incoming: 1_000f,
            outgoing: 899f,
            deposited: 100f,
            tolerance);
        EnergyLedger relativeOutside = CreateLedger(
            incoming: 1_000f,
            outgoing: 898.9f,
            deposited: 100f,
            tolerance);
        EnergyLedger absoluteBoundary = CreateLedger(
            incoming: 10f,
            outgoing: 4.9f,
            deposited: 5f,
            tolerance);

        Assert.True(relativeBoundary.IsConserved);
        Assert.Equal(1f, relativeBoundary.NumericalResidual.Joules, 5);
        Assert.Null(relativeBoundary.ConservationWarning);

        Assert.False(relativeOutside.IsConserved);
        Assert.NotNull(relativeOutside.ConservationWarning);
        Assert.True(relativeOutside.ValidateConservation().ResidualMagnitude.Joules > 1f);

        Assert.True(absoluteBoundary.IsConserved);
        Assert.Equal(0.1f, absoluteBoundary.ValidateConservation().AllowedResidual.Joules, 5);
    }

    [Fact]
    public void Ledger_RecordsSignedOverAllocationAsAWarning()
    {
        EnergyLedger ledger = CreateLedger(
            incoming: 100f,
            outgoing: 20f,
            deposited: 81f,
            new EnergyConservationTolerance(Energy.FromJoules(0.01f), 0f));

        Assert.Equal(-1f, ledger.NumericalResidual.Joules);
        Assert.False(ledger.IsConserved);
        Assert.Contains("-1", ledger.ConservationWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void Ledger_RejectsNegativeEnergyAndInvalidTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyLedger(
            Energy.FromJoules(-1f),
            Energy.FromJoules(0f),
            Array.Empty<EnergyDeposit>(),
            Energy.FromJoules(0f),
            Energy.FromJoules(0f)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyDeposit(
            0,
            "thorax.lung.left",
            Energy.FromJoules(-0.01f)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyConservationTolerance(
            Energy.FromJoules(0f),
            -0.001f));
    }

    [Fact]
    public void Ledger_DefensivelyCopiesDepositsAndRequiresDeterministicOrder()
    {
        var deposits = new List<EnergyDeposit>
        {
            new(0, "thorax.skin.anterior", Energy.FromJoules(5f))
        };

        var ledger = new EnergyLedger(
            Energy.FromJoules(10f),
            Energy.FromJoules(5f),
            deposits,
            Energy.FromJoules(0f),
            Energy.FromJoules(0f));
        deposits[0] = new EnergyDeposit(0, "changed", Energy.FromJoules(10f));
        deposits.Add(new EnergyDeposit(1, "also.changed", Energy.FromJoules(1f)));

        Assert.Single(ledger.StructureDeposits);
        Assert.Equal("thorax.skin.anterior", ledger.StructureDeposits[0].StructureId);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EnergyDeposit>)ledger.StructureDeposits).Add(
                new EnergyDeposit(1, "forbidden", Energy.FromJoules(1f))));

        Assert.Throws<ArgumentException>(() => new EnergyLedger(
            Energy.FromJoules(10f),
            Energy.FromJoules(9f),
            new[] { new EnergyDeposit(1, "out.of.order", Energy.FromJoules(1f)) },
            Energy.FromJoules(0f),
            Energy.FromJoules(0f)));
    }

    private static EnergyLedger CreateLedger(
        float incoming,
        float outgoing,
        float deposited,
        EnergyConservationTolerance tolerance) =>
        new(
            Energy.FromJoules(incoming),
            Energy.FromJoules(outgoing),
            new[] { new EnergyDeposit(0, "thorax.test", Energy.FromJoules(deposited)) },
            Energy.FromJoules(0f),
            Energy.FromJoules(0f),
            tolerance);
}
