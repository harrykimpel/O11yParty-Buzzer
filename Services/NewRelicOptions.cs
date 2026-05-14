namespace O11yPartyBuzzer.Services;

public sealed class NewRelicOptions
{
    public const string SectionName = "NewRelic";

    public string IngestApiKey { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Region { get; set; } = "US";

    public string EventType { get; set; } = "TeamBuzzed";

    public string LeadCaptureEventType { get; set; } = "LeadCaptureSubmitted";

    public int EventPublishTimeoutSeconds { get; set; } = 15;

    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    public int CircuitBreakerBreakDurationSeconds { get; set; } = 60;
}
