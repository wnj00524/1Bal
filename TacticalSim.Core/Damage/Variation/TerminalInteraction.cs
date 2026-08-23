using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Variation;

public enum ProjectileConstruction { FullMetalJacket, HollowPoint, SoftPoint, Frangible }

/// <summary>Data-driven, conservative terminal behavior independent of nominal calibre.</summary>
public sealed record TerminalProjectileProfile(
    string Id,
    ProjectileConstruction Construction,
    float YawTendency,
    float DeformationThresholdMetersPerSecond,
    float ExpandedAreaMultiplier,
    float FragmentationThresholdMetersPerSecond,
    float RetainedMassFraction)
{
    public BallisticProfile Apply(BallisticProfile source, float impactSpeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (YawTendency is < 0f or > 1f || ExpandedAreaMultiplier is < 1f or > 4f || RetainedMassFraction is <= 0f or > 1f)
            throw new InvalidOperationException("Terminal profile values are outside supported bounds.");
        float deformation = impactSpeed >= DeformationThresholdMetersPerSecond ? ExpandedAreaMultiplier : 1f;
        float fragmentation = impactSpeed >= FragmentationThresholdMetersPerSecond ? RetainedMassFraction : 1f;
        return new BallisticProfile
        {
            Mass = source.MassKilograms.Kilograms * fragmentation,
            CrossSectionalArea = source.CrossSectionalAreaSquareMeters.SquareMeters * deformation * (1f + 0.25f * YawTendency),
            DragModel = source.DragModel
        };
    }
}

public sealed record WearableBarrierLayer(string Id, float EnergyLossJoules, float VelocityMultiplier, bool CanCauseBluntTrauma);
public sealed record WearableInteractionResult(Energy IncomingEnergy, Energy ResidualEnergy, float ResidualSpeedMetersPerSecond,
    bool Penetrated, Energy BluntEnergy, IReadOnlyList<string> AppliedLayers);

/// <summary>Core pre-entry hook for clothing and bounded first-version armor effects.</summary>
public static class WearableBarrierResolver
{
    public static WearableInteractionResult Resolve(float massKilograms, float speedMetersPerSecond,
        IEnumerable<WearableBarrierLayer> layers)
    {
        if (massKilograms <= 0f || speedMetersPerSecond < 0f) throw new ArgumentOutOfRangeException();
        float incoming = 0.5f * massKilograms * speedMetersPerSecond * speedMetersPerSecond;
        float remaining = incoming;
        float speedMultiplier = 1f;
        float blunt = 0f;
        var applied = new List<string>();
        foreach (var layer in layers ?? throw new ArgumentNullException(nameof(layers)))
        {
            if (layer.EnergyLossJoules < 0f || layer.VelocityMultiplier is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(layers));
            applied.Add(layer.Id);
            float absorbed = MathF.Min(remaining, layer.EnergyLossJoules);
            remaining -= absorbed;
            speedMultiplier *= layer.VelocityMultiplier;
            if (layer.CanCauseBluntTrauma) blunt += absorbed;
            if (remaining <= 0f) break;
        }
        float energyLimitedSpeed = remaining > 0f ? MathF.Sqrt(2f * remaining / massKilograms) : 0f;
        float residualSpeed = energyLimitedSpeed * speedMultiplier;
        float residual = 0.5f * massKilograms * residualSpeed * residualSpeed;
        return new(Energy.FromJoules(incoming), Energy.FromJoules(residual), residualSpeed, residual > 0f,
            Energy.FromJoules(blunt), applied.AsReadOnly());
    }
}
