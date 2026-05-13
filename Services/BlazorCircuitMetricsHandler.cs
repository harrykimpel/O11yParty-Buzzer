using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;
using NewRelic.Api.Agent;

namespace O11yPartyBuzzer.Services;

public sealed class BlazorCircuitMetricsHandler(ILogger<BlazorCircuitMetricsHandler> logger) : CircuitHandler
{
    private readonly ILogger<BlazorCircuitMetricsHandler> _logger = logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _circuitOpenedAt = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _connectionOpenedAt = new();
    private int _activeCircuits;
    private int _activeConnections;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuitOpenedAt[circuit.Id] = DateTimeOffset.UtcNow;
        var activeCircuits = Interlocked.Increment(ref _activeCircuits);
        RecordGauge("Custom/Blazor/ActiveCircuits", activeCircuits);
        NewRelic.Api.Agent.NewRelic.IncrementCounter("Custom/Blazor/CircuitsOpened");
        _logger.LogInformation("Blazor circuit opened. Active circuits: {ActiveCircuits}", activeCircuits);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_circuitOpenedAt.TryRemove(circuit.Id, out var openedAt))
        {
            var lifetime = DateTimeOffset.UtcNow - openedAt;
            NewRelic.Api.Agent.NewRelic.RecordResponseTimeMetric("Custom/Blazor/CircuitDuration", (long)lifetime.TotalMilliseconds);
        }

        var activeCircuits = DecrementWithoutGoingNegative(ref _activeCircuits);
        RecordGauge("Custom/Blazor/ActiveCircuits", activeCircuits);
        _logger.LogInformation("Blazor circuit closed. Active circuits: {ActiveCircuits}", activeCircuits);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectionOpenedAt[circuit.Id] = DateTimeOffset.UtcNow;
        var activeConnections = Interlocked.Increment(ref _activeConnections);
        RecordGauge("Custom/Blazor/ActiveConnections", activeConnections);
        NewRelic.Api.Agent.NewRelic.IncrementCounter("Custom/Blazor/ConnectionsOpened");
        _logger.LogInformation("Blazor connection established. Active connections: {ActiveConnections}", activeConnections);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_connectionOpenedAt.TryRemove(circuit.Id, out var connectedAt))
        {
            var lifetime = DateTimeOffset.UtcNow - connectedAt;
            NewRelic.Api.Agent.NewRelic.RecordResponseTimeMetric("Custom/Blazor/ConnectionDuration", (long)lifetime.TotalMilliseconds);
        }

        var activeConnections = DecrementWithoutGoingNegative(ref _activeConnections);
        RecordGauge("Custom/Blazor/ActiveConnections", activeConnections);
        _logger.LogInformation("Blazor connection dropped. Active connections: {ActiveConnections}", activeConnections);
        return Task.CompletedTask;
    }

    private static void RecordGauge(string metricName, int value)
    {
        NewRelic.Api.Agent.NewRelic.RecordMetric(metricName, value);
    }

    private static int DecrementWithoutGoingNegative(ref int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref value);
            if (current == 0)
            {
                return 0;
            }

            if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
            {
                return current - 1;
            }
        }
    }
}
