namespace O11yPartyBuzzer.Services;

public sealed class NewRelicOptions
{
    public const string SectionName = "NewRelic";
    public const int DefaultPublishTimeoutSeconds = 10;
    public const int DefaultSignalRLongPollingTimeoutSeconds = 25;

    public string IngestApiKey { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Region { get; set; } = "US";

    public string EventType { get; set; } = "TeamBuzzed";

    public string LeadCaptureEventType { get; set; } = "LeadCaptureSubmitted";

    public int PublishTimeoutSeconds { get; set; } = DefaultPublishTimeoutSeconds;

    public int SignalRLongPollingTimeoutSeconds { get; set; } = DefaultSignalRLongPollingTimeoutSeconds;
}
