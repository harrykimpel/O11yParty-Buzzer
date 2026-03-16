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

        if (string.IsNullOrWhiteSpace(_options.IngestApiKey))
        {
            throw new InvalidOperationException("New Relic IngestApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.AccountId))
        {
            throw new InvalidOperationException("New Relic AccountId is not configured.");
        }

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
