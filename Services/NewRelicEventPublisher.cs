using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class NewRelicEventPublisher(
    HttpClient httpClient,
    IOptions<NewRelicOptions> options,
    ILogger<NewRelicEventPublisher> logger) : INewRelicEventPublisher
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly NewRelicOptions _options = options.Value;
    private readonly ILogger<NewRelicEventPublisher> _logger = logger;

    private const int SlowCallThresholdMs = 1_000;

    private void LogCallDuration(string operation, long elapsedMs, int statusCode)
    {
        if (elapsedMs > SlowCallThresholdMs)
        {
            _logger.LogWarning(
                "Slow New Relic API call for {Operation}: {ElapsedMs} ms",
                operation, elapsedMs);
        }
        else
        {
            _logger.LogInformation(
                "{Operation} published successfully in {ElapsedMs} ms (status={StatusCode})",
                operation, elapsedMs, statusCode);
        }
    }

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

        _logger.LogInformation("Publishing buzz event for team {TeamName}", teamName);
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            sw.Stop();
            LogCallDuration("BuzzEvent", sw.ElapsedMilliseconds, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to publish buzz event after {ElapsedMs} ms (team={TeamName})",
                sw.ElapsedMilliseconds, teamName);
            throw;
        }
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

        _logger.LogInformation("Publishing lead capture event");
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            sw.Stop();
            LogCallDuration("LeadCaptureEvent", sw.ElapsedMilliseconds, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to publish lead capture event after {ElapsedMs} ms",
                sw.ElapsedMilliseconds);
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
}
