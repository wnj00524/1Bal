using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Ballistics;
using System.Numerics;

class Program
{
    static void Main()
    {
        var services = new ServiceCollection();
        services.AddTacticalSimCore();
        var sp = services.BuildServiceProvider();
        var env = sp.GetRequiredService<IEnvironmentModel>();

        var profiles = new List<AmmunitionProfile>
        {
            new AmmunitionProfile { Name = "5.56x45mm NATO (Arm Shot)", MuzzleVelocity = 900f, Ballistics = new BallisticProfile { Mass = 0.004f, CrossSectionalArea = 0.000024f, DragModel = new StandardDragCurve(0.3f) } },
            new AmmunitionProfile { Name = ".308 Winchester (Leg Shot)", MuzzleVelocity = 800f, Ballistics = new BallisticProfile { Mass = 0.0097f, CrossSectionalArea = 0.000048f, DragModel = new StandardDragCurve(0.4f) } }
        };

        foreach (var ammo in profiles)
        {
            Console.WriteLine($"\n================== {ammo.Name} ==================");
            var dummyPhys = AnatomicalDummyBuilder.BuildDummy();
            var allVoxels = GetAllVoxels(dummyPhys.RootBodyPart);

            var voxelGrid = new PhysiologicalVoxel[100, 220, 100];
            foreach (var v in allVoxels)
            {
                int vx = (int)MathF.Round(v.Center.X * 100f) + 50;
                int vy = (int)MathF.Round(v.Center.Y * 100f) + 100;
                int vz = (int)MathF.Round(v.Center.Z * 100f) + 50;
                if (vx >= 0 && vx < 100 && vy >= 0 && vy < 220 && vz >= 0 && vz < 100) voxelGrid[vx, vy, vz] = v;
            }

            Vector3 impactStatePos = ammo.Name.Contains("Arm") ? new Vector3(0.3f, 0.25f, -1.0f) : new Vector3(0.1f, -0.4f, -1.0f);
            Vector3 impactDir = new Vector3(0, 0, 1);
            
            var impactState = new ProjectileState { Position = impactStatePos, Velocity = impactDir * ammo.MuzzleVelocity, Time = 0f };
            var cavEvents = new List<(float Time, TacticalSim.Core.Physiology.CavitationEvent Cav)>();
            
            while (impactState.Position.Z < 0.2f && impactState.Velocity.LengthSquared() > 0.01f)
            {
                var localPos = impactState.Position;
                float simTimeStep = 0.00001f;
                impactState = TacticalSim.Core.Ballistics.BallisticSolver.StepRK4(impactState, ammo.Ballistics, env, simTimeStep);
                
                int bx = (int)MathF.Round(localPos.X * 100f) + 50;
                int by = (int)MathF.Round(localPos.Y * 100f) + 100;
                int bz = (int)MathF.Round(localPos.Z * 100f) + 50;
                
                if (bx >= 0 && bx < 100 && by >= 0 && by < 220 && bz >= 0 && bz < 100)
                {
                    var voxel = voxelGrid[bx, by, bz];
                    if (voxel != null && voxel.Contains(localPos))
                    {
                        var localState = impactState;
                        localState.Position = localPos;
                        float distanceThisStep = localState.Velocity.Length() * simTimeStep;
                        var cav = voxel.ProcessPenetrationStep(ref localState, ammo.Ballistics, distanceThisStep);
                        impactState.Velocity = localState.Velocity;

                        if (cav.HasValue)
                        {
                            if (cavEvents.Count == 0 || (localPos - cavEvents[cavEvents.Count - 1].Cav.Origin).Length() > 0.01f) cavEvents.Add((impactState.Time, cav.Value));
                            else {
                                var last = cavEvents[cavEvents.Count - 1];
                                var modifiedCav = last.Cav;
                                modifiedCav.Energy += cav.Value.Energy;
                                modifiedCav.Radius = MathF.Max(modifiedCav.Radius, cav.Value.Radius);
                                cavEvents[cavEvents.Count - 1] = (last.Time, modifiedCav);
                            }
                        }
                    }
                }
            }

            foreach (var cavEvent in cavEvents)
            {
                var cav = cavEvent.Cav;
                int radCells = (int)MathF.Ceiling(cav.Radius * 100f);
                int cx = (int)MathF.Round(cav.Origin.X * 100f) + 50;
                int cy = (int)MathF.Round(cav.Origin.Y * 100f) + 100;
                int cz = (int)MathF.Round(cav.Origin.Z * 100f) + 50;

                for (int x = cx - radCells; x <= cx + radCells; x++)
                    for (int y = cy - radCells; y <= cy + radCells; y++)
                        for (int z = cz - radCells; z <= cz + radCells; z++)
                            if (x >= 0 && x < 100 && y >= 0 && y < 220 && z >= 0 && z < 100)
                            {
                                var neighbor = voxelGrid[x, y, z];
                                if (neighbor != null)
                                {
                                    float dist = (neighbor.Center - cav.Origin).Length();
                                    if (dist > 0 && dist <= cav.Radius) neighbor.ApplyKineticEnergy(cav.Energy * (1f - (dist / cav.Radius)), cav.Origin, 0f);
                                }
                            }
            }
            
            dummyPhys.TickPhysiology(0.1f);
            var report = MedicalAssessor.AssessTrauma(dummyPhys);
            Console.WriteLine(report.AssessmentText);
        }
    }

    private static List<PhysiologicalVoxel> GetAllVoxels(BodyPart root)
    {
        var list = new List<PhysiologicalVoxel>();
        list.AddRange(root.Voxels);
        foreach (var child in root.Children) list.AddRange(GetAllVoxels(child));
        return list;
    }
}
