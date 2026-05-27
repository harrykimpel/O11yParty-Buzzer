namespace O11yPartyBuzzer.Services;

public static class ChaosModePolicy
{
    public static bool HasChaosParameter(string? failureMode)
    {
        return !string.IsNullOrWhiteSpace(failureMode);
    }

    public static bool IsChaosModeEnabled(string? enableChaosMode, string? environmentName, string? aspNetCoreEnvironmentName)
    {
        var isChaosEnabled = string.Equals(enableChaosMode, "true", StringComparison.OrdinalIgnoreCase);
        if (!isChaosEnabled)
        {
            return false;
        }

        var effectiveEnvironment = !string.IsNullOrWhiteSpace(environmentName)
            ? environmentName
            : aspNetCoreEnvironmentName;

        return !string.Equals(effectiveEnvironment, "production", StringComparison.OrdinalIgnoreCase);
    }
}
