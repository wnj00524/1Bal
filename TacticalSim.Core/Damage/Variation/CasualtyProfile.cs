using System.Text.Json.Serialization;

namespace TacticalSim.Core.Damage.Variation;

/// <summary>Serializable, scenario-owned baseline physiology for one casualty.</summary>
public sealed record CasualtyProfile
{
    public const string CurrentSchemaVersion = "casualty-profile-v1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Id { get; init; } = "standard-adult";
    public float BodyMassKilograms { get; init; } = 70f;
    public float BloodVolumeMilliliters { get; init; } = 5000f;
    public float BaselineHeartRateBpm { get; init; } = 80f;
    public float BaselineMeanArterialPressureMmhg { get; init; } = 93f;
    public float OxygenCarryingCapacity { get; init; } = 1f;
    public StressResponseProfile StressResponse { get; init; } = StressResponseProfile.Standard;
    public IReadOnlyDictionary<string, float> ComorbidityModifiers { get; init; } =
        new Dictionary<string, float>();

    [JsonIgnore]
    public static CasualtyProfile Default { get; } = new();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new ArgumentException("Unsupported casualty profile schema.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (BodyMassKilograms is < 20f or > 250f) throw new ArgumentOutOfRangeException(nameof(BodyMassKilograms));
        if (BloodVolumeMilliliters is < 1000f or > 12000f) throw new ArgumentOutOfRangeException(nameof(BloodVolumeMilliliters));
        if (BaselineHeartRateBpm is < 30f or > 180f) throw new ArgumentOutOfRangeException(nameof(BaselineHeartRateBpm));
        if (BaselineMeanArterialPressureMmhg is < 35f or > 160f) throw new ArgumentOutOfRangeException(nameof(BaselineMeanArterialPressureMmhg));
        if (OxygenCarryingCapacity is < 0.5f or > 1.25f) throw new ArgumentOutOfRangeException(nameof(OxygenCarryingCapacity));
    }

    public static CasualtyProfile FromBodyMass(string id, float kilograms) => new()
    {
        Id = id,
        BodyMassKilograms = kilograms,
        // Provisional adult scenario rule: 70 ml/kg, deliberately bounded by Validate.
        BloodVolumeMilliliters = kilograms * 70f
    };
}

public enum StressResponseProfile { Blunted, Standard, Heightened }
