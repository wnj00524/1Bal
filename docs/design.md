Here is the updated Technical Design Document (TDD). This version has been specifically formulated for an AI coding assistant. It removes all game-engine boilerplate (like Godot nodes or Unity Monobehaviours) and provides precise C# struct definitions mapped to **Friflo.Engine.ECS**, alongside a **Raylib/ImGui** bootstrapper.

You can copy and paste everything below the line directly to your coding agent to begin Milestone 1.

---

# Technical Design Document (TDD)

**Project Name:** [Temp: Proxy State]
**Genre:** Grand Strategy / Espionage Management
**Tech Stack:**

* **Language:** C# (.NET 8.0+)
* **ECS Framework:** `Friflo.Engine.ECS`
* **Rendering & Input:** `Raylib-cs`
* **UI Framework:** `rlImGui-cs` (Dear ImGui bindings for Raylib)

## 1. Architectural Directives

* **Paradigm:** 100% Code-First, Data-Oriented Design. No visual editor is used.
* **UI Aesthetic:** Terminal/Hacker Interface using Immediate Mode GUI (ImGui) overlaid on a Raylib 2D canvas.
* **ECS Usage:** Utilize `Friflo.Engine.ECS` native features. Use pure `struct` implementations for `IComponent` and `ITag`.
* **Data-Driven:** Content (traits, actions, factions) must be serialized via JSON.
* **The Intelligence Isolation Layer:** The ImGui UI must NEVER query the Ground Truth ECS data. It only queries the `PlayerIntelligenceDB` or the specific `KnowledgeMask` of an active player-controlled entity.

---

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

## 3. Application Bootstrapper (Raylib + ImGui)

*Note to Coding Agent: The entry point should initialize Raylib, inject rlImGui, and maintain the ECS World tick loop.*

```csharp
using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;
using Friflo.Engine.ECS;

public static class Program {
    public static void Main() {
        // 1. Init Raylib
        Raylib.InitWindow(1280, 720, "Proxy State - Intelligence Terminal");
        Raylib.SetTargetFPS(60);

        // 2. Init ImGui
        rlImGui.Setup(true); // true = dark theme

        // 3. Init ECS World
        var store = new EntityStore();
        
        // TODO: Load JSON Data & Populate Initial Entities here

        // 4. Main Game Loop
        while (!Raylib.WindowShouldClose()) {
            // -- ECS Update Phase --
            // store.Update(); (Trigger Friflo Query Systems)

            // -- Rendering Phase --
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(15, 15, 15, 255)); 

            // Draw Raylib 2D map/nodes here

            // -- UI Phase (ImGui) --
            rlImGui.Begin();
            
            ImGui.Begin("Intelligence Dossier");
            ImGui.Text("Agent Data will render here...");
            // TODO: Implement Player UI querying only known knowledge masks
            ImGui.End();

            rlImGui.End();
            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}

```

---

## 4. Core ECS Systems (Logic)

### 4.1 Utility AI System

**Goal:** Determine what an agent does this tick.

* Query all entities with `AgentState` and `Tier1LodTag`.
* Iterate through available actions (Work, Rest, Socialize) loaded from `actions.json`.
* Score formula: `BaseScore + (TraitModifiers) - (Fatigue/Stress Penalties)`.
* Assign highest-scoring action to `AgentState.CurrentActionHash`.

### 4.2 Interaction & Discovery System

**Goal:** Handle target interrogation/surveillance based on Perception vs Willpower.

* When `Source` interacts with `Target`, calculate: `Source.Perception` vs `Target.Willpower` (modified by Target's `Paranoid` trait).
* On success, perform bitwise `OR` on `EdgeData.KnownTraitMask`. (e.g., `KnownTraitMask |= 0x0004` to reveal the Greedy trait).
* Recalculate `Affinity` by checking shared traits: `Target.Psychology.TraitMask & EdgeData.KnownTraitMask`.

---

## 5. Development Milestones for Coding Agent

### Milestone 1: The Core Framework & Dummy Simulation

1. Set up a .NET 8 Console Application project.
2. Install NuGet packages: `Friflo.Engine.ECS`, `Raylib-cs`, `rlImGui-cs`.
3. Implement the `Program.cs` bootstrapper as defined in Section 3.
4. Implement the struct definitions from Section 2.
5. Write a spawner function to instantiate 1,000 dummy entities with randomized stats and traits.
6. Write a basic `Friflo` System that slowly increases `Fatigue` and `Stress` on all entities and resets them when they hit 100.

### Milestone 2: Social Graph & Bitwise Discovery

1. Implement the `EdgeData` relationship entities.
2. Assign 5 random relationships (Edge Entities) to each Agent upon generation.
3. Implement an `InteractionSystem` that runs every X ticks, forcing an agent to roll Perception to reveal a bit of their target's `Psychology.TraitMask`.
4. Update the `EdgeData.KnownTraitMask` accordingly.

### Milestone 3: The ImGui Intelligence Terminal

1. Create an ImGui window titled "Surveillance Terminal".
2. Draw a list of all Agents. When the user clicks an Agent, open their "Dossier".
3. **Crucial Security Check:** The Dossier UI must ONLY display traits that are unlocked in the Player's Knowledge Mask for that Agent. Use bitwise `AND` (`&`) logic. If the mask bit is `0`, render `"Trait: ???"`. If `1`, render the trait name.