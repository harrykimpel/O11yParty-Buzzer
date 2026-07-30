using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

/// <summary>
/// Thrown by <see cref="BuzzHubClient.SendBuzzAsync"/> when the circuit breaker is open,
/// i.e. the hub has been consistently unreachable and requests are being shed to fail fast.
/// </summary>
public sealed class CircuitBreakerOpenException(string message) : InvalidOperationException(message);

/// <summary>
/// Point-in-time snapshot of <see cref="BuzzHubClient"/> health, returned by
/// <see cref="BuzzHubClient.GetHealthStatus"/>.
/// </summary>
public sealed record BuzzHubHealthStatus(
    bool IsHealthy,
    string ConnectionState,
    bool CircuitBreakerOpen,
    int ConsecutiveFailures);

public sealed class BuzzHubClient : IHostedService, IAsyncDisposable
{
    private readonly BuzzHubOptions _options;
    private readonly ILogger<BuzzHubClient> _logger;
    private readonly HubConnection? _connection;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    // ── Circuit-breaker state (thread-safe via Interlocked / volatile) ──────────
    private int _consecutiveFailures = 0;
    private long _circuitOpenedAtTicks = 0;   // DateTime.UtcNow.Ticks when opened
    private volatile bool _circuitOpen = false;

    private TimeSpan BreakDuration =>
        TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds > 0
            ? _options.CircuitBreakerBreakDurationSeconds
            : 30);

    public BuzzHubClient(IOptions<BuzzHubOptions> options, ILogger<BuzzHubClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_options.Url, o =>
            {
                var secret = _options.SharedSecret;
                o.AccessTokenProvider = () => Task.FromResult<string?>(secret);
            })
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _logger.LogWarning("BuzzHub:Url is not configured — BuzzHubClient will remain idle (dev/local mode).");
            return;
        }

        try
        {
            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("BuzzHubClient connected to {Url}", _options.Url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BuzzHubClient could not connect to {Url} on startup — auto-reconnect will retry.", _options.Url);
            // Best-effort: do not throw; automatic reconnect and lazy-start in SendBuzzAsync will recover.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is null) return;

        try
        {
            await _connection.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BuzzHubClient encountered an error while stopping the connection.");
        }
    }

    // [Trace] gives this call its own segment/span in the APM distributed trace, so
    // "how long did the critical-path SignalR hop take" is visible per-request, not
    // just inferred from the overall /api/buzz transaction time.
    [NewRelic.Api.Agent.Trace]
    public async Task SendBuzzAsync(string teamName, long buzzedAtUtcMs, CancellationToken ct = default)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("BuzzHubClient is not configured (BuzzHub:Url is blank).");
        }

        // ── Circuit-breaker: fail fast when the hub is persistently unreachable ──
        if (!TryEnterCircuit())
        {
            _logger.LogWarning("Circuit breaker open — rejecting buzz without attempting hub call.");
            NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/BuzzHub/CircuitBreaker/Rejected", 1);
            throw new CircuitBreakerOpenException(
                "BuzzHub circuit breaker is open; the hub is temporarily unavailable.");
        }

        // ── Per-call wall-clock timeout (wraps all retry attempts) ──────────────
        var timeoutSeconds = _options.SendTimeoutSeconds > 0 ? _options.SendTimeoutSeconds : 10;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var linkedCt = timeoutCts.Token;

        // The buzzer holds exactly one shared connection to the game's hub, so every
        // concurrent buzz serializes through this call -- under enough simultaneous
        // buzzes it queues (measured: ~300ms/buzz, degrading past ~25-40 concurrent).
        // This custom metric makes that queueing visible in NR dashboards/alerts
        // instead of only showing up as a load-test finding.
        var stopwatch = Stopwatch.StartNew();
        var maxAttempts = Math.Max(1, _options.MaxRetryAttempts + 1);
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                // Exponential back-off: 200 ms, 400 ms, 800 ms, …
                var delayMs = 200 * (1 << (attempt - 1));
                _logger.LogWarning(
                    "SendBuzzAsync retry {Attempt}/{Max} in {DelayMs} ms.",
                    attempt + 1, maxAttempts, delayMs);
                try
                {
                    await Task.Delay(delayMs, linkedCt);
                }
                catch (OperationCanceledException) { /* handled below */ }
            }

            // Abort early if timeout or caller cancelled.
            if (linkedCt.IsCancellationRequested)
                break;

            try
            {
                await EnsureConnectedAsync(linkedCt);
                await _connection.InvokeAsync("Buzz", teamName, buzzedAtUtcMs, linkedCt);

                // ── Success: reset circuit breaker ───────────────────────────────
                RecordSuccess();
                NewRelic.Api.Agent.NewRelic.RecordResponseTimeMetric(
                    "Custom/BuzzHub/SendBuzzAsync", stopwatch.ElapsedMilliseconds);
                return;
            }
            catch (OperationCanceledException ex) when (
                timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Timeout from our internal CTS, not from the caller — don't retry.
                lastException = new TimeoutException(
                    $"SendBuzzAsync timed out after {timeoutSeconds} s for team {teamName}.", ex);
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex, "SendBuzzAsync attempt {Attempt}/{Max} failed.",
                    attempt + 1, maxAttempts);
            }
        }

        // ── All attempts exhausted: record failure, maybe open circuit ───────────
        RecordFailure();
        NewRelic.Api.Agent.NewRelic.RecordResponseTimeMetric(
            "Custom/BuzzHub/SendBuzzAsync", stopwatch.ElapsedMilliseconds);
        throw lastException!;
    }

    /// <summary>Returns a health snapshot for the <c>/health</c> endpoint.</summary>
    public BuzzHubHealthStatus GetHealthStatus()
    {
        if (_connection is null)
        {
            // Unconfigured (dev/local mode) — treat as degraded but not broken.
            return new BuzzHubHealthStatus(
                IsHealthy: false,
                ConnectionState: "Unconfigured",
                CircuitBreakerOpen: false,
                ConsecutiveFailures: 0);
        }

        var cbOpen = _circuitOpen;
        var failures = Volatile.Read(ref _consecutiveFailures);
        var state = _connection.State.ToString();
        var healthy = _connection.State == HubConnectionState.Connected && !cbOpen;

        return new BuzzHubHealthStatus(
            IsHealthy: healthy,
            ConnectionState: state,
            CircuitBreakerOpen: cbOpen,
            ConsecutiveFailures: failures);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures <see cref="_connection"/> is <c>Connected</c>, acquiring the start-lock
    /// to avoid concurrent reconnect races.
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connection!.State == HubConnectionState.Connected) return;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_connection.State != HubConnectionState.Connected)
            {
                _logger.LogInformation(
                    "BuzzHubClient reconnecting (state={State})…", _connection.State);
                await _connection.StartAsync(ct);
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Resets the circuit breaker after a successful call.</summary>
    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        if (_circuitOpen)
        {
            _circuitOpen = false;
            _logger.LogInformation("Circuit breaker closed — hub connection recovered.");
            NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/BuzzHub/CircuitBreaker/Closed", 1);
        }
    }

    /// <summary>
    /// Increments the failure counter; opens the circuit breaker once the threshold is reached.
    /// </summary>
    private void RecordFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        var threshold = Math.Max(1, _options.CircuitBreakerThreshold);

        if (failures >= threshold && !_circuitOpen)
        {
            Interlocked.Exchange(ref _circuitOpenedAtTicks, DateTime.UtcNow.Ticks);
            _circuitOpen = true;
            _logger.LogWarning(
                "Circuit breaker opened after {Failures} consecutive failures. " +
                "Hub calls will be shed for {BreakSeconds} s.",
                failures, _options.CircuitBreakerBreakDurationSeconds);
            NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/BuzzHub/CircuitBreaker/Open", 1);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the circuit is closed (or half-open after the break duration).
    /// </summary>
    private bool TryEnterCircuit()
    {
        if (!_circuitOpen) return true;

        // Check whether the break duration has elapsed; if so, allow a single probe (half-open).
        var openedAt = new DateTime(
            Interlocked.Read(ref _circuitOpenedAtTicks), DateTimeKind.Utc);

        if (DateTime.UtcNow - openedAt >= BreakDuration)
        {
            // Transition to half-open: allow one probe attempt.
            _circuitOpen = false;
            _logger.LogInformation(
                "Circuit breaker entering half-open state — allowing a probe attempt.");
            return true;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _startLock.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
