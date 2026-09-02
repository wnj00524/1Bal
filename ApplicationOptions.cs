using ProxyState.Simulation;

namespace ProxyState;

/// <summary>Validated interactive-launch options, kept pure so startup failures never open Raylib.</summary>
public sealed record ApplicationOptions(int AgentCount, bool DebugMode)
{
    public const int MaximumAgentCount = 100_000;

    public static bool TryParse(IReadOnlyList<string> arguments, out ApplicationOptions options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var agentCount = SimulationDefaults.AgentCount;
        var debugMode = false;
        var agentsSeen = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "-debug", StringComparison.OrdinalIgnoreCase))
            {
                debugMode = true;
                continue;
            }

            if (!string.Equals(argument, "--agents", StringComparison.OrdinalIgnoreCase))
            {
                options = new ApplicationOptions(agentCount, debugMode);
                error = $"Unknown option '{argument}'. Usage: ProxyState [--agents <1-{MaximumAgentCount}>] [-debug]";
                return false;
            }

            if (agentsSeen)
            {
                options = new ApplicationOptions(agentCount, debugMode);
                error = "The --agents option may only be specified once.";
                return false;
            }
            agentsSeen = true;
            if (++index >= arguments.Count)
            {
                options = new ApplicationOptions(agentCount, debugMode);
                error = "The --agents option requires a population value.";
                return false;
            }

            var value = arguments[index];
            if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out agentCount))
            {
                options = new ApplicationOptions(SimulationDefaults.AgentCount, debugMode);
                error = $"The --agents value '{value}' must be a whole number.";
                return false;
            }
            if (agentCount is < 1 or > MaximumAgentCount)
            {
                options = new ApplicationOptions(SimulationDefaults.AgentCount, debugMode);
                error = $"The --agents value must be between 1 and {MaximumAgentCount}.";
                return false;
            }
        }

        options = new ApplicationOptions(agentCount, debugMode);
        error = null;
        return true;
    }
}
