using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
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

    /// <summary>Maximum number of attempts (1 initial + 2 retries).</summary>
    private const int MaxAttempts = 3;

    public async Task PublishBuzzAsync(string teamName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            throw new ArgumentException("Team name is required.", nameof(teamName));
        }

        ValidateNewRelicConfiguration();

        var endpoint = BuildEndpoint(_options.Region, _options.AccountId);
        var payload = new[]
        {
            new
            {
                eventType = _options.EventType,
                teamName,
                canAnswer = true,
                buzzedAtUtc = DateTimeOffset.UtcNow
            }
        };

        await SendWithRetryAsync(endpoint, payload, "buzz", cancellationToken);
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
        var payload = new[]
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
        };

        await SendWithRetryAsync(endpoint, payload, "lead-capture", cancellationToken);
    }

    /// <summary>
    /// Sends a POST request to the New Relic ingest endpoint with exponential back-off retry
    /// for transient server errors and network timeouts.
    /// </summary>
    private async Task SendWithRetryAsync<T>(
        string endpoint,
        T payload,
        string operationName,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("X-Insert-Key", _options.IngestApiKey);
                request.Content = JsonContent.Create(payload);

                _logger.LogDebug(
                    "Sending New Relic {Operation} event (attempt {Attempt}/{MaxAttempts})",
                    operationName, attempt, MaxAttempts);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 1)
                    {
                        _logger.LogInformation(
                            "New Relic {Operation} succeeded on attempt {Attempt}",
                            operationName, attempt);
                    }
                    return;
                }

                // 4xx errors are client errors – retrying won't help.
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    _logger.LogError(
                        "New Relic {Operation} failed with HTTP {StatusCode}. Not retrying client error.",
                        operationName, (int)response.StatusCode);
                    response.EnsureSuccessStatusCode();
                }

                // 5xx or unexpected: log and maybe retry.
                _logger.LogWarning(
                    "New Relic {Operation} returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}",
                    operationName, (int)response.StatusCode, attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                {
                    await BackoffDelayAsync(attempt, cancellationToken);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller cancelled – propagate immediately without retrying.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // HttpClient timeout fired (the internal CancellationToken was cancelled).
                _logger.LogError(
                    ex,
                    "New Relic {Operation} timed out on attempt {Attempt}/{MaxAttempts}",
                    operationName, attempt, MaxAttempts);
                lastException = new TimeoutException(
                    $"New Relic {operationName} request timed out (attempt {attempt}/{MaxAttempts}).", ex);

                if (attempt < MaxAttempts)
                {
                    await BackoffDelayAsync(attempt, cancellationToken);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "New Relic {Operation} HTTP request failed on attempt {Attempt}/{MaxAttempts}: {Message}",
                    operationName, attempt, MaxAttempts, ex.Message);
                lastException = ex;

                if (attempt < MaxAttempts)
                {
                    await BackoffDelayAsync(attempt, cancellationToken);
                }
            }
        }

        // All attempts exhausted.
        if (lastException is not null)
        {
            throw lastException;
        }
    }

    /// <summary>
    /// Waits before the next retry attempt using exponential back-off (1 s, 2 s for attempts 1 and 2).
    /// Respects caller cancellation so the back-off does not block a shutdown.
    /// </summary>
    private static async Task BackoffDelayAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during back-off is expected on shutdown – let it propagate.
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
