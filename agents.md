
## Agent Instructions: TacticalSim


**Project Name:** Proxy State

**Tech Stack:**

* **Language:** C# (.NET 8.0+)
* **ECS Framework:** `Friflo.Engine.ECS`
* **Rendering & Input:** `Raylib-cs`
* **UI Framework:** `rlImGui-cs` (Dear ImGui bindings for Raylib)



### Architectural Directives

* **Paradigm:** 100% Code-First, Data-Oriented Design. No visual editor is used.
* **UI Aesthetic:** Windows 3.1 style GUI and a terminal with Raylib 2D canvas.
* **ECS Usage:** Utilize `Friflo.Engine.ECS` native features. Use pure `struct` implementations for `IComponent` and `ITag`.
* **Data-Driven:** Content (traits, actions, factions) must be serialized via JSON.
* **The Intelligence Isolation Layer:** The ImGui UI must NEVER query the Ground Truth ECS data. It only queries the `PlayerIntelligenceDB` or the specific `KnowledgeMask` of an active player-controlled entity.



### Documentation 

When adding code, please comment for a human reviewer to explain the relevant logic. 
Make sure that design principles/data structures/GUI rules are documented in the "docs" directory. Add documents to the list below as they are added: 

** Core ECS Systems (Logic):** can be found in docs/coreecs.md. When updating ECS logic, update this document to reflect the changes. 
** Data Structures: ** See docs/datastructs.md for data structures. When editing, adding or removing a data structure make sure to update this file to reflect the change.

### Core ECS Systems



## GitHub Project & Tracking

Agents working in this repository MUST track their progress and align tasks with the GitHub Project "PState".

Milestones: Check whether a task fits within a milestone. If it does not, then add a new milestone. 

Labels: Apply relevant labels when working a task. Examples are "feature", "bug", "framework". Check docs/labels.md for existig labels. If a relevant label can't be found, add a new one to the list. 

Before starting any work, you MUST check if the proposed work fits under an existing issue on the repository.

If the work does NOT fit under an existing issue, you MUST use the GitHub CLI to create a new issue detailing the task, and add it to the PState project.

Before making code or documentation changes, you MUST move the appropriate issue to the "In progress" status on the PState project using the GitHub CLI (for example, gh project item-edit --project-id ...).

Keep issue status, milestone, priority, size, labels, dependencies, and acceptance criteria synchronized with the implementation.

Project Updates: When implementing new features, always verify if issues need to be transitioned across columns in the PState project when completed.

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