using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class NewRelicEventPublisher(
    HttpClient httpClient,
    IMemoryCache memoryCache,
    ILogger<NewRelicEventPublisher> logger,
    IOptions<NewRelicOptions> options) : INewRelicEventPublisher
{
    private const string EndpointCacheKey = "NewRelicEndpoint";
    private readonly HttpClient _httpClient = httpClient;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<NewRelicEventPublisher> _logger = logger;
    private readonly NewRelicOptions _options = options.Value;

    public async Task PublishBuzzAsync(string teamName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            throw new ArgumentException("Team name is required.", nameof(teamName));
        }

        ValidateNewRelicConfiguration();

        var endpoint = GetEndpoint();
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

        using var response = await SendWithDiagnosticsAsync(request, "buzz", cancellationToken);
        response.EnsureSuccessStatusCode();
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

        var endpoint = GetEndpoint();
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

        using var response = await SendWithDiagnosticsAsync(request, "lead-capture", cancellationToken);
        response.EnsureSuccessStatusCode();
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

    private string GetEndpoint()
    {
        var endpoint = _memoryCache.GetOrCreate(EndpointCacheKey, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            return BuildEndpoint(_options.Region, _options.AccountId);
        });

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Unable to build New Relic endpoint from current configuration.");
        }

        return endpoint;
    }

    private async Task<HttpResponseMessage> SendWithDiagnosticsAsync(
        HttpRequestMessage request,
        string operationName,
        CancellationToken cancellationToken)
    {
        var warningThresholdMs = _options.SlowRequestWarningThresholdMs > 0
            ? _options.SlowRequestWarningThresholdMs
            : NewRelicOptions.DefaultSlowRequestWarningThresholdMs;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (stopwatch.ElapsedMilliseconds >= warningThresholdMs)
            {
                _logger.LogWarning(
                    "Slow New Relic {Operation} publish took {ElapsedMs} ms (threshold {ThresholdMs} ms).",
                    operationName,
                    stopwatch.ElapsedMilliseconds,
                    warningThresholdMs);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed New Relic {Operation} publish after {ElapsedMs} ms.",
                operationName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
