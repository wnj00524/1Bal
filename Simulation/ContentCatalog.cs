using System.Text.Json;

namespace ProxyState.Simulation;

public sealed record TraitDefinition(string Id, string Name, long Bit, float Prevalence);
public sealed record ActionDefinition(string Id, string Name, int Hash);
public sealed record FactionDefinition(string Id, string Name, byte FactionId);
public sealed record AgentAttributeDefinition(string Id, float Min, float Max, float Average);

public sealed class AgentAttributeSchema
{
    private readonly Dictionary<string, int> _indices;

    internal AgentAttributeSchema(IReadOnlyList<AgentAttributeDefinition> definitions)
    {
        Definitions = definitions;
        _indices = definitions
            .Select((definition, index) => new { definition.Id, index })
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AgentAttributeDefinition> Definitions { get; }

    public int Count => Definitions.Count;

    public int GetIndex(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _indices.TryGetValue(id, out var index)
            ? index
            : throw new KeyNotFoundException($"Agent attribute '{id}' is not defined in the schema.");
    }
}

public sealed class AgentSchemaDocument
{
    public List<AgentAttributeDefinition>? Attributes { get; init; }
}

public sealed class ContentCatalog
{
    private ContentCatalog(
        IReadOnlyList<TraitDefinition> traits,
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<FactionDefinition> factions,
        AgentAttributeSchema agentAttributes)
    {
        Traits = traits;
        Actions = actions;
        Factions = factions;
        AgentAttributes = agentAttributes;
        AllTraitBits = traits.Aggregate(0L, (mask, trait) => mask | trait.Bit);
    }

    public IReadOnlyList<TraitDefinition> Traits { get; }
    public IReadOnlyList<ActionDefinition> Actions { get; }
    public IReadOnlyList<FactionDefinition> Factions { get; }
    public AgentAttributeSchema AgentAttributes { get; }
    public long AllTraitBits { get; }

    public static ContentCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var traits = LoadFile<TraitDefinition>(directory, "traits.json", options);
        var actions = LoadFile<ActionDefinition>(directory, "actions.json", options);
        var factions = LoadFile<FactionDefinition>(directory, "factions.json", options);
        var schemaDocument = LoadObject<AgentSchemaDocument>(directory, "agent-schema.json", options);

        var agentAttributes = Validate(traits, actions, factions, schemaDocument.Attributes);
        return new ContentCatalog(traits, actions, factions, agentAttributes);
    }

    private static IReadOnlyList<T> LoadFile<T>(
        string directory,
        string fileName,
        JsonSerializerOptions options)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required content file was not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, options)
            ?? throw new InvalidDataException($"Content file is empty or invalid: {path}");
    }

    private static T LoadObject<T>(
        string directory,
        string fileName,
        JsonSerializerOptions options)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required content file was not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new InvalidDataException($"Content file is empty or invalid: {path}");
    }

    private static AgentAttributeSchema Validate(
        IReadOnlyList<TraitDefinition> traits,
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<FactionDefinition> factions,
        IReadOnlyList<AgentAttributeDefinition>? attributeDefinitions)
    {
        if (traits.Count == 0 || actions.Count == 0 || factions.Count == 0)
        {
            throw new InvalidDataException("Traits, actions, and factions must each contain at least one definition.");
        }

        if (attributeDefinitions is null || attributeDefinitions.Count == 0)
        {
            throw new InvalidDataException("The agent attribute schema must contain at least one attribute.");
        }

        var traitBits = new HashSet<long>();
        var traitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trait in traits)
        {
            if (string.IsNullOrWhiteSpace(trait.Id) || !traitIds.Add(trait.Id))
            {
                throw new InvalidDataException($"Trait IDs must be non-empty and unique; '{trait.Id}' is invalid or duplicated.");
            }

            if (trait.Bit <= 0 || (trait.Bit & (trait.Bit - 1)) != 0 || !traitBits.Add(trait.Bit))
            {
                throw new InvalidDataException($"Trait '{trait.Id}' must have a unique positive single-bit value.");
            }

            if (!float.IsFinite(trait.Prevalence) || trait.Prevalence is < 0f or > 1f)
            {
                throw new InvalidDataException($"Trait '{trait.Id}' prevalence must be a finite value between 0 and 1.");
            }
        }

        if (factions.Select(faction => faction.FactionId).Distinct().Count() != factions.Count)
        {
            throw new InvalidDataException("Faction IDs must be unique.");
        }

        if (actions.Select(action => action.Hash).Distinct().Count() != actions.Count)
        {
            throw new InvalidDataException("Action hashes must be unique.");
        }

        var attributeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in attributeDefinitions)
        {
            if (string.IsNullOrWhiteSpace(attribute.Id) || !attributeIds.Add(attribute.Id))
            {
                throw new InvalidDataException($"Agent attribute IDs must be non-empty and unique; '{attribute.Id}' is invalid or duplicated.");
            }

            if (!float.IsFinite(attribute.Min) || !float.IsFinite(attribute.Max) || !float.IsFinite(attribute.Average))
            {
                throw new InvalidDataException($"Agent attribute '{attribute.Id}' must contain only finite numeric values.");
            }

            if (attribute.Min > attribute.Max || attribute.Average < attribute.Min || attribute.Average > attribute.Max)
            {
                throw new InvalidDataException($"Agent attribute '{attribute.Id}' must satisfy min <= average <= max.");
            }
        }

        var schema = new AgentAttributeSchema(attributeDefinitions);
        _ = schema.GetIndex("fatigue");
        _ = schema.GetIndex("stress");
        return schema;
    }
}
