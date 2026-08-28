using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace ProxyState.Simulation;

public sealed class FatigueStressSystem : QuerySystem<AgentAttributes>
{
    private readonly float _increasePerTick;
    private readonly int _fatigueIndex;
    private readonly int _stressIndex;

    public FatigueStressSystem(
        AgentAttributeSchema schema,
        float increasePerTick = SimulationDefaults.FatigueStressIncreasePerTick)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (increasePerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(increasePerTick), "The update amount must be positive.");
        }

        _increasePerTick = increasePerTick;
        _fatigueIndex = schema.GetIndex("fatigue");
        _stressIndex = schema.GetIndex("stress");
        Filter.AllTags(Tags.Get<Tier1LodTag>());
    }

    protected override void OnUpdate()
    {
        Query.ForEachEntity((ref AgentAttributes attributes, Entity _) =>
        {
            attributes.Values[_fatigueIndex] = IncreaseOrReset(attributes.Values[_fatigueIndex]);
            attributes.Values[_stressIndex] = IncreaseOrReset(attributes.Values[_stressIndex]);
        });
    }

    private float IncreaseOrReset(float value)
    {
        var updated = value + _increasePerTick;
        return updated >= SimulationDefaults.MaximumFatigueStress ? 0f : updated;
    }
}
