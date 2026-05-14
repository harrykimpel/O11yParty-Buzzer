using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using O11yPartyBuzzer.Services;

namespace O11yPartyBuzzer.Tests;

public class ChaosConfigurationValidatorTests
{
    [Fact]
    public void ValidateOrThrow_Throws_WhenChaosEnabledInProduction()
    {
        var options = new ChaosOptions { Enabled = true };
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var action = () => ChaosConfigurationValidator.ValidateOrThrow(options, environment);

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Equal("Chaos mode cannot be enabled in Production.", exception.Message);
    }

    [Fact]
    public void ValidateOrThrow_DoesNotThrow_WhenChaosDisabledInProduction()
    {
        var options = new ChaosOptions { Enabled = false };
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() => ChaosConfigurationValidator.ValidateOrThrow(options, environment));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateOrThrow_DoesNotThrow_WhenChaosEnabledOutsideProduction()
    {
        var options = new ChaosOptions { Enabled = true };
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };

        var exception = Record.Exception(() => ChaosConfigurationValidator.ValidateOrThrow(options, environment));

        Assert.Null(exception);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = nameof(O11yPartyBuzzer);

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
