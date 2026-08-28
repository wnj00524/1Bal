# Proxy State

Proxy State is a code-first .NET 8 simulation built around Friflo.Engine.ECS,
Raylib-cs, and rlImGui-cs. Milestone 3 provides the core ECS components, JSON
content catalogs, schema-driven agent generation, binary trait masks, a world
clock, networked locations, jobs, commuting, a fatigue/stress simulation loop,
and a randomized bidirectional social graph with scheduled bitwise discovery.

## Run

```text
dotnet run --project ProxyState.csproj
```

To enable the development-only agent inspector, pass `-debug`:

```text
dotnet run --project ProxyState.csproj -- -debug
```

The application loads numeric agent attributes from `data/agent-schema.json`,
traits from `data/traits.json`, jobs from `data/jobs.json`, and the location
network from `data/world.json`. It then opens the Raylib canvas and the ImGui
Applications program manager. One in-world day advances in about ten real minutes, and
agents commute along shortest-time routes between assigned homes and workplaces.
The `Applications` window acts as the program manager: double-click `Dossiers`
to open the `Surveillance Terminal`, or, in debug mode, double-click the
`Debug Window` icon to open the development inspector. The debug window lists
all agents and shows the full copied simulation state for the selected agent.

The simulation randomly selects five agents as Operatives (or all agents when
the population is smaller). Selected Operatives have the `Officer`
IntelligenceRole; all other agents default to `None`. The Surveillance Terminal
lists every agent, shows any assigned intelligence role, and displays only
traits discovered by at least one Operative. Operative knowledge is combined at
the ECS/UI boundary; hidden traits are displayed as `Trait: ???`.

Every mode also includes a bottom status bar showing the in-game day, weekday,
and time of day from the simulation clock. Agents have five unique social peers
represented by reciprocal directed edge entities. Every 60 simulation ticks,
each edge can discover one present target trait through an opposed Perception
versus Willpower d100 contest; Paranoid targets receive a 20-point Willpower
bonus.

## Test

```text
dotnet test ProxyState.sln
```
