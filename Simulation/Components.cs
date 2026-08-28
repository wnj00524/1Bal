using Friflo.Engine.ECS;

namespace ProxyState.Simulation;

// LOD tags are intentionally empty. They let systems select update frequency
// without adding per-entity storage to the component data.
public struct Tier1LodTag : ITag { }
public struct Tier2LodTag : ITag { }
public struct Tier3LodTag : ITag { }

public struct Identity : IComponent
{
    public int NameId;
    public int OccupationId;
}

public struct PoliticalAlignment : IComponent
{
    public byte FactionId;
}

public struct Psychology : IComponent
{
    public long TraitMask;
}

public struct AgentState : IComponent
{
    public int CurrentActionHash;
}

// Numeric agent attributes are kept in schema order. The shared schema supplies
// the meaning of each index, avoiding a per-agent dictionary and fixed fields.
public struct AgentAttributes : IComponent
{
    public float[] Values;
}

public static class SimulationDefaults
{
    public const int AgentCount = 1_000;
    public const float FatigueStressIncreasePerTick = 0.1f;
    public const float MaximumFatigueStress = 100f;
    public const float MaximumWealth = 10_000f;
}
