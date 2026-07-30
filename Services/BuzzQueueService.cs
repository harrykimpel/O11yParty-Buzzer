using System.Threading.Channels;

namespace O11yPartyBuzzer.Services;

/// <summary>
/// Background queue that buffers buzz events for fallback delivery when the BuzzHub SignalR
/// connection is temporarily unavailable. Implements graceful degradation: the /api/buzz
/// endpoint returns HTTP 200 immediately and this service retries delivery asynchronously
/// with exponential backoff.
/// </summary>
public sealed class BuzzQueueService : BackgroundService
{
    private const int QueueCapacity = 200;
    private const int MaxRetryAttempts = 3;

    private static readonly int[] RetryDelaysMs = [2_000, 4_000, 8_000];

    private readonly Channel<BuzzRecord> _channel;
    private readonly BuzzHubClient _buzzHubClient;
    private readonly ILogger<BuzzQueueService> _logger;

    public BuzzQueueService(
        BuzzHubClient buzzHubClient,
        ILogger<BuzzQueueService> logger)
    {
        _buzzHubClient = buzzHubClient;
        _logger = logger;
        _channel = Channel.CreateBounded<BuzzRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Gets the number of buzzes currently waiting for delivery.</summary>
    public int QueueDepth => _channel.Reader.Count;

    /// <summary>
    /// Enqueues a buzz for fallback delivery. Returns <c>false</c> only if the channel writer
    /// has been completed (i.e. the app is shutting down). When the channel is full the oldest
    /// entry is dropped automatically (<see cref="BoundedChannelFullMode.DropOldest"/>).
    /// </summary>
    public bool TryEnqueue(BuzzRecord record) => _channel.Writer.TryWrite(record);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BuzzQueueService started.");

        await foreach (var record in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await RetryDeliverAsync(record, stoppingToken);
        }
    }

    private async Task RetryDeliverAsync(BuzzRecord record, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var delayMs = RetryDelaysMs[Math.Min(attempt - 1, RetryDelaysMs.Length - 1)];
                _logger.LogInformation(
                    "BuzzQueue: retry {Attempt}/{Max} for team={TeamName} — waiting {DelayMs}ms",
                    attempt, MaxRetryAttempts, record.TeamName, delayMs);

                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "BuzzQueue: cancelled during backoff for team={TeamName}",
                        record.TeamName);
                    return;
                }
            }

            try
            {
                await _buzzHubClient.SendBuzzAsync(record.TeamName, record.BuzzedAtUtcMs, ct);

                _logger.LogInformation(
                    "BuzzQueue: delivered buzz for team={TeamName} (attempt {Attempt}/{Max})",
                    record.TeamName, attempt + 1, MaxRetryAttempts);
                NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/BuzzQueue/Delivered", 1);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "BuzzQueue: shutdown requested — stopping retry for team={TeamName}",
                    record.TeamName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BuzzQueue: attempt {Attempt}/{Max} failed for team={TeamName}",
                    attempt + 1, MaxRetryAttempts, record.TeamName);
            }
        }

        _logger.LogError(
            "BuzzQueue: buzz dropped after {MaxAttempts} attempts — team={TeamName}, buzzedAt={BuzzedAtUtcMs}",
            MaxRetryAttempts, record.TeamName, record.BuzzedAtUtcMs);
        NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/BuzzQueue/Dropped", 1);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
