using TacticalSim.Core.Randomness;

namespace TacticalSim.Core.Damage.Variation;

public sealed record PhysiologicalVariation(
    float BloodVolumeMultiplier,
    float HeartRateOffsetBpm,
    float PressureOffsetMmhg,
    float StressResponseMultiplier);

/// <summary>Versioned bounded sampling. Samples alter baseline inputs, never causal outputs.</summary>
public sealed record PhysiologicalUncertaintyOptions
{
    public const string CurrentVersion = "physiology-uncertainty-v1";
    public bool Enabled { get; init; } = true;
    public float BloodVolumeFraction { get; init; } = 0.08f;
    public float HeartRateRangeBpm { get; init; } = 8f;
    public float PressureRangeMmhg { get; init; } = 7f;
    public float StressResponseFraction { get; init; } = 0.12f;
}

public static class PhysiologicalVariationSampler
{
    public static PhysiologicalVariation Sample(
        IDeterministicRandomStreamProvider random,
        string casualtyId,
        PhysiologicalUncertaintyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentException.ThrowIfNullOrWhiteSpace(casualtyId);
        options ??= new PhysiologicalUncertaintyOptions();
        if (!options.Enabled) return new(1f, 0f, 0f, 1f);
        Validate(options);
        var stream = random.GetStream($"m11/physiology/{casualtyId}");
        float Symmetric(float radius) => ((float)stream.NextUnitDouble() * 2f - 1f) * radius;
        return new(1f + Symmetric(options.BloodVolumeFraction), Symmetric(options.HeartRateRangeBpm),
            Symmetric(options.PressureRangeMmhg), 1f + Symmetric(options.StressResponseFraction));
    }

    private static void Validate(PhysiologicalUncertaintyOptions value)
    {
        if (value.BloodVolumeFraction is < 0f or > 0.25f || value.HeartRateRangeBpm is < 0f or > 30f ||
            value.PressureRangeMmhg is < 0f or > 25f || value.StressResponseFraction is < 0f or > 0.3f)
            throw new ArgumentOutOfRangeException(nameof(value), "Uncertainty exceeds documented safety bounds.");
    }
}
