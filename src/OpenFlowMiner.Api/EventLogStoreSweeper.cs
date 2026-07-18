using OpenFlowMiner.Core.Storage;

namespace OpenFlowMiner.Api;

/// <summary>
/// Periodically evicts expired logs from the in-memory store so a long-running public demo
/// doesn't accumulate memory from abandoned uploads (bounds the TTL retention from Open Q4).
/// </summary>
public sealed class EventLogStoreSweeper(InMemoryEventLogStore store, ILogger<EventLogStoreSweeper> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = store.RemoveExpired();
            if (removed > 0)
            {
                logger.LogInformation("Swept {Removed} expired event log(s); {Remaining} remain.", removed, store.Count);
            }
        }
    }
}
