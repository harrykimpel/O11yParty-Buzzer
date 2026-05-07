namespace O11yPartyBuzzer.Services;

public sealed class NewRelicOptions
{
    public const string SectionName = "NewRelic";

    public string IngestApiKey { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Region { get; set; } = "US";

    public string EventType { get; set; } = "TeamBuzzed";

    public string LeadCaptureEventType { get; set; } = "LeadCaptureSubmitted";

    public int RequestTimeoutSeconds { get; set; } = 3;

    public int SlowRequestWarningThresholdMs { get; set; } = 1000;

    public int MaxConnectionsPerServer { get; set; } = 32;

    public int PooledConnectionLifetimeSeconds { get; set; } = 300;
}
