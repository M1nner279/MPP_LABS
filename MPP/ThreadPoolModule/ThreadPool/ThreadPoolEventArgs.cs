namespace ThreadPoolModule.ThreadPool;

public sealed class ThreadPoolEventArgs : EventArgs
{
    public required string EventType { get; init; }
    public string? WorkerName { get; init; }
    public long? TaskId { get; init; }
    public string? Message { get; init; }
    public required PoolSnapshot Snapshot { get; init; }
}
