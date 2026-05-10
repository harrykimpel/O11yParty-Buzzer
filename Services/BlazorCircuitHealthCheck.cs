using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class BlazorCircuitHealthCheck(
    BlazorCircuitMonitor circuitMonitor,
    IOptions<BlazorServerOptions> options) : IHealthCheck
{
    private readonly BlazorCircuitMonitor _circuitMonitor = circuitMonitor;
    private readonly BlazorServerOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _circuitMonitor.GetSnapshot();
        var data = new Dictionary<string, object>
        {
            ["openCircuits"] = snapshot.OpenCircuitCount,
            ["connectedCircuits"] = snapshot.ConnectedCircuitCount,
            ["disconnectedCircuits"] = snapshot.DisconnectedCircuitCount
        };

        if (snapshot.LastConnectionUpUtc is { } lastConnectionUpUtc)
        {
            data["lastConnectionUpUtc"] = lastConnectionUpUtc.ToString("O");
        }

        if (snapshot.LastConnectionDownUtc is { } lastConnectionDownUtc)
        {
            data["lastConnectionDownUtc"] = lastConnectionDownUtc.ToString("O");
        }

        if (snapshot.DisconnectedCircuitCount >= _options.UnhealthyDisconnectedCircuitThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Blazor disconnected circuits exceeded the configured threshold.",
                data: data));
        }

        if (snapshot.DisconnectedCircuitCount > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Blazor has disconnected circuits awaiting reconnection or cleanup.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Blazor circuits are healthy.", data));
    }
}
