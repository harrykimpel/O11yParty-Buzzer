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

    public static string SanitizeForLog(string value)
    {
        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
