using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class ChaosEngineeringPolicy(
    IWebHostEnvironment environment,
    IOptions<ChaosEngineeringOptions> chaosOptions)
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ChaosEngineeringOptions _chaosOptions = chaosOptions.Value;

    public ChaosRequestEvaluation Evaluate(string? chaosMode)
    {
        var normalizedMode = NormalizeMode(chaosMode);
        if (string.IsNullOrWhiteSpace(normalizedMode))
        {
            return new ChaosRequestEvaluation(string.Empty, _environment.EnvironmentName, false, false, false);
        }

        if (_environment.IsProduction())
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, false, true, true);
        }

        if (!_chaosOptions.Enabled)
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, false, false, false);
        }

        if (!_chaosOptions.IsEnvironmentAllowed(_environment.EnvironmentName))
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, false, false, false);
        }

        return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, true, false, false);
    }

    private static string NormalizeMode(string? chaosMode)
    {
        return (chaosMode ?? string.Empty).Trim().ToLowerInvariant();
    }
}

public readonly record struct ChaosRequestEvaluation(
    string Mode,
    string EnvironmentName,
    bool ShouldApplyChaos,
    bool IsBlockedInProduction,
    bool IsDisabledByConfiguration)
{
    public bool IsRequested => !string.IsNullOrWhiteSpace(Mode);
}
