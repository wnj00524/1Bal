## 2. Core ECS Data Structures

*Note to Coding Agent: Implement these as pure unmanaged structs utilizing `Friflo.Engine.ECS` interfaces.*

### 2.1 Agent Components (Ground Truth)

```csharp
using Friflo.Engine.ECS;

// Tags (Zero-byte markers)
public struct Tier1LodTag : ITag {} // Updates every tick
public struct Tier2LodTag : ITag {} // Updates hourly
public struct Tier3LodTag : ITag {} // Updates daily

// Components
public struct Identity : IComponent {
    public int NameId;       // Hash mapped to localization
    public int OccupationId; // Hash mapped to job data
}

public struct PoliticalAlignment : IComponent {
    public byte FactionId;   // Enum mapping (0: Blue, 1: Red, etc.)
    public float Preference; // 0.0 to 1.0
    public float Salience;   // 0.0 to 1.0 (willingness to act)
}

public struct BaseStats : IComponent {
    public byte Intelligence;
    public byte Charisma;
    public byte Perception;
    public byte Willpower;
}

public struct Psychology : IComponent {
    public long TraitMask;   // Bitmask: 1=Brave, 2=Chaste, 4=Greedy, 8=Paranoid
}

public struct AgentState : IComponent {
    public float Fatigue;
    public float Stress;
    public float Wealth;
    public int CurrentActionHash; 
}

```

### 2.2 The Social Graph (Edge Entities)

To model the social network and intelligence discovery, relationships are created as distinct Entities containing the `EdgeData` component, linking two agents.

```csharp
public struct EdgeData : IComponent {
    public Entity Source;
    public Entity Target;
    public float Affinity;       // -100 to 100

    // KNOWLEDGE MASKS (Parallel Bitmasks)
    // 1 = Source knows this data about Target; 0 = Hidden
    public long KnownTraitMask;      
    public byte KnownStatsMask;      
    public byte KnownPoliticalMask;  
}

```

---
a