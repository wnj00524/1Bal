using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Scenarios;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Randomness;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.Usage);
                return 0;
            }

            var services = new ServiceCollection();
            services.AddSingleton<IRootSeedProvider>(new FixedRootSeedProvider(options.Seed));
            services.AddTacticalSimCore();
            using ServiceProvider provider = services.BuildServiceProvider();
            IReferenceImpactScenarioCatalog catalog = provider.GetRequiredService<IReferenceImpactScenarioCatalog>();

            if (options.ListScenarios)
            {
                IReadOnlyList<ReferenceImpactScenarioInput> scenarios = catalog.List();
                Console.WriteLine(options.Format == OutputFormat.Json
                    ? ReferenceImpactFormatter.ScenarioListToJson(scenarios)
                    : ReferenceImpactFormatter.ScenarioListToText(scenarios));
                return 0;
            }

            IReferenceImpactRunner runner = provider.GetRequiredService<IReferenceImpactRunner>();
            if (options.Compare)
            {
                ReferenceImpactComparisonResult comparison = runner.Compare(
                    new ReferenceImpactComparisonRequest(
                        options.ScenarioId,
                        options.BaselineModel,
                        options.Model,
                        options.Seed));
                Console.WriteLine(options.Format == OutputFormat.Json
                    ? ReferenceImpactFormatter.ToJson(comparison)
                    : ReferenceImpactFormatter.ToText(comparison));
            }
            else
            {
                ReferenceImpactResult result = runner.Run(
                    new ReferenceImpactRunRequest(
                        options.ScenarioId,
                        options.Model,
                        options.Seed));
                Console.WriteLine(options.Format == OutputFormat.Json
                    ? ReferenceImpactFormatter.ToJson(result)
                    : ReferenceImpactFormatter.ToText(result));
            }

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or KeyNotFoundException
                                          or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }
    }

    private enum OutputFormat
    {
        Text,
        Json
    }

    private sealed record CliOptions(
        string ScenarioId,
        DamageModelVersion Model,
        DamageModelVersion BaselineModel,
        ulong Seed,
        OutputFormat Format,
        bool ListScenarios,
        bool Compare,
        bool ShowHelp)
    {
        internal const string Usage =
            "Usage: TacticalSim.ConsoleApp [--list] [--scenario <id>] "
            + "[--model <legacy-v1|m5-foundations-v2>] [--seed <uint64>] "
            + "[--format <text|json>] [--compare [baseline[,candidate]]] [--help]";

        internal static CliOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            string scenarioId = "rifle-arm";
            DamageModelVersion model = DamageModelVersion.FoundationsV2;
            DamageModelVersion baseline = DamageModelVersion.LegacyV1;
            ulong seed = 0UL;
            OutputFormat format = OutputFormat.Text;
            bool list = false;
            bool compare = false;
            bool help = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument)
                {
                    case "--scenario":
                        scenarioId = ReadValue(args, ref index, argument);
                        break;
                    case "--model":
                        model = DamageModelVersionExtensions.ParseIdentifier(
                            ReadValue(args, ref index, argument));
                        break;
                    case "--seed":
                        string seedValue = ReadValue(args, ref index, argument);
                        if (!ulong.TryParse(seedValue, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException($"Invalid unsigned 64-bit seed '{seedValue}'.", argument);
                        break;
                    case "--format":
                        string formatValue = ReadValue(args, ref index, argument);
                        format = formatValue.ToLowerInvariant() switch
                        {
                            "text" => OutputFormat.Text,
                            "json" => OutputFormat.Json,
                            _ => throw new ArgumentException($"Unknown output format '{formatValue}'.", argument)
                        };
                        break;
                    case "--list":
                        list = true;
                        break;
                    case "--compare":
                        compare = true;
                        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            string comparisonValue = args[++index];
                            string[] models = comparisonValue.Split(',', StringSplitOptions.TrimEntries);
                            if (models.Length is < 1 or > 2 || models.Any(string.IsNullOrWhiteSpace))
                                throw new ArgumentException($"Invalid model comparison '{comparisonValue}'.", argument);
                            baseline = DamageModelVersionExtensions.ParseIdentifier(models[0]);
                            if (models.Length == 2)
                                model = DamageModelVersionExtensions.ParseIdentifier(models[1]);
                        }
                        break;
                    case "--help" or "-h":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{argument}'.", nameof(args));
                }
            }

            return new CliOptions(scenarioId, model, baseline, seed, format, list, compare, help);
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option '{option}' requires a value.", option);

            return args[++index];
        }
    }
}
