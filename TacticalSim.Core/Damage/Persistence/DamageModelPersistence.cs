using System.Text.Json;
using TacticalSim.Core.Damage.Lesions;

namespace TacticalSim.Core.Damage.Persistence;

public sealed record PersistedAction(float TimeSeconds, Guid ActorId, string ActionType, string Payload);
public sealed record DamageModelSave(string SchemaVersion, string ModelVersion, string AnatomyVersion,
    string LesionSchemaVersion, IReadOnlyList<Lesion> Lesions);
public sealed record DamageModelReplay(string SchemaVersion, string ModelVersion, string AnatomyVersion,
    ulong RootSeed, IReadOnlyDictionary<string, ulong> NamedStreamSeeds, IReadOnlyList<PersistedAction> Actions);

/// <summary>Strict save/replay envelope. Schema changes require an explicitly registered migration.</summary>
public sealed class DamageModelPersistence
{
    public const string CurrentSaveSchema = "damage-save-v1";
    public const string CurrentReplaySchema = "damage-replay-v1";
    public const string CurrentLesionSchema = "lesion-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<(string From, string To), Func<string, string>> _migrations = [];

    public void RegisterMigration(string fromSchema, string toSchema, Func<string, string> migrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSchema); ArgumentException.ThrowIfNullOrWhiteSpace(toSchema);
        ArgumentNullException.ThrowIfNull(migrate);
        if (!_migrations.TryAdd((fromSchema, toSchema), migrate)) throw new InvalidOperationException("Migration is already registered.");
    }

    public string SerializeSave(DamageModelSave save)
    {
        ValidateSave(save); return JsonSerializer.Serialize(save, JsonOptions);
    }

    public DamageModelSave DeserializeSave(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        string schema = ReadSchema(json);
        if (schema != CurrentSaveSchema)
        {
            if (!_migrations.TryGetValue((schema, CurrentSaveSchema), out var migrate))
                throw new NotSupportedException($"Save schema '{schema}' is incompatible with '{CurrentSaveSchema}'; an explicit migration is required.");
            json = migrate(json);
        }
        var save = JsonSerializer.Deserialize<DamageModelSave>(json, JsonOptions) ?? throw new JsonException("Save payload is empty.");
        ValidateSave(save); return save;
    }

    public string SerializeReplay(DamageModelReplay replay)
    {
        ValidateReplay(replay); return JsonSerializer.Serialize(replay, JsonOptions);
    }

    public DamageModelReplay DeserializeReplay(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        string schema = ReadSchema(json);
        if (schema != CurrentReplaySchema) throw new NotSupportedException($"Replay schema '{schema}' is incompatible with '{CurrentReplaySchema}'.");
        var replay = JsonSerializer.Deserialize<DamageModelReplay>(json, JsonOptions) ?? throw new JsonException("Replay payload is empty.");
        ValidateReplay(replay); return replay;
    }

    private static string ReadSchema(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("SchemaVersion", out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new JsonException("SchemaVersion is required.");
    }
    private static void ValidateSave(DamageModelSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.SchemaVersion != CurrentSaveSchema) throw new NotSupportedException($"Unsupported save schema '{save.SchemaVersion}'.");
        RequireVersions(save.ModelVersion, save.AnatomyVersion);
        if (save.LesionSchemaVersion != CurrentLesionSchema) throw new NotSupportedException($"Lesion schema '{save.LesionSchemaVersion}' cannot be silently reinterpreted.");
        if (save.Lesions is null) throw new ArgumentException("Lesions are required.", nameof(save));
    }
    private static void ValidateReplay(DamageModelReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.SchemaVersion != CurrentReplaySchema) throw new NotSupportedException($"Unsupported replay schema '{replay.SchemaVersion}'.");
        RequireVersions(replay.ModelVersion, replay.AnatomyVersion);
        if (replay.NamedStreamSeeds is null || replay.Actions is null) throw new ArgumentException("Replay seeds and actions are required.", nameof(replay));
        float previous = -1;
        foreach (var action in replay.Actions)
        {
            if (!float.IsFinite(action.TimeSeconds) || action.TimeSeconds < previous) throw new ArgumentException("Replay actions must have finite, monotonic timestamps.", nameof(replay));
            previous = action.TimeSeconds;
        }
    }
    private static void RequireVersions(string model, string anatomy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model); ArgumentException.ThrowIfNullOrWhiteSpace(anatomy);
    }
}
