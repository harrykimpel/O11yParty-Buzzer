using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace O11yPartyBuzzer.Services;

/// <summary>
/// ASP.NET Core health check that reports the state of the BuzzHub SignalR connection
/// and the number of buzzes currently queued for fallback delivery.
/// </summary>
public sealed class BuzzHubHealthCheck(
    BuzzHubClient buzzHubClient,
    BuzzQueueService buzzQueueService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var state = buzzHubClient.ConnectionState;
        var queueDepth = buzzQueueService.QueueDepth;

        var data = new Dictionary<string, object>
        {
            ["connectionState"] = state.ToString(),
            ["queueDepth"] = queueDepth
        };

        var result = state switch
        {
            HubConnectionState.Connected when queueDepth == 0
                => HealthCheckResult.Healthy("BuzzHub connected.", data: data),

            HubConnectionState.Connected
                => HealthCheckResult.Degraded(
                    $"BuzzHub connected but {queueDepth} buzz(es) queued for retry.", data: data),

            HubConnectionState.Connecting or HubConnectionState.Reconnecting
                => HealthCheckResult.Degraded(
                    $"BuzzHub {state.ToString().ToLowerInvariant()} — {queueDepth} buzz(es) queued.", data: data),

            _ => HealthCheckResult.Unhealthy(
                    $"BuzzHub disconnected — {queueDepth} buzz(es) queued.", data: data)
        };

        return Task.FromResult(result);
    }
}
