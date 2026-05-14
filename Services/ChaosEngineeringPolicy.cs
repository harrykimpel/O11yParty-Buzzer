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
            return new ChaosRequestEvaluation(string.Empty, _environment.EnvironmentName, ChaosRequestBlockReason.None);
        }

        if (_environment.IsProduction())
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, ChaosRequestBlockReason.Production);
        }

        if (!_chaosOptions.Enabled)
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, ChaosRequestBlockReason.Configuration);
        }

        if (!_chaosOptions.IsEnvironmentAllowed(_environment.EnvironmentName))
        {
            return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, ChaosRequestBlockReason.Environment);
        }

        return new ChaosRequestEvaluation(normalizedMode, _environment.EnvironmentName, ChaosRequestBlockReason.None);
    }

    private static string NormalizeMode(string? chaosMode)
    {
        return (chaosMode ?? string.Empty).Trim().ToLowerInvariant();
    }
}

public readonly record struct ChaosRequestEvaluation(
    string Mode,
    string EnvironmentName,
    ChaosRequestBlockReason BlockReason)
{
    public bool IsRequested => !string.IsNullOrWhiteSpace(Mode);

    public bool ShouldApplyChaos => IsRequested && BlockReason is ChaosRequestBlockReason.None;

    public bool IsBlockedInProduction => BlockReason is ChaosRequestBlockReason.Production;

    public bool IsDisabledByConfiguration => BlockReason is ChaosRequestBlockReason.Configuration;

    public bool IsDisallowedEnvironment => BlockReason is ChaosRequestBlockReason.Environment;
}

public enum ChaosRequestBlockReason
{
    None,
    Production,
    Configuration,
    Environment
}
