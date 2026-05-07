using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using O11yPartyBuzzer.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace O11yPartyBuzzer.Tests;

public class ChaosModePolicyTests
{
    [Fact]
    public void TryGetAllowedMode_ReturnsFalse_WhenFeatureFlagDisabled()
    {
        var policy = CreatePolicy(
            new ChaosEngineeringOptions { Enabled = false },
            environmentName: "Development");

        var allowed = policy.TryGetAllowedMode("exception", out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryGetAllowedMode_ReturnsTrue_WhenEnabledInNonProduction()
    {
        var policy = CreatePolicy(
            new ChaosEngineeringOptions { Enabled = true },
            environmentName: "Development");

        var allowed = policy.TryGetAllowedMode("  ExCePtIoN ", out var mode);

        Assert.True(allowed);
        Assert.Equal("exception", mode);
    }

    [Fact]
    public void TryGetAllowedMode_ReturnsFalse_InProduction()
    {
        var policy = CreatePolicy(
            new ChaosEngineeringOptions { Enabled = true, AllowInProduction = false },
            environmentName: "Production");

        var allowed = policy.TryGetAllowedMode("exception", out _);

        Assert.False(allowed);
    }

    [Fact]
    public void ValidateStartupConfiguration_Throws_WhenAllowInProductionEnabled()
    {
        var policy = CreatePolicy(
            new ChaosEngineeringOptions { Enabled = true, AllowInProduction = true },
            environmentName: "Production");

        Assert.Throws<InvalidOperationException>(() => policy.ValidateStartupConfiguration());
    }

    private static ChaosModePolicy CreatePolicy(ChaosEngineeringOptions options, string environmentName)
    {
        return new ChaosModePolicy(
            Options.Create(options),
            new TestHostEnvironment(environmentName),
            NullLogger<ChaosModePolicy>.Instance);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "O11yPartyBuzzer.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
