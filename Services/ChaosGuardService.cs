using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace O11yPartyBuzzer.Services;

/// <summary>
/// Default implementation of <see cref="IChaosGuardService"/>.
/// Registered as a singleton so that rate-limit state is shared across all requests.
/// </summary>
public sealed class ChaosGuardService : IChaosGuardService
{
    private readonly ChaosOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ChaosGuardService> _logger;

    // Sliding-window rate limiter state.
    private readonly Queue<DateTimeOffset> _requestTimestamps = new();
    private readonly object _rateLimitLock = new();

    public ChaosGuardService(
        IOptions<ChaosOptions> options,
        IWebHostEnvironment env,
        ILogger<ChaosGuardService> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool TryAllow(string chaosMode, string? chaosToken, out string reason)
    {
        var safeMode = SanitizeForLog(chaosMode);

        // 1. Master enabled check.
        if (!_options.Enabled)
        {
            reason = "Chaos features are disabled via configuration.";
            _logger.LogWarning(
                "[chaos] [blocked] Chaos request suppressed — chaos disabled in configuration. " +
                "chaos.mode={ChaosMode} chaos.blocked=true chaos.reason=disabled",
                safeMode);
            return false;
        }

        // 2. Environment check.
        if (_options.AllowedEnvironments.Length > 0 &&
            !_options.AllowedEnvironments.Any(e =>
                e.Equals(_env.EnvironmentName, StringComparison.OrdinalIgnoreCase)))
        {
            reason = $"Chaos features are not permitted in the '{_env.EnvironmentName}' environment.";
            _logger.LogWarning(
                "[chaos] [blocked] Chaos request suppressed — disallowed environment. " +
                "chaos.mode={ChaosMode} chaos.environment={Environment} chaos.blocked=true chaos.reason=environment",
                safeMode, _env.EnvironmentName);
            return false;
        }

        // 3. Token authentication check (constant-time comparison to prevent timing attacks).
        if (!string.IsNullOrEmpty(_options.Token))
        {
            var configBytes = Encoding.UTF8.GetBytes(_options.Token);
            var providedBytes = Encoding.UTF8.GetBytes(chaosToken ?? string.Empty);
            if (!CryptographicOperations.FixedTimeEquals(configBytes, providedBytes))
            {
                reason = "Invalid or missing chaos authorization token.";
                _logger.LogWarning(
                    "[chaos] [blocked] Chaos request suppressed — invalid or missing token. " +
                    "chaos.mode={ChaosMode} chaos.blocked=true chaos.reason=unauthorized",
                    safeMode);
                return false;
            }
        }

        // 4. Sliding-window rate limit check.
        lock (_rateLimitLock)
        {
            var windowStart = DateTimeOffset.UtcNow.AddSeconds(-_options.RateLimitWindowSeconds);

            // Evict timestamps that have fallen outside the current window.
            while (_requestTimestamps.TryPeek(out var oldest) && oldest < windowStart)
            {
                _requestTimestamps.Dequeue();
            }

            if (_requestTimestamps.Count >= _options.RateLimitMaxRequests)
            {
                reason = $"Chaos rate limit exceeded " +
                         $"({_options.RateLimitMaxRequests} requests per {_options.RateLimitWindowSeconds}s window).";
                _logger.LogWarning(
                    "[chaos] [blocked] Chaos request rate-limited. " +
                    "chaos.mode={ChaosMode} chaos.count={Count} chaos.limit={Limit} " +
                    "chaos.windowSeconds={WindowSeconds} chaos.blocked=true chaos.reason=rate_limit",
                    safeMode, _requestTimestamps.Count,
                    _options.RateLimitMaxRequests, _options.RateLimitWindowSeconds);
                return false;
            }

            _requestTimestamps.Enqueue(DateTimeOffset.UtcNow);
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Removes newline and carriage-return characters from user-supplied strings before they are
    /// written to log output, preventing log-forging / log-injection attacks.
    /// </summary>
    private static string SanitizeForLog(string? value) =>
        value is null ? string.Empty : value.Replace('\r', '_').Replace('\n', '_');
}
