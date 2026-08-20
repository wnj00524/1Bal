using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Entities;

/// <summary>Representative factory ammunition for repeatable scenario comparisons.</summary>
public static class AmmunitionCatalog
{
    public static AmmunitionProfile TwentyTwoLongRifle => Create(
        ".22 LR", 330f, 0.0026f, 0.000026f, 0.12f);

    public static AmmunitionProfile ThreeEightyAcp => Create(
        ".380 ACP", 290f, 0.0062f, 0.0000456f, 0.14f);

    private static AmmunitionProfile Create(
        string name, float velocity, float mass, float area, float dragCoefficient) => new()
    {
        Name = name,
        MuzzleVelocity = velocity,
        Ballistics = new BallisticProfile
        {
            Mass = mass,
            CrossSectionalArea = area,
            DragModel = new StandardDragCurve(dragCoefficient)
        }
    };
}
