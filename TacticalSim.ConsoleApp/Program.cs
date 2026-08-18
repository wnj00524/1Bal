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
                        
                        Console.WriteLine("\n--- Terminal Ballistics & Trauma Simulation ---");
                        // Transform ray to dummy local space for the voxel check. 
                        // To guarantee a hit on our test rig, we simulate the bullet just before impact on the torso center.
                        Vector3 torsoCenter = new Vector3(0, 0.25f, 0); 
                        Vector3 impactDir = Vector3.Normalize(torsoCenter - new Vector3(0, 0.25f, -10f));
                        
                        var ammo = shooter.EquippedWeapon.LoadedAmmunition;
                        var impactState = new ProjectileState 
                        {
                            Position = torsoCenter - (impactDir * 0.5f), // 0.5m in front
                            Velocity = impactDir * (ammo.MuzzleVelocity * 0.9f), // 10% velocity loss in air
                            Time = fs.Time
                        };
                        
                        float initialEnergy = 0.5f * ammo.Ballistics.Mass * impactState.Velocity.LengthSquared();
                        Console.WriteLine($"Impact Velocity: {impactState.Velocity.Length():F1} m/s");
                        Console.WriteLine($"Impact Energy: {initialEnergy:F1} Joules\n");
                        
                        int voxelsHit = 0;
                        foreach(var voxel in dummy.Physiology.RootBodyPart.Voxels)
                        {
                            var cavitation = voxel.ProcessPenetration(ref impactState, ammo.Ballistics);
                            
                            // If distance > 0, it means it hit the voxel bounding box, even if no radial cavitation was spawned
                            if (voxel.DepositedEnergy > 0)
                            {
                                voxelsHit++;
                                Console.WriteLine($"[HIT] Voxel at {voxel.Center} (Tissue: {voxel.Organ})");
                                Console.WriteLine($"  -> Energy Deposited: {voxel.DepositedEnergy:F1} Joules");
                                
                                if (cavitation != null)
                                {
                                    Console.WriteLine($"  -> Cavitation Radius: {cavitation.Value.Radius * 1000f:F1} mm");
                                }
                                
                                if (voxel.IsDestroyed) 
                                {
                                    Console.WriteLine($"  -> [CRITICAL] Tissue destroyed! Permanent cavitation induced.");
                                }
                            }
                        }
                        
                        Console.WriteLine($"\nTotal Voxels Penetrated: {voxelsHit}");
                        Console.WriteLine($"Exit Velocity: {impactState.Velocity.Length():F1} m/s");
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
