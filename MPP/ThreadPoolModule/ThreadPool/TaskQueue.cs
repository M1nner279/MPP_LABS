using ThreadPoolModule.Logging;

namespace ThreadPoolModule.ThreadPool;

public class TaskQueue
{
    private readonly Queue<TaskItem> _queue = new();
    private readonly object _sync = new();

    public void Enqueue(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_sync)
        {
            _queue.Enqueue(task);
            Monitor.Pulse(_sync);
        }

        Logger.Debug($"Task #{task.Id} enqueued. Queue={Count}");
    }

    public bool TryDequeue(int waitTimeoutMs, out TaskItem? task)
    {
        lock (_sync)
        {
            if (_queue.Count == 0)
            {
                if (!Monitor.Wait(_sync, waitTimeoutMs))
                {
                    task = null;
                    return false;
                }
            }

            if (_queue.Count == 0)
            {
                task = null;
                return false;
            }

            task = _queue.Dequeue();
            return true;
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _queue.Count;
            }
        }
    }

    public TaskItem[] Snapshot()
    {
        lock (_sync)
        {
            return _queue.ToArray();
        }
    }

    public TimeSpan? GetOldestWaitTime()
    {
        lock (_sync)
        {
            if (_queue.Count == 0)
            {
                return null;
            }

            return DateTime.UtcNow - _queue.Peek().EnqueueTimeUtc;
        }
    }

    public int RemoveTimedOutTasks()
    {
        lock (_sync)
        {
            if (_queue.Count == 0)
            {
                return 0;
            }

            var kept = new Queue<TaskItem>(_queue.Where(t => !t.HasTimedOut()));
            var removed = _queue.Count - kept.Count;
            _queue.Clear();
            foreach (var task in kept)
            {
                _queue.Enqueue(task);
            }

            if (removed > 0)
            {
                Logger.Info($"Queue cleanup removed {removed} timed-out task(s).");
            }

            return removed;
        }
    }

    public void WakeAll()
    {
        lock (_sync)
        {
            Monitor.PulseAll(_sync);
        }
    }
}