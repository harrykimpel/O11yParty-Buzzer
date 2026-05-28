namespace O11yPartyBuzzer.Services;

/// <summary>
/// Guards chaos-mode execution by enforcing environment restrictions,
/// optional token authentication, and request rate limiting.
/// </summary>
public interface IChaosGuardService
{
    /// <summary>
    /// Determines whether a chaos-mode request may proceed.
    /// </summary>
    /// <param name="chaosMode">The requested chaos mode (e.g. "exception", "latency").</param>
    /// <param name="chaosToken">Optional token supplied by the caller via ?chaosToken=.</param>
    /// <param name="reason">
    /// When the method returns <c>false</c>, contains a human-readable explanation of why the
    /// request was blocked. Empty when the method returns <c>true</c>.
    /// </param>
    /// <returns><c>true</c> if chaos may execute; <c>false</c> if it should be suppressed.</returns>
    bool TryAllow(string chaosMode, string? chaosToken, out string reason);
}
