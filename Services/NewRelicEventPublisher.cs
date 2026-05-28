using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class NewRelicEventPublisher(
    HttpClient httpClient,
    IOptions<NewRelicOptions> options,
    ILogger<NewRelicEventPublisher> logger) : INewRelicEventPublisher
{
    private readonly object _circuitBreakerLock = new();
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<NewRelicEventPublisher> _logger = logger;
    private readonly NewRelicOptions _options = options.Value;
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenUntilUtc = DateTimeOffset.MinValue;

    public async Task PublishBuzzAsync(string teamName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            throw new ArgumentException("Team name is required.", nameof(teamName));
        }

        ValidateNewRelicConfiguration();

        var endpoint = BuildEndpoint(_options.Region, _options.AccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Insert-Key", _options.IngestApiKey);
        request.Content = JsonContent.Create(new[]
        {
            new
            {
                eventType = _options.EventType,
                teamName,
                canAnswer = true,
                buzzedAtUtc = DateTimeOffset.UtcNow
            }
        });

        await SendEventAsync(request, _options.EventType, cancellationToken);
    }

    public async Task PublishLeadCaptureAsync(
        string firstName,
        string lastName,
        string businessEmailAddress,
        string companyName,
        string jobTitle,
        string country,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("First name is required.", nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("Last name is required.", nameof(lastName));
        }

        if (string.IsNullOrWhiteSpace(businessEmailAddress))
        {
            throw new ArgumentException("Business email address is required.", nameof(businessEmailAddress));
        }

        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ArgumentException("Company name is required.", nameof(companyName));
        }

        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            throw new ArgumentException("Job title is required.", nameof(jobTitle));
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            throw new ArgumentException("Country is required.", nameof(country));
        }

        ValidateNewRelicConfiguration();

        var endpoint = BuildEndpoint(_options.Region, _options.AccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Insert-Key", _options.IngestApiKey);
        request.Content = JsonContent.Create(new[]
        {
            new
            {
                eventType = _options.LeadCaptureEventType,
                firstName,
                lastName,
                businessEmailAddress,
                companyName,
                jobTitle,
                country,
                capturedAtUtc = DateTimeOffset.UtcNow
            }
        });

        await SendEventAsync(request, _options.LeadCaptureEventType, cancellationToken);
    }

    private async Task SendEventAsync(HttpRequestMessage request, string eventType, CancellationToken cancellationToken)
    {
        if (TryGetCircuitOpenUntil(out var openUntil))
        {
            _logger.LogWarning(
                "Skipping New Relic publish for {EventType}; circuit breaker is open until {CircuitOpenUntilUtc}",
                eventType,
                openUntil);
            throw new InvalidOperationException("Circuit breaker is open; New Relic event publishing is temporarily disabled.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            stopwatch.Stop();
            MarkSuccess();
            _logger.LogInformation(
                "New Relic publish succeeded for {EventType} in {ElapsedMilliseconds}ms (status {StatusCode})",
                eventType,
                stopwatch.ElapsedMilliseconds,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            MarkFailure(eventType, stopwatch.ElapsedMilliseconds, "timeout");
            throw;
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            MarkFailure(eventType, stopwatch.ElapsedMilliseconds, "http");
            throw;
        }
    }

    private void ValidateNewRelicConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.IngestApiKey))
        {
            throw new InvalidOperationException("New Relic IngestApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.AccountId))
        {
            throw new InvalidOperationException("New Relic AccountId is not configured.");
        }
    }

    private static string BuildEndpoint(string region, string accountId)
    {
        var normalizedRegion = region.Trim().ToUpperInvariant();
        var host = normalizedRegion switch
        {
            "EU" => "insights-collector.eu01.nr-data.net",
            _ => "insights-collector.newrelic.com"
        };

        return $"https://{host}/v1/accounts/{accountId}/events";
    }

    private bool TryGetCircuitOpenUntil(out DateTimeOffset openUntilUtc)
    {
        lock (_circuitBreakerLock)
        {
            if (_circuitOpenUntilUtc > DateTimeOffset.UtcNow)
            {
                openUntilUtc = _circuitOpenUntilUtc;
                return true;
            }

            openUntilUtc = DateTimeOffset.MinValue;
            return false;
        }
    }

    private void MarkSuccess()
    {
        lock (_circuitBreakerLock)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntilUtc = DateTimeOffset.MinValue;
        }
    }

    private void MarkFailure(string eventType, long elapsedMilliseconds, string failureType)
    {
        int failures;
        DateTimeOffset openUntil;
        var threshold = Math.Max(1, _options.CircuitBreakerFailureThreshold);
        var breakDurationSeconds = Math.Max(1, _options.CircuitBreakerBreakDurationSeconds);

        lock (_circuitBreakerLock)
        {
            failures = ++_consecutiveFailures;
            if (failures >= threshold)
            {
                _circuitOpenUntilUtc = DateTimeOffset.UtcNow.AddSeconds(breakDurationSeconds);
            }
            openUntil = _circuitOpenUntilUtc;
        }

        _logger.LogWarning(
            "New Relic publish failed for {EventType} after {ElapsedMilliseconds}ms (type: {FailureType}). Consecutive failures: {Failures}. Circuit open until: {CircuitOpenUntilUtc}",
            eventType,
            elapsedMilliseconds,
            failureType,
            failures,
            openUntil == DateTimeOffset.MinValue ? null : openUntil);
    }
}
