namespace O11yPartyBuzzer.Services;

public static class SyntheticFailureModePolicy
{
    public static string Normalize(string? mode)
    {
        return (mode ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool IsEnabled(string normalizedMode, bool isProductionEnvironment)
    {
        return !string.IsNullOrEmpty(normalizedMode) && !isProductionEnvironment;
    }
}
