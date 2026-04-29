using ThreadPoolModule.Logging;

namespace ThreadPoolModule.ThreadPool;

public sealed class ThreadPoolDemonstrationResult
{
    public required PoolSnapshot FinalSnapshot { get; init; }
    public required int TotalRuns { get; init; }
    public required TimeSpan Duration { get; init; }
}

public static class ThreadPoolDemonstration
{
    public static ThreadPoolDemonstrationResult Run(int runs = 50)
    {
        if (runs < 50)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "At least 50 runs are required.");
        }

        var random = new Random();
        var startedAt = DateTime.UtcNow;

        using var pool = new DynamicThreadPool(
            minThreads: 2,
            maxThreads: 10,
            idleTimeout: TimeSpan.FromSeconds(2),
            stuckWorkerTimeout: TimeSpan.FromSeconds(4),
            queueWaitScaleUpThreshold: TimeSpan.FromMilliseconds(800));

        for (var i = 0; i < runs; i++)
        {
            var runId = i + 1;
            var pattern = runId % 10;

            if (pattern is >= 0 and <= 2)
            {
                Thread.Sleep(150);
            }
            else if (pattern is >= 3 and <= 7)
            {
                Thread.Sleep(20);
            }
            else
            {
                Thread.Sleep(400);
            }

            pool.Enqueue(() =>
            {
                try
                {
                    var durationMs = runId % 11 == 0
                        ? 4500
                        : random.Next(150, 700);

                    Thread.Sleep(durationMs);

                    if (runId % 17 == 0)
                    {
                        throw new InvalidOperationException($"Simulated failure in test #{runId}");
                    }
                }
                finally
                {
                    // no-op: task completion is tracked by pool metrics
                }
            }, maxQueueWait: TimeSpan.FromSeconds(3));

            if (runId % 8 == 0)
            {
                Logger.Info($"Burst checkpoint at test #{runId}");
            }
        }

        pool.WaitForIdle(TimeSpan.FromSeconds(90));
        var snapshot = pool.GetSnapshot();
        var totalDuration = DateTime.UtcNow - startedAt;

        Logger.Info(
            $"Demo finished. Runs={runs}, workers={snapshot.WorkersTotal}, completed={snapshot.CompletedTasks}, " +
            $"failed={snapshot.FailedTasks}, replaced={snapshot.ReplacedWorkers}, duration={totalDuration.TotalSeconds:F1}s");

        return new ThreadPoolDemonstrationResult
        {
            FinalSnapshot = snapshot,
            TotalRuns = runs,
            Duration = totalDuration
        };
    }
}
