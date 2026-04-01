using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class NewRelicEventPublisher(
    HttpClient httpClient,
    IOptions<NewRelicOptions> options) : INewRelicEventPublisher
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly NewRelicOptions _options = options.Value;

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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
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
}
