using ProxyState.Simulation;
using Xunit;

namespace ProxyState.Tests;

public sealed class ApplicationOptionsTests
{
    [Fact]
    public void EmptyArgumentsKeepTheOrdinaryPopulation()
    {
        Assert.True(ApplicationOptions.TryParse([], out var options, out var error));
        Assert.Equal(SimulationDefaults.AgentCount, options.AgentCount);
        Assert.False(options.DebugMode);
        Assert.Null(error);
    }

    [Fact]
    public void PopulationOverrideAndDebugCanAppearInEitherOrder()
    {
        Assert.True(ApplicationOptions.TryParse(["-debug", "--agents", "100000"], out var options, out var error));
        Assert.Equal(100_000, options.AgentCount);
        Assert.True(options.DebugMode);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nonnumeric")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("excessive")]
    public void InvalidPopulationIsRejectedWithHelpfulError(string kind)
    {
        var arguments = kind switch
        {
            "missing" => new[] { "--agents" },
            "nonnumeric" => new[] { "--agents", "many" },
            "zero" => new[] { "--agents", "0" },
            "negative" => new[] { "--agents", "-1" },
            _ => new[] { "--agents", "100001" }
        };

        Assert.False(ApplicationOptions.TryParse(arguments, out _, out var error));
        Assert.Contains("--agents", error);
    }

    [Fact]
    public void UnknownAndDuplicateOptionsAreRejected()
    {
        Assert.False(ApplicationOptions.TryParse(["--unknown"], out _, out var unknownError));
        Assert.Contains("Usage", unknownError);
        Assert.False(ApplicationOptions.TryParse(["--agents", "10", "--agents", "20"], out _, out var duplicateError));
        Assert.Contains("only be specified once", duplicateError);
    }
}
