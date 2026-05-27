using O11yPartyBuzzer.Services;

namespace O11yPartyBuzzer.Tests;

public class ChaosModePolicyTests
{
    [Fact]
    public void IsChaosModeEnabled_ReturnsFalse_WhenFeatureFlagIsMissing()
    {
        var enabled = ChaosModePolicy.IsChaosModeEnabled(null, "development", "Development");

        Assert.False(enabled);
    }

    [Fact]
    public void IsChaosModeEnabled_ReturnsFalse_InProductionEnvironment()
    {
        var enabled = ChaosModePolicy.IsChaosModeEnabled("true", "production", "Production");

        Assert.False(enabled);
    }

    [Fact]
    public void IsChaosModeEnabled_ReturnsFalse_WhenAspNetCoreEnvironmentIsProduction()
    {
        var enabled = ChaosModePolicy.IsChaosModeEnabled("true", null, "Production");

        Assert.False(enabled);
    }

    [Fact]
    public void IsChaosModeEnabled_ReturnsTrue_WhenFeatureFlagEnabledOutsideProduction()
    {
        var enabled = ChaosModePolicy.IsChaosModeEnabled("true", "staging", "Staging");

        Assert.True(enabled);
    }

    [Fact]
    public void HasChaosParameter_ReturnsFalse_WhenMissing()
    {
        Assert.False(ChaosModePolicy.HasChaosParameter(" "));
    }

    [Fact]
    public void HasChaosParameter_ReturnsTrue_WhenPresent()
    {
        Assert.True(ChaosModePolicy.HasChaosParameter("exception"));
    }
}
