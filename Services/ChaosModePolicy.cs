using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class ChaosModePolicy(
    IOptions<ChaosEngineeringOptions> chaosOptions,
    IHostEnvironment hostEnvironment,
    ILogger<ChaosModePolicy> logger)
{
    public bool TryGetAllowedMode(string? requestedMode, out string normalizedMode)
    {
        normalizedMode = (requestedMode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedMode))
        {
            return false;
        }

        if (!chaosOptions.Value.Enabled)
        {
            logger.LogWarning("Chaos mode requested while chaos feature is disabled");
            return false;
        }

        if (hostEnvironment.IsProduction() && !chaosOptions.Value.AllowInProduction)
        {
            logger.LogWarning("Chaos mode requested in production and was blocked");
            return false;
        }

        return true;
    }

    public void ValidateStartupConfiguration()
    {
        if (!hostEnvironment.IsProduction())
        {
            return;
        }

        if (chaosOptions.Value.AllowInProduction)
        {
            throw new InvalidOperationException($"{ChaosEngineeringOptions.SectionName}:AllowInProduction cannot be true in production.");
        }

        if (chaosOptions.Value.Enabled)
        {
            logger.LogWarning(
                "{Section}:Enabled is true in production. Chaos query parameters will still be ignored.",
                ChaosEngineeringOptions.SectionName);
        }
    }
}
