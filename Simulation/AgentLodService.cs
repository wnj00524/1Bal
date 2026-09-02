using Friflo.Engine.ECS;

namespace ProxyState.Simulation;

// This is the sole mutation boundary for tier and detailed-simulation tags.
// Later classification code must request transitions here rather than editing
// entity tags or AgentLodState directly.
public sealed class AgentLodService
{
    private readonly AgentLodSettings _settings;

    public AgentLodService(AgentLodSettings settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public void InitializeTierOne(Entity entity, AgentInterestReason reasons = AgentInterestReason.None)
    {
        if (!entity.HasComponent<AgentLodState>())
        {
            entity.AddComponent(new AgentLodState
            {
                DesiredTier = AgentLodTier.Tier1,
                InterestReasons = reasons,
                ScheduledDemotionMinute = -1,
                CoarseProfileId = 0,
                LastCoarseSimulatedMinute = -1
            });
        }

        SynchronizeTags(entity, AgentLodTier.Tier1);
    }

    // Desired Tier 3 is clamped to detailed Tier 2 until the production switch
    // is enabled in Milestone 19. Keeping desired and materialized tiers apart
    // makes that rollout explicit without teaching callers about tag mechanics.
    public void SetDesiredTier(Entity entity, AgentLodTier desiredTier)
    {
        if (!entity.HasComponent<AgentLodState>())
            throw new InvalidOperationException("Agent LOD state must be initialized before changing tier.");

        ref var state = ref entity.GetComponent<AgentLodState>();
        state.DesiredTier = desiredTier;
        var currentTier = desiredTier == AgentLodTier.Tier3 && !_settings.Tier3Enabled
            ? AgentLodTier.Tier2
            : desiredTier;
        SynchronizeTags(entity, currentTier);
    }

    public static bool HasExactlyOneTierTag(Entity entity)
    {
        var count = entity.Tags.Has<Tier1LodTag>() ? 1 : 0;
        count += entity.Tags.Has<Tier2LodTag>() ? 1 : 0;
        count += entity.Tags.Has<Tier3LodTag>() ? 1 : 0;
        return count == 1;
    }

    public static bool RequiresDetailedSimulation(AgentLodTier tier) =>
        tier is AgentLodTier.Tier1 or AgentLodTier.Tier2;

    private static void SynchronizeTags(Entity entity, AgentLodTier tier)
    {
        entity.RemoveTag<Tier1LodTag>();
        entity.RemoveTag<Tier2LodTag>();
        entity.RemoveTag<Tier3LodTag>();
        entity.RemoveTag<DetailedSimulationTag>();

        switch (tier)
        {
            case AgentLodTier.Tier1: entity.AddTag<Tier1LodTag>(); break;
            case AgentLodTier.Tier2: entity.AddTag<Tier2LodTag>(); break;
            case AgentLodTier.Tier3: entity.AddTag<Tier3LodTag>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown agent LOD tier.");
        }

        if (RequiresDetailedSimulation(tier)) entity.AddTag<DetailedSimulationTag>();
    }
}
