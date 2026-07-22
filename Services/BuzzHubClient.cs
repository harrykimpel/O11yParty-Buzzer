using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace O11yPartyBuzzer.Services;

public sealed class BuzzHubClient : IHostedService, IAsyncDisposable
{
    private readonly BuzzHubOptions _options;
    private readonly ILogger<BuzzHubClient> _logger;
    private readonly HubConnection? _connection;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    public BuzzHubClient(IOptions<BuzzHubOptions> options, ILogger<BuzzHubClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_options.Url, o =>
            {
                var secret = _options.SharedSecret;
                o.AccessTokenProvider = () => Task.FromResult<string?>(secret);
            })
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _logger.LogWarning("BuzzHub:Url is not configured — BuzzHubClient will remain idle (dev/local mode).");
            return;
        }

        try
        {
            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("BuzzHubClient connected to {Url}", _options.Url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BuzzHubClient could not connect to {Url} on startup — auto-reconnect will retry.", _options.Url);
            // Best-effort: do not throw; automatic reconnect and lazy-start in SendBuzzAsync will recover.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is null) return;

        try
        {
            await _connection.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BuzzHubClient encountered an error while stopping the connection.");
        }
    }

    public async Task SendBuzzAsync(string teamName, long buzzedAtUtcMs, CancellationToken ct = default)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("BuzzHubClient is not configured (BuzzHub:Url is blank).");
        }

        if (_connection.State != HubConnectionState.Connected)
        {
            await _startLock.WaitAsync(ct);
            try
            {
                // Re-check inside the lock to avoid a double-start race.
                if (_connection.State != HubConnectionState.Connected)
                {
                    _logger.LogInformation("BuzzHubClient is not connected (state={State}); attempting to start.", _connection.State);
                    await _connection.StartAsync(ct);
                }
            }
            finally
            {
                _startLock.Release();
            }
        }

        await _connection.InvokeAsync("Buzz", teamName, buzzedAtUtcMs, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _startLock.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
