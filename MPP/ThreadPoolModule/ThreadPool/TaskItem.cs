namespace ThreadPoolModule.ThreadPool;

public class TaskItem
{
    private static long _idCounter;

    public long Id { get; }
    public Action Work { get; }
    public DateTime EnqueueTimeUtc { get; }
    public TimeSpan MaxQueueWaitTime { get; }

    public TaskItem(Action work, TimeSpan maxQueueWaitTime)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work), "Task work cannot be null.");
        Id = Interlocked.Increment(ref _idCounter);
        EnqueueTimeUtc = DateTime.UtcNow;
        MaxQueueWaitTime = maxQueueWaitTime;
    }

    public TimeSpan TimeInQueue => DateTime.UtcNow - EnqueueTimeUtc;

    public bool HasTimedOut()
    {
        return TimeInQueue > MaxQueueWaitTime;
    }
}