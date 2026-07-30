namespace O11yPartyBuzzer.Services;

public sealed class BuzzHubOptions
{
    public const string SectionName = "BuzzHub";

    public string Url { get; set; } = "";

    public string SharedSecret { get; set; } = "";

    /// <summary>
    /// Maximum number of additional retry attempts after the first failure inside a single
    /// <c>SendBuzzAsync</c> call.  Retries use exponential back-off (200 ms × 2^attempt).
    /// Set to 0 to disable retries (one attempt only).  Default: 2.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Number of consecutive <c>SendBuzzAsync</c> failures required to open the circuit
    /// breaker.  While open, every buzz is rejected immediately with a 502 rather than
    /// waiting for a network timeout.  Default: 5.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// How long (seconds) to keep the circuit breaker open before allowing a single probe
    /// attempt (half-open state).  Default: 30.
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Wall-clock timeout (seconds) for a single <c>SendBuzzAsync</c> call, inclusive of
    /// all retry attempts.  0 means no enforced timeout (not recommended for production).
    /// Default: 10.
    /// </summary>
    public int SendTimeoutSeconds { get; set; } = 10;
}
