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

            var knifeAmmo = new AmmunitionProfile
            {
                Name = "Combat Knife (Abdomen)",
                MuzzleVelocity = 15f,
                Ballistics = new BallisticProfile { Mass = 0.4f, CrossSectionalArea = 0.00015f, DragModel = new StandardDragCurve(10.0f) }
            };

            Console.WriteLine("======================================");
            Console.WriteLine(" TacticalSim Ammunition Comparison Test");
            Console.WriteLine("======================================\n");

            var dummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            var dummy = new TacticalEntity(new Vector3(0, 1.0f, 0), dummyPhysiology);
            
            // 5.56x45mm NATO
            var rifleAmmo = new AmmunitionProfile
            {
                Name = "5.56x45mm NATO",
                MuzzleVelocity = 900f,
                Ballistics = new BallisticProfile
                {
                    Mass = 0.004f, // 4 grams
                    CrossSectionalArea = 0.000024f,
                    DragModel = new StandardDragCurve(0.3f)
                }
            };
            
            // .45 ACP
            var handgunAmmo = new AmmunitionProfile
            {
                Name = ".45 ACP",
                MuzzleVelocity = 250f, // subsonic
                Ballistics = new BallisticProfile
                {
                    Mass = 0.0149f, // 14.9 grams (230 grain)
                    CrossSectionalArea = 0.000103f, // 11.43mm diameter
                    DragModel = new StandardDragCurve(0.2f)
                }
            };
            
            var knifeNeckAmmo = new AmmunitionProfile
            {
                Name = "Combat Knife (Neck)",
                MuzzleVelocity = 15f,
                Ballistics = new BallisticProfile { Mass = 0.4f, CrossSectionalArea = 0.00015f, DragModel = new StandardDragCurve(10.0f) }
            };

            var nineMmHeadAmmo = new AmmunitionProfile
            {
                Name = "9x19mm (Head)",
                MuzzleVelocity = 380f,
                Ballistics = new BallisticProfile { Mass = 0.008f, CrossSectionalArea = 0.0000636f, DragModel = new StandardDragCurve(0.15f) }
            };

            var ammoProfiles = new[] { rifleAmmo, handgunAmmo, knifeAmmo, knifeNeckAmmo, nineMmHeadAmmo };
            foreach(var ammo in ammoProfiles)
            {
                SimulateTerminalBallistics(dummy, ammo);
            }
        }

        static void SimulateTerminalBallistics(TacticalEntity dummy, AmmunitionProfile ammo)
        {
            Console.WriteLine($"--- Terminal Ballistics: {ammo.Name} ---");
            
            // Setup initial bullet state right before impact
            float aimYOffset = 0.25f; // Chest
            if (ammo.Name.Contains("Abdomen")) aimYOffset = 0.10f;
            else if (ammo.Name.Contains("Neck")) aimYOffset = 0.58f;
            else if (ammo.Name.Contains("Head")) aimYOffset = 0.76f;
            
            Vector3 globalTorsoCenter = dummy.Position + new Vector3(0, aimYOffset, 0); 
            Vector3 impactDir = Vector3.Normalize(globalTorsoCenter - new Vector3(0, globalTorsoCenter.Y, -10f));
            
            var impactState = new ProjectileState 
            {
                Position = globalTorsoCenter - (impactDir * 0.15f),
                Velocity = impactDir * (ammo.MuzzleVelocity * 0.9f),
                Time = 0f
            };
            
            float initialEnergy = 0.5f * ammo.Ballistics.Mass * impactState.Velocity.LengthSquared();
            Console.WriteLine($"Impact Velocity: {impactState.Velocity.Length():F1} m/s");
            Console.WriteLine($"Impact Energy: {initialEnergy:F1} Joules\n");
            
            int voxelsHit = 0;
            var hitVoxels = new System.Collections.Generic.HashSet<TacticalSim.Core.Physiology.PhysiologicalVoxel>();
            
            // Create a fresh dummy for the test so we don't accumulate damage across runs
            var testDummyPhysiology = AnatomicalDummyBuilder.BuildDummy();
            var testDummy = new TacticalEntity(new Vector3(0, 1.0f, 0), testDummyPhysiology);
            
            var allVoxels = new System.Collections.Generic.List<TacticalSim.Core.Physiology.PhysiologicalVoxel>();
            void CollectVoxels(TacticalSim.Core.Physiology.BodyPart part) {
                allVoxels.AddRange(part.Voxels);
                if (part.Type == TacticalSim.Core.Physiology.BodyPartType.Neck) {
                    Console.WriteLine($"Neck has {part.Voxels.Count} voxels");
                }
                foreach(var c in part.Children) CollectVoxels(c);
            }
            CollectVoxels(testDummy.Physiology.RootBodyPart);

            foreach(var voxel in allVoxels)
            {
                var localState = impactState;
                localState.Position -= testDummy.Position; // Convert to local space
                
                var cavitation = voxel.ProcessPenetration(ref localState, ammo.Ballistics);
                
                if (voxel.DepositedEnergy > 0)
                {
                    if (voxel.IsDestroyed && !hitVoxels.Contains(voxel))
                    {
                        hitVoxels.Add(voxel);
                        voxelsHit++;
                        Console.WriteLine($"[HIT] Voxel at {voxel.Center} (Tissue: {voxel.Organ})");
                        Console.WriteLine($"  -> Energy Deposited: {voxel.DepositedEnergy:F1} Joules");
                        
                        if (cavitation != null)
                        {
                            Console.WriteLine($"  -> Cavitation Radius: {cavitation.Value.Radius * 1000f:F1} mm");
                        }
                    }
                }
                
                impactState.Velocity = localState.Velocity;
            }
            
            Console.WriteLine($"\nTotal Voxels Penetrated: {voxelsHit}");
            Console.WriteLine($"Exit Velocity: {impactState.Velocity.Length():F1} m/s");
            Console.WriteLine("----------------------------------------\n");
        }
    }
}
