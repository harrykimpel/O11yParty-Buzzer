using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace O11yPartyBuzzer.Services;

public sealed class BlazorCircuitMonitor
{
    private readonly ConcurrentDictionary<Circuit, byte> _openCircuits = new();
    private readonly ConcurrentDictionary<Circuit, byte> _connectedCircuits = new();
    private readonly ConcurrentDictionary<Circuit, byte> _disconnectedCircuits = new();
    private long _lastConnectionUpTicks;
    private long _lastConnectionDownTicks;

    public void OnCircuitOpened(Circuit circuit) => _openCircuits.TryAdd(circuit, 0);

    public void OnCircuitClosed(Circuit circuit)
    {
        _openCircuits.TryRemove(circuit, out _);
        _connectedCircuits.TryRemove(circuit, out _);
        _disconnectedCircuits.TryRemove(circuit, out _);
    }

    public void OnConnectionUp(Circuit circuit)
    {
        _connectedCircuits.TryAdd(circuit, 0);
        _disconnectedCircuits.TryRemove(circuit, out _);
        Interlocked.Exchange(ref _lastConnectionUpTicks, DateTimeOffset.UtcNow.Ticks);
    }

    public void OnConnectionDown(Circuit circuit)
    {
        _connectedCircuits.TryRemove(circuit, out _);
        _disconnectedCircuits.TryAdd(circuit, 0);
        Interlocked.Exchange(ref _lastConnectionDownTicks, DateTimeOffset.UtcNow.Ticks);
    }

    public BlazorCircuitSnapshot GetSnapshot()
    {
        var openCircuitCount = _openCircuits.Count;
        var connectedCircuitCount = _connectedCircuits.Count;
        var disconnectedCircuitCount = _disconnectedCircuits.Count;

        return new BlazorCircuitSnapshot(
            openCircuitCount,
            connectedCircuitCount,
            disconnectedCircuitCount,
            GetTimestamp(_lastConnectionUpTicks),
            GetTimestamp(_lastConnectionDownTicks));
    }

    private static DateTimeOffset? GetTimestamp(long ticks) =>
        ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
}

public sealed record BlazorCircuitSnapshot(
    int OpenCircuitCount,
    int ConnectedCircuitCount,
    int DisconnectedCircuitCount,
    DateTimeOffset? LastConnectionUpUtc,
    DateTimeOffset? LastConnectionDownUtc);
