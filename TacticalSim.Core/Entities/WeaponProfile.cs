namespace TacticalSim.Core.Entities
{
    public record WeaponProfile
    {
        public string Name { get; init; } = string.Empty;
        public AmmunitionProfile LoadedAmmunition { get; set; } = null!;
        public float BaseTUCostToFire { get; init; } = 15f;
    }
}
