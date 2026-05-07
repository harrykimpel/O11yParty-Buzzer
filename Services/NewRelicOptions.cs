namespace O11yPartyBuzzer.Services;

public sealed class NewRelicOptions
{
    public const string SectionName = "NewRelic";
    public const int DefaultRequestTimeoutSeconds = 3;
    public const int DefaultSlowRequestWarningThresholdMs = 1000;
    public const int DefaultMaxConnectionsPerServer = 32;
    public const int DefaultPooledConnectionLifetimeSeconds = 300;

    public string IngestApiKey { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Region { get; set; } = "US";

    public string EventType { get; set; } = "TeamBuzzed";

    public string LeadCaptureEventType { get; set; } = "LeadCaptureSubmitted";

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    public int SlowRequestWarningThresholdMs { get; set; } = DefaultSlowRequestWarningThresholdMs;

    public int MaxConnectionsPerServer { get; set; } = DefaultMaxConnectionsPerServer;

    public int PooledConnectionLifetimeSeconds { get; set; } = DefaultPooledConnectionLifetimeSeconds;
}
