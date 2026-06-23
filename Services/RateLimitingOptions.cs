namespace O11yPartyBuzzer.Services;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public LeadCaptureRateLimitOptions LeadCapture { get; set; } = new();
    public BuzzRateLimitOptions Buzz { get; set; } = new();
}

public sealed class LeadCaptureRateLimitOptions
{
    /// <summary>Maximum lead-capture requests per IP within the window.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Sliding-window duration in minutes.</summary>
    public int WindowMinutes { get; set; } = 10;

    /// <summary>Number of segments the sliding window is divided into.</summary>
    public int SegmentsPerWindow { get; set; } = 5;
}

public sealed class BuzzRateLimitOptions
{
    /// <summary>Maximum buzz requests per IP within the sliding window.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Sliding-window duration in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Number of segments the sliding window is divided into.</summary>
    public int SegmentsPerWindow { get; set; } = 6;
}
