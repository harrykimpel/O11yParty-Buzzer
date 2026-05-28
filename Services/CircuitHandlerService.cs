using Microsoft.AspNetCore.Components.Server.Circuits;

namespace O11yPartyBuzzer.Services;

public sealed class CircuitHandlerService(ILogger<CircuitHandlerService> logger) : CircuitHandler
{
    private readonly ILogger<CircuitHandlerService> _logger = logger;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blazor circuit connection up: {CircuitId}", circuit.Id);
        TryRecordMetric("Custom/Blazor/CircuitConnected");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Blazor circuit connection down: {CircuitId}", circuit.Id);
        TryRecordMetric("Custom/Blazor/CircuitDisconnected");
        return Task.CompletedTask;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blazor circuit opened: {CircuitId}", circuit.Id);
        TryRecordMetric("Custom/Blazor/CircuitOpened");
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blazor circuit closed: {CircuitId}", circuit.Id);
        TryRecordMetric("Custom/Blazor/CircuitClosed");
        return Task.CompletedTask;
    }

    private void TryRecordMetric(string metricName)
    {
        try
        {
            NewRelic.Api.Agent.NewRelic.RecordMetric(metricName, 1);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to record New Relic metric {MetricName}", metricName);
        }
    }
}
