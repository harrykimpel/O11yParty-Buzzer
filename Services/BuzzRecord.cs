namespace O11yPartyBuzzer.Services;

/// <summary>
/// Represents a buzz event queued for delivery to the game hub.
/// The timestamp is captured at API receipt time to preserve ordering.
/// </summary>
public sealed record BuzzRecord(string TeamName, long BuzzedAtUtcMs);
