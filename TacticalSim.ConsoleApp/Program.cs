using System;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TacticalSim.Core;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;

namespace TacticalSim.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddTacticalSimCore();
                })
                .Build();

            using var scope = host.Services.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ITurnResolver>();
            var environment = scope.ServiceProvider.GetRequiredService<IEnvironmentModel>();

            Console.WriteLine("======================================");
            Console.WriteLine(" TacticalSim Vertical Slice 1");
            Console.WriteLine("======================================");

            // Create Actor
            var actorPhysiology = new DummyPhysiology();
            actorPhysiology.SetRoot(new TacticalSim.Core.Physiology.BodyPart { Type = TacticalSim.Core.Physiology.BodyPartType.Thorax });
            
            var shooter = new TacticalEntity(new Vector3(0, 1.5f, -10f), actorPhysiology);
            shooter.EquippedWeapon = new WeaponProfile
            {
                Name = "Rifle",
                BaseTUCostToFire = 15f,
                LoadedAmmunition = new AmmunitionProfile
                {
                    Name = "5.56x45mm NATO",
                    MuzzleVelocity = 900f,
                    Ballistics = new BallisticProfile
                    {
                        Mass = 0.004f, // 4 grams
                        CrossSectionalArea = 0.000024f,
                        DragModel = new StandardDragCurve(0.3f)
                    }
                }
            };

            // Create Dummy
            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            var dummy = new TacticalEntity(new Vector3(0, 1.0f, 0), dummyPhysiology);
            
            Console.WriteLine($"Shooter spawned at {shooter.Position}");
            Console.WriteLine($"Dummy spawned at {dummy.Position}");
            Console.WriteLine();

            // Subscribe to events
            resolver.ActionStarted += (s, e) => Console.WriteLine($"[{resolver.GlobalTime:F2} TU] Action Started: {e.Action.GetType().Name} by Actor {e.Action.ActorId}");
            resolver.ActionCompleted += (s, e) => {
                Console.WriteLine($"[{resolver.GlobalTime:F2} TU] Action Completed: {e.Action.GetType().Name}");
                if (e.Action is ShootTacticalAction shootAction)
                {
                    if (shootAction.FinalState.HasValue)
                    {
                        var fs = shootAction.FinalState.Value;
                        Console.WriteLine($"   -> Final Projectile Position: {fs.Position}");
                        Console.WriteLine($"   -> Flight Time: {fs.Time:F4}s");
                        
                        // Check if dummy was hit
                        if (Vector3.Distance(fs.Position, dummy.Position) < 0.5f)
                        {
                            Console.WriteLine("   -> Dummy hit! (Scaffold intersection)");
                        }
                    }
                }
            };

            // Schedule the shot
            Vector3 targetDir = Vector3.Normalize(dummy.Position - shooter.Position);
            var shootAction = new ShootTacticalAction(shooter, targetDir, environment);
            resolver.ScheduleAction(shootAction);

            // Run timeline
            float timeStep = 5f; // 5 TU steps
            while (resolver.HasActiveActions)
            {
                resolver.Tick(timeStep);
            }

            // Report physiological state
            Console.WriteLine("\n--- Dummy Medical Report ---");
            Console.WriteLine($"Total Blood Volume: {dummy.Physiology.TotalBloodVolume:F1} ml");
            Console.WriteLine($"Consciousness Level: {dummy.Physiology.ConsciousnessLevel * 100:F1}%");
        }
    }
}
