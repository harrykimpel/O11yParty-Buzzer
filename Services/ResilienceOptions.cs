namespace O11yPartyBuzzer.Services;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int BlazorDisconnectedCircuitMaxRetained { get; set; } = 100;

    public int BlazorDisconnectedCircuitRetentionMinutes { get; set; } = 3;

    public int BlazorJsInteropCallTimeoutSeconds { get; set; } = 30;

    public int BlazorHubClientTimeoutSeconds { get; set; } = 30;

    public int BlazorHubHandshakeTimeoutSeconds { get; set; } = 15;

    public int BlazorHubKeepAliveIntervalSeconds { get; set; } = 15;
}
