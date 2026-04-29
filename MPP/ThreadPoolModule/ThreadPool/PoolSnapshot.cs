namespace ThreadPoolModule.ThreadPool;

public sealed class PoolSnapshot
{
    public int WorkersTotal { get; init; }
    public int WorkersBusy { get; init; }
    public int QueueLength { get; init; }
    public long SubmittedTasks { get; init; }
    public long CompletedTasks { get; init; }
    public long FailedTasks { get; init; }
    public long TimedOutInQueueTasks { get; init; }
    public long ReplacedWorkers { get; init; }
}
