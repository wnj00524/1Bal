using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using ImGuiNET;
using ProxyState.Simulation;
using Raylib_cs;
using rlImGui_cs;

namespace ProxyState;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ContentValidation.IsRequested(args))
            return ContentValidation.Run(args, Console.Out, Console.Error);
        var debugMode = DebugMode.IsEnabled(args);
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = ContentCatalog.Load(contentDirectory);
        var store = new EntityStore();
        var spawner = new AgentSpawner(catalog);

        // A fresh seed gives each interactive run a new population. The spawner
        // accepts Random explicitly so tests and future replay tools can inject one.
        spawner.Spawn(store, SimulationDefaults.AgentCount, new Random());
        // Retain the immutable bootstrap indexes for indexed simulation systems
        // introduced by subsequent Milestone 17 slices.
        var agentSocialIndexes = spawner.Indexes;

        var clock = new WorldClockSystem(store);
        var systems = new SystemRoot(store)
        {
            clock,
            new AgentDecisionSystem(store, catalog, clock.ClockEntity, captureDiagnostics: debugMode,
                socialIndexes: agentSocialIndexes),
            new CoordinationSystem(store, catalog, clock.ClockEntity, agentSocialIndexes),
            new IntentExecutionSystem(store, catalog, clock.ClockEntity),
            new ActivityEffectsSystem(catalog, clock.ClockEntity),
            new InteractionSystem(catalog, new Random())
        };

        Raylib.InitWindow(1280, 720, "Proxy State - Applications");
        Raylib.SetTargetFPS(60);

        try
        {
            rlImGui.Setup(true);
            Windows31Theme.Apply();
            var applicationShell = new ApplicationShell();
            var dossierWindow = new DossierWindow();
            var debugWindow = debugMode ? new DebugWindow() : null;

            while (!Raylib.WindowShouldClose())
            {
                // Simulation runs before rendering so the frame presents the
                // state produced by the current ECS tick.
                clock.Advance(Raylib.GetFrameTime());
                systems.Update(default);
                var worldTime = WorldTimeSnapshot.From(clock.ClockEntity.GetComponent<WorldTime>());
                var intelligence = PlayerIntelligenceDB.Capture(store, catalog);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 15, 255));

                rlImGui.Begin();
                applicationShell.DrawLauncher(debugMode);
                applicationShell.DrawDossiersWindow(intelligence, catalog.Traits, dossierWindow);
                if (debugWindow is not null && applicationShell.DebugWindowOpen)
                {
                    // Capture immutable values before drawing so the UI never
                    // reaches into the Ground Truth ECS store directly.
                    applicationShell.DrawDebugWindow(DebugSnapshotBuilder.CaptureInspection(store, catalog), debugWindow);
                }
                WorldTimeBar.Draw(worldTime);
                rlImGui.End();

                Raylib.EndDrawing();
            }
        }
        finally
        {
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
        return 0;
    }
}
