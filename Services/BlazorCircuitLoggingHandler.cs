using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace O11yPartyBuzzer.Services;

public sealed class BlazorCircuitLoggingHandler(ILogger<BlazorCircuitLoggingHandler> logger) : CircuitHandler
{
    private readonly ILogger<BlazorCircuitLoggingHandler> _logger = logger;
    private readonly ConcurrentDictionary<Circuit, DateTimeOffset> _openedAtUtc = new();

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _openedAtUtc[circuit] = DateTimeOffset.UtcNow;
        _logger.LogInformation("Blazor circuit opened: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Blazor circuit closed: {CircuitId} after {DurationSeconds:F2}s",
            circuit.Id,
            GetCircuitDurationSeconds(circuit));

        _openedAtUtc.TryRemove(circuit, out _);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blazor circuit connection up: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Blazor circuit connection down: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    private double GetCircuitDurationSeconds(Circuit circuit)
    {
        if (!_openedAtUtc.TryGetValue(circuit, out var startedAt))
        {
            return 0;
        }

        return (DateTimeOffset.UtcNow - startedAt).TotalSeconds;
    }
}
