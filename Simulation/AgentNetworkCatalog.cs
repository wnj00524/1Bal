using System.Text.Json;

namespace ProxyState.Simulation;

public enum NetworkHierarchyMode
{
    Flat,
    SingleSupervisor
}

public enum NetworkRemainderHandling
{
    MergeIntoPrevious,
    CreateUndersized
}

public enum NetworkPartitionStrategy
{
    Global,
    HomeLocation,
    WorkLocation
}

public sealed record NetworkRoleDefinition(string Id, string Name, int Hash, int NetworkTypeHash);

public sealed record NetworkTypeDefinition(
    string Id,
    string Name,
    int Hash,
    NetworkHierarchyMode HierarchyMode,
    int MaxNetworksPerAgent,
    bool SeedsSocialGraph,
    IReadOnlyList<int> RoleHashes);

public sealed record NetworkSizeWeight(int Size, int Weight);

public sealed record NetworkGeneratorDefinition(
    string Id,
    int Hash,
    int NetworkTypeHash,
    NetworkPartitionStrategy PartitionStrategy,
    int MinimumSize,
    int MaximumSize,
    IReadOnlyList<NetworkSizeWeight> SizeWeights,
    NetworkRemainderHandling RemainderHandling,
    int MemberRoleHash,
    int RootRoleHash,
    int ManagerRoleHash,
    int LeafRoleHash,
    int TargetSpanOfControl,
    int MaximumSpanOfControl,
    int MaximumDepth);

/// <summary>
/// Validated, immutable network content. All string references are resolved once
/// while loading so simulation code can use compact integer keys exclusively.
/// </summary>
public sealed class AgentNetworkCatalog
{
    private readonly Dictionary<string, NetworkTypeDefinition> _typesById;
    private readonly Dictionary<int, NetworkTypeDefinition> _typesByHash;
    private readonly Dictionary<string, NetworkRoleDefinition> _rolesById;
    private readonly Dictionary<int, NetworkRoleDefinition> _rolesByHash;
    private readonly Dictionary<string, NetworkGeneratorDefinition> _generatorsById;
    private readonly Dictionary<int, NetworkGeneratorDefinition> _generatorsByHash;

    internal AgentNetworkCatalog(
        IReadOnlyList<NetworkTypeDefinition> types,
        IReadOnlyList<NetworkRoleDefinition> roles,
        IReadOnlyList<NetworkGeneratorDefinition> generators)
    {
        Types = types;
        Roles = roles;
        Generators = generators;
        _typesById = types.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        _typesByHash = types.ToDictionary(item => item.Hash);
        _rolesById = roles.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        _rolesByHash = roles.ToDictionary(item => item.Hash);
        _generatorsById = generators.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        _generatorsByHash = generators.ToDictionary(item => item.Hash);
    }

    public IReadOnlyList<NetworkTypeDefinition> Types { get; }
    public IReadOnlyList<NetworkRoleDefinition> Roles { get; }
    public IReadOnlyList<NetworkGeneratorDefinition> Generators { get; }

    public NetworkTypeDefinition GetType(string id) => Get(_typesById, id, "network type");
    public NetworkTypeDefinition GetType(int hash) => Get(_typesByHash, hash, "network type");
    public NetworkRoleDefinition GetRole(string id) => Get(_rolesById, id, "network role");
    public NetworkRoleDefinition GetRole(int hash) => Get(_rolesByHash, hash, "network role");
    public NetworkGeneratorDefinition GetGenerator(string id) => Get(_generatorsById, id, "network generator");
    public NetworkGeneratorDefinition GetGenerator(int hash) => Get(_generatorsByHash, hash, "network generator");

    private static T Get<T>(IReadOnlyDictionary<string, T> lookup, string id, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return lookup.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown {kind} '{id}'.");
    }

    private static T Get<T>(IReadOnlyDictionary<int, T> lookup, int hash, string kind) =>
        lookup.TryGetValue(hash, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown {kind} hash '{hash}'.");

    internal static AgentNetworkCatalog Load(string path, JsonSerializerOptions options)
    {
        var document = JsonSerializer.Deserialize<NetworkCatalogDocument>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException($"Content file is empty or invalid: {path}");
        return NetworkCatalogValidator.Validate(document);
    }
}

internal sealed class NetworkCatalogDocument
{
    public List<NetworkTypeDocument>? NetworkTypes { get; init; }
    public List<NetworkRoleDocument>? Roles { get; init; }
    public List<NetworkGeneratorDocument>? Generators { get; init; }
}

internal sealed record NetworkTypeDocument(
    string? Id,
    string? Name,
    string? HierarchyMode,
    int MaxNetworksPerAgent,
    bool SeedsSocialGraph,
    List<string>? Roles);
internal sealed record NetworkRoleDocument(string? Id, string? Name, string? NetworkType);
internal sealed record NetworkSizeWeightDocument(int Size, int Weight);
internal sealed record NetworkGeneratorDocument(
    string? Id,
    string? NetworkType,
    string? PartitionKey,
    int MinimumSize,
    int MaximumSize,
    List<NetworkSizeWeightDocument>? SizeWeights,
    string? RemainderHandling,
    string? MemberRole,
    string? RootRole,
    string? ManagerRole,
    string? LeafRole,
    int TargetSpanOfControl,
    int MaximumSpanOfControl,
    int MaximumDepth);

internal static class NetworkCatalogValidator
{
    public static AgentNetworkCatalog Validate(NetworkCatalogDocument document)
    {
        if (document.NetworkTypes is null || document.NetworkTypes.Count == 0 ||
            document.Roles is null || document.Roles.Count == 0 ||
            document.Generators is null || document.Generators.Count == 0)
        {
            throw Invalid("Networks must define at least one network type, role, and generator.");
        }

        EnsureUniqueIds(document.NetworkTypes.Select(item => item.Id), "network type");
        EnsureUniqueIds(document.Roles.Select(item => item.Id), "network role");
        EnsureUniqueIds(document.Generators.Select(item => item.Id), "network generator");

        var typeDocuments = document.NetworkTypes.ToDictionary(item => item.Id!, StringComparer.OrdinalIgnoreCase);
        var typeHashes = BuildHashes(typeDocuments.Keys, "network type");
        var roleDocuments = document.Roles.ToDictionary(item => item.Id!, StringComparer.OrdinalIgnoreCase);
        var roleHashes = BuildHashes(roleDocuments.Keys, "network role");
        var generatorHashes = BuildHashes(document.Generators.Select(item => item.Id!), "network generator");

        var roles = new List<NetworkRoleDefinition>();
        foreach (var role in document.Roles)
        {
            RequireName(role.Name, $"Network role '{role.Id}'");
            if (string.IsNullOrWhiteSpace(role.NetworkType) || !typeDocuments.ContainsKey(role.NetworkType))
            {
                throw Invalid($"Network role '{role.Id}' references an unknown network type '{role.NetworkType}'.");
            }
            roles.Add(new(role.Id!, role.Name!, roleHashes[role.Id!], typeHashes[role.NetworkType]));
        }

        var types = new List<NetworkTypeDefinition>();
        foreach (var type in document.NetworkTypes)
        {
            RequireName(type.Name, $"Network type '{type.Id}'");
            var mode = ParseHierarchy(type.HierarchyMode, type.Id!);
            if (type.MaxNetworksPerAgent <= 0)
                throw Invalid($"Network type '{type.Id}' membership cardinality must be positive.");
            if (type.Roles is null || type.Roles.Count == 0 || type.Roles.Any(string.IsNullOrWhiteSpace))
                throw Invalid($"Network type '{type.Id}' must reference at least one role.");
            if (type.Roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != type.Roles.Count)
                throw Invalid($"Network type '{type.Id}' contains duplicate role references.");

            var hashes = type.Roles.Select(roleId =>
            {
                if (!roleDocuments.TryGetValue(roleId, out var role))
                    throw Invalid($"Network type '{type.Id}' references missing role '{roleId}'.");
                if (!string.Equals(role.NetworkType, type.Id, StringComparison.OrdinalIgnoreCase))
                    throw Invalid($"Role '{roleId}' belongs to a different network type than '{type.Id}'.");
                return roleHashes[roleId];
            }).ToArray();
            types.Add(new(type.Id!, type.Name!, typeHashes[type.Id!], mode, type.MaxNetworksPerAgent,
                type.SeedsSocialGraph, hashes));
        }

        var rolesById = roles.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var typesById = types.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var generators = document.Generators.Select(generator => ValidateGenerator(
            generator, generatorHashes[generator.Id!], typesById, rolesById)).ToArray();
        return new AgentNetworkCatalog(types, roles, generators);
    }

    private static NetworkGeneratorDefinition ValidateGenerator(
        NetworkGeneratorDocument generator,
        int hash,
        IReadOnlyDictionary<string, NetworkTypeDefinition> types,
        IReadOnlyDictionary<string, NetworkRoleDefinition> roles)
    {
        if (string.IsNullOrWhiteSpace(generator.NetworkType) || !types.TryGetValue(generator.NetworkType, out var type))
            throw Invalid($"Network generator '{generator.Id}' references unknown network type '{generator.NetworkType}'.");
        var partition = generator.PartitionKey?.ToLowerInvariant() switch
        {
            "global" => NetworkPartitionStrategy.Global,
            "home-location" => NetworkPartitionStrategy.HomeLocation,
            "work-location" => NetworkPartitionStrategy.WorkLocation,
            _ => throw Invalid($"Network generator '{generator.Id}' has unknown partition key '{generator.PartitionKey}'.")
        };
        var remainder = generator.RemainderHandling?.ToLowerInvariant() switch
        {
            "merge-into-previous" => NetworkRemainderHandling.MergeIntoPrevious,
            "create-undersized" => NetworkRemainderHandling.CreateUndersized,
            _ => throw Invalid($"Network generator '{generator.Id}' has invalid remainder handling '{generator.RemainderHandling}'.")
        };
        if (generator.MinimumSize <= 0 || generator.MaximumSize < generator.MinimumSize)
            throw Invalid($"Network generator '{generator.Id}' has invalid size bounds.");
        if (generator.SizeWeights is null || generator.SizeWeights.Count == 0 ||
            generator.SizeWeights.Any(item => item.Size < generator.MinimumSize || item.Size > generator.MaximumSize || item.Weight <= 0) ||
            generator.SizeWeights.Select(item => item.Size).Distinct().Count() != generator.SizeWeights.Count)
            throw Invalid($"Network generator '{generator.Id}' has invalid sizes or weights.");
        if (remainder == NetworkRemainderHandling.CreateUndersized && generator.MinimumSize == 1)
            throw Invalid($"Network generator '{generator.Id}' requests impossible undersized remainder handling with minimum size one.");

        int RoleHash(string? id, string field, bool required)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                if (!required) return 0;
                throw Invalid($"Network generator '{generator.Id}' is missing its {field} role.");
            }
            if (!roles.TryGetValue(id, out var role))
                throw Invalid($"Network generator '{generator.Id}' references missing role '{id}'.");
            if (role.NetworkTypeHash != type.Hash)
                throw Invalid($"Network generator '{generator.Id}' references cross-type role '{id}'.");
            return role.Hash;
        }

        var flat = type.HierarchyMode == NetworkHierarchyMode.Flat;
        var member = RoleHash(generator.MemberRole, "member", flat);
        var root = RoleHash(generator.RootRole, "root", !flat);
        var manager = RoleHash(generator.ManagerRole, "manager", !flat);
        var leaf = RoleHash(generator.LeafRole, "leaf", !flat);
        if (flat && (!string.IsNullOrWhiteSpace(generator.RootRole) || !string.IsNullOrWhiteSpace(generator.ManagerRole) ||
                     !string.IsNullOrWhiteSpace(generator.LeafRole) || generator.TargetSpanOfControl != 0 ||
                     generator.MaximumSpanOfControl != 0 || generator.MaximumDepth != 0))
            throw Invalid($"Flat network generator '{generator.Id}' contains incompatible hierarchy fields.");
        if (!flat && (!string.IsNullOrWhiteSpace(generator.MemberRole) || generator.TargetSpanOfControl <= 0 ||
                      generator.MaximumSpanOfControl < generator.TargetSpanOfControl || generator.MaximumDepth <= 0))
            throw Invalid($"Hierarchical network generator '{generator.Id}' has incompatible span/depth fields.");
        if (!flat && Capacity(generator.MaximumSpanOfControl, generator.MaximumDepth) < generator.MaximumSize)
            throw Invalid($"Company generator '{generator.Id}' maximum size exceeds hierarchy capacity.");

        return new(generator.Id!, hash, type.Hash, partition, generator.MinimumSize, generator.MaximumSize,
            generator.SizeWeights.Select(item => new NetworkSizeWeight(item.Size, item.Weight)).ToArray(), remainder,
            member, root, manager, leaf, generator.TargetSpanOfControl, generator.MaximumSpanOfControl, generator.MaximumDepth);
    }

    private static long Capacity(int span, int depth)
    {
        long total = 1, level = 1;
        for (var i = 0; i < depth; i++)
        {
            level = Math.Min(int.MaxValue, level * span);
            total = Math.Min(int.MaxValue, total + level);
        }
        return total;
    }

    private static Dictionary<string, int> BuildHashes(IEnumerable<string> ids, string kind)
    {
        var result = ids.ToDictionary(id => id, StableHash, StringComparer.OrdinalIgnoreCase);
        if (result.Values.Distinct().Count() != result.Count)
            throw Invalid($"{kind} identifiers produce duplicate hashes.");
        return result;
    }

    // FNV-1a is stable across processes, unlike string.GetHashCode().
    public static int StableHash(string id)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in id.ToLowerInvariant())
                hash = (hash ^ character) * 16777619;
            return (int)hash;
        }
    }

    private static void EnsureUniqueIds(IEnumerable<string?> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw Invalid($"{kind} IDs must be non-empty and unique; '{id}' is invalid or duplicated.");
    }

    private static NetworkHierarchyMode ParseHierarchy(string? value, string id) => value?.ToLowerInvariant() switch
    {
        "flat" => NetworkHierarchyMode.Flat,
        "single-supervisor" => NetworkHierarchyMode.SingleSupervisor,
        _ => throw Invalid($"Network type '{id}' has invalid hierarchy mode '{value}'.")
    };

    private static void RequireName(string? name, string owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Invalid($"{owner} must have a display name.");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
