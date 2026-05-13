namespace O11yPartyBuzzer.Services;

/// <summary>
/// Configuration options for the chaos engineering feature.
/// Bind from the "Chaos" section in appsettings.json.
/// </summary>
public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>
    /// Master switch. When false (default), all chaos modes are silently ignored regardless of
    /// the ?chaos query parameter. Set to true only in non-production environments.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Environments in which chaos is permitted. Defaults to Development only.
    /// Set to an empty array to allow in any environment (not recommended for production).
    /// </summary>
    public string[] AllowedEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// Optional secret token. When non-empty, the ?chaosToken=&lt;value&gt; query parameter
    /// must match this value exactly for chaos to be permitted.
    /// Leave empty to skip token validation.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Maximum number of chaos requests allowed within <see cref="RateLimitWindowSeconds"/>.</summary>
    public int RateLimitMaxRequests { get; set; } = 10;

    /// <summary>Sliding-window duration (seconds) used by the rate limiter.</summary>
    public int RateLimitWindowSeconds { get; set; } = 60;
}
