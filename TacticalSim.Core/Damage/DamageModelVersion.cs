using System;

namespace TacticalSim.Core.Damage;

/// <summary>
/// Selects the authoritative projectile-to-injury behavior used for an impact.
/// The value is recorded in debug traces and reference-scenario output.
/// </summary>
public enum DamageModelVersion
{
    /// <summary>
    /// Compatibility behavior that deposits the full impact energy at a point.
    /// It is retained only for explicit migration comparison.
    /// </summary>
    LegacyV1 = 1,

    /// <summary>
    /// M5 authoritative ordered-traversal model with energy accounting.
    /// </summary>
    FoundationsV2 = 2
}

/// <summary>
/// Feature-flag configuration for damage-model migration.
/// </summary>
public sealed record DamageModelOptions
{
    /// <summary>
    /// Creates model options. New simulations default to the M5 authoritative model;
    /// callers must opt in explicitly to <see cref="DamageModelVersion.LegacyV1"/>.
    /// </summary>
    public DamageModelOptions(DamageModelVersion defaultVersion = DamageModelVersion.FoundationsV2)
    {
        if (!Enum.IsDefined(defaultVersion))
            throw new ArgumentOutOfRangeException(nameof(defaultVersion));

        DefaultVersion = defaultVersion;
    }

    public DamageModelVersion DefaultVersion { get; }
}

public static class DamageModelVersionExtensions
{
    /// <summary>Stable identifier used by CLI, JSON, replay, and comparison output.</summary>
    public static string ToIdentifier(this DamageModelVersion version) => version switch
    {
        DamageModelVersion.LegacyV1 => "legacy-v1",
        DamageModelVersion.FoundationsV2 => "m5-foundations-v2",
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    public static DamageModelVersion ParseIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToLowerInvariant() switch
        {
            "legacy" or "legacy-v1" => DamageModelVersion.LegacyV1,
            "m5" or "foundations" or "foundations-v2" or "m5-foundations-v2" =>
                DamageModelVersion.FoundationsV2,
            _ => throw new ArgumentException($"Unknown damage-model version '{value}'.", nameof(value))
        };
    }
}
