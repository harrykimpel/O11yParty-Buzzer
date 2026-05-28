using Microsoft.AspNetCore.Components.Server.Circuits;

namespace O11yPartyBuzzer.Services;

public sealed class CircuitHandlerService(
    ILogger<CircuitHandlerService> logger,
    BlazorCircuitMonitor circuitMonitor) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        circuitMonitor.OnCircuitOpened(circuit);
        LogCircuitState("opened");
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        circuitMonitor.OnCircuitClosed(circuit);
        LogCircuitState("closed");
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        circuitMonitor.OnConnectionUp(circuit);
        LogCircuitState("connected");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        circuitMonitor.OnConnectionDown(circuit);
        LogCircuitState("disconnected", isWarning: true);
        return Task.CompletedTask;
    }

    private void LogCircuitState(string transition, bool isWarning = false)
    {
        var snapshot = circuitMonitor.GetSnapshot();

        if (isWarning)
        {
            logger.LogWarning(
                "Blazor circuit {Transition}. OpenCircuits: {OpenCircuitCount}, ConnectedCircuits: {ConnectedCircuitCount}, DisconnectedCircuits: {DisconnectedCircuitCount}",
                transition,
                snapshot.OpenCircuitCount,
                snapshot.ConnectedCircuitCount,
                snapshot.DisconnectedCircuitCount);

            return;
        }

        logger.LogInformation(
            "Blazor circuit {Transition}. OpenCircuits: {OpenCircuitCount}, ConnectedCircuits: {ConnectedCircuitCount}, DisconnectedCircuits: {DisconnectedCircuitCount}",
            transition,
            snapshot.OpenCircuitCount,
            snapshot.ConnectedCircuitCount,
            snapshot.DisconnectedCircuitCount);
    }
}
