using ThreadPoolModule.Logging;

namespace ThreadPoolModule.ThreadPool;

public sealed class DynamicThreadPool : IDisposable
{
    private readonly object _sync = new();
    private readonly TaskQueue _queue = new();
    private readonly List<WorkerThread> _workers = new();
    private readonly Thread _watchdogThread;
    private readonly Thread _monitorThread;

    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _stuckWorkerTimeout;
    private readonly TimeSpan _queueWaitScaleUpThreshold;
    private readonly int _queueScaleFactor;
    private readonly int _dequeueWaitMs;

    private volatile bool _disposeRequested;
    private int _workerIdCounter;

    private long _submittedTasks;
    private long _completedTasks;
    private long _failedTasks;
    private long _timedOutInQueueTasks;
    private long _replacedWorkers;
    
    public event EventHandler<ThreadPoolEventArgs>? PoolEvent;

    public DynamicThreadPool(
        int minThreads,
        int maxThreads,
        TimeSpan idleTimeout,
        TimeSpan stuckWorkerTimeout,
        TimeSpan queueWaitScaleUpThreshold,
        int queueScaleFactor = 2,
        int dequeueWaitMs = 300)
    {
        if (minThreads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minThreads));
        }

        if (maxThreads < minThreads)
        {
            throw new ArgumentOutOfRangeException(nameof(maxThreads));
        }

        MinThreads = minThreads;
        MaxThreads = maxThreads;
        _idleTimeout = idleTimeout;
        _stuckWorkerTimeout = stuckWorkerTimeout;
        _queueWaitScaleUpThreshold = queueWaitScaleUpThreshold;
        _queueScaleFactor = Math.Max(1, queueScaleFactor);
        _dequeueWaitMs = Math.Max(100, dequeueWaitMs);

        lock (_sync)
        {
            for (var i = 0; i < MinThreads; i++)
            {
                CreateWorkerLocked("init");
            }
        }

        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "TP-Watchdog" };
        _monitorThread = new Thread(MonitorLoop) { IsBackground = true, Name = "TP-Monitor" };
        _watchdogThread.Start();
        _monitorThread.Start();
    }

    public int MinThreads { get; }
    public int MaxThreads { get; }

    public int CurrentThreadsCount
    {
        get
        {
            lock (_sync)
            {
                return _workers.Count;
            }
        }
    }

    public int QueueLength => _queue.Count;

    public void Enqueue(Action action, TimeSpan? maxQueueWait = null)
    {
        if (_disposeRequested)
        {
            throw new ObjectDisposedException(nameof(DynamicThreadPool));
        }

        var task = new TaskItem(action, maxQueueWait ?? TimeSpan.FromSeconds(5));
        _queue.Enqueue(task);
        Interlocked.Increment(ref _submittedTasks);
        RaiseEvent("task-enqueued", taskId: task.Id);

        lock (_sync)
        {
            ScaleUpIfNeededLocked();
        }
    }

    internal bool TryGetNextTask(WorkerThread worker, out TaskItem? task)
    {
        task = null;
        if (_disposeRequested)
        {
            return false;
        }

        if (!_queue.TryDequeue(_dequeueWaitMs, out task))
        {
            if (_disposeRequested)
            {
                return false;
            }

            TryScaleDown(worker);
            return true;
        }

        if (task is not null && task.HasTimedOut())
        {
            Interlocked.Increment(ref _timedOutInQueueTasks);
            RegisterTaskCompleted(success: false, wasTimeout: true);
            Logger.Warn($"Task #{task.Id} dropped due to queue timeout.");
            RaiseEvent("task-queue-timeout", taskId: task.Id);
            task = null;
            return true;
        }

        return true;
    }

    internal void RegisterTaskCompleted(bool success, bool wasTimeout)
    {
        if (!wasTimeout)
        {
            Interlocked.Increment(ref _completedTasks);
        }

        if (!success)
        {
            Interlocked.Increment(ref _failedTasks);
        }
    }

    private void TryScaleDown(WorkerThread worker)
    {
        if (worker.IsBusy)
        {
            return;
        }

        var idleFor = DateTime.UtcNow - worker.LastStateChangeUtc;
        if (idleFor < _idleTimeout)
        {
            return;
        }

        lock (_sync)
        {
            if (_workers.Count <= MinThreads || _queue.Count > 0)
            {
                return;
            }

            if (_workers.Remove(worker))
            {
                worker.Stop();
                Logger.Info($"Scale down: {worker.Name} stopped after idle timeout.");
                RaiseEvent("worker-scale-down", worker.Name);
            }
        }
    }

    private void ScaleUpIfNeededLocked()
    {
        if (_workers.Count >= MaxThreads)
        {
            return;
        }

        var busyCount = _workers.Count(w => w.IsBusy);
        var queueLength = _queue.Count;
        var thresholdByQueue = Math.Max(1, _workers.Count * _queueScaleFactor);
        var oldestWait = _queue.GetOldestWaitTime();
        var waitExceeded = oldestWait.HasValue && oldestWait.Value > _queueWaitScaleUpThreshold;

        if (busyCount >= _workers.Count || queueLength >= thresholdByQueue || waitExceeded)
        {
            CreateWorkerLocked("scale up");
        }
    }

    private void CreateWorkerLocked(string reason)
    {
        if (_workers.Count >= MaxThreads)
        {
            return;
        }

        var id = Interlocked.Increment(ref _workerIdCounter);
        var worker = new WorkerThread(this, id);
        _workers.Add(worker);
        worker.Start();
        Logger.Info($"Worker {worker.Name} created ({reason}).");
        RaiseEvent("worker-created", worker.Name, message: reason);
    }

    private void WatchdogLoop()
    {
        while (!_disposeRequested)
        {
            Thread.Sleep(500);
            if (_disposeRequested)
            {
                break;
            }

            _queue.RemoveTimedOutTasks();

            lock (_sync)
            {
                ScaleUpIfNeededLocked();

                for (var i = _workers.Count - 1; i >= 0; i--)
                {
                    var worker = _workers[i];

                    if (!worker.IsAlive)
                    {
                        Logger.Warn($"Watchdog: {worker.Name} is dead, replacing.");
                        _workers.RemoveAt(i);
                        Interlocked.Increment(ref _replacedWorkers);
                        RaiseEvent("worker-replaced-dead", worker.Name);
                        CreateWorkerLocked("dead replacement");
                        continue;
                    }

                    if (worker.IsBusy && DateTime.UtcNow - worker.LastStateChangeUtc > _stuckWorkerTimeout)
                    {
                        Logger.Warn($"Watchdog: {worker.Name} looks stuck, replacing.");
                        worker.MarkRetiredByWatchdog();
                        _workers.RemoveAt(i);
                        Interlocked.Increment(ref _replacedWorkers);
                        RaiseEvent("worker-replaced-stuck", worker.Name);
                        CreateWorkerLocked("stuck replacement");
                    }
                }

                while (_workers.Count < MinThreads)
                {
                    CreateWorkerLocked("restore min threads");
                }
            }
        }
    }

    private void MonitorLoop()
    {
        while (!_disposeRequested)
        {
            Thread.Sleep(1000);
            if (_disposeRequested)
            {
                break;
            }

            var snapshot = GetSnapshot();
            Logger.Info(
                $"Monitor: workers={snapshot.WorkersTotal} busy={snapshot.WorkersBusy} queue={snapshot.QueueLength} " +
                $"submitted={snapshot.SubmittedTasks} completed={snapshot.CompletedTasks} failed={snapshot.FailedTasks} " +
                $"timedout={snapshot.TimedOutInQueueTasks} replaced={snapshot.ReplacedWorkers}");
        }
    }

    public PoolSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new PoolSnapshot
            {
                WorkersTotal = _workers.Count,
                WorkersBusy = _workers.Count(w => w.IsBusy),
                QueueLength = _queue.Count,
                SubmittedTasks = Interlocked.Read(ref _submittedTasks),
                CompletedTasks = Interlocked.Read(ref _completedTasks),
                FailedTasks = Interlocked.Read(ref _failedTasks),
                TimedOutInQueueTasks = Interlocked.Read(ref _timedOutInQueueTasks),
                ReplacedWorkers = Interlocked.Read(ref _replacedWorkers)
            };
        }
    }

    public bool WaitForIdle(TimeSpan timeout, int pollIntervalMs = 100)
    {
        var deadline = DateTime.UtcNow + timeout;
        var interval = Math.Max(20, pollIntervalMs);

        while (DateTime.UtcNow <= deadline && !_disposeRequested)
        {
            var snapshot = GetSnapshot();
            var processed = snapshot.CompletedTasks + snapshot.TimedOutInQueueTasks;

            if (snapshot.QueueLength == 0 &&
                snapshot.WorkersBusy == 0 &&
                processed >= snapshot.SubmittedTasks)
            {
                return true;
            }

            Thread.Sleep(interval);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposeRequested)
        {
            return;
        }

        _disposeRequested = true;

        lock (_sync)
        {
            foreach (var worker in _workers)
            {
                worker.Stop();
            }
        }

        _queue.WakeAll();

        if (_watchdogThread.IsAlive)
        {
            _watchdogThread.Join(1000);
        }

        if (_monitorThread.IsAlive)
        {
            _monitorThread.Join(1000);
        }
        
        RaiseEvent("pool-disposed");
    }

    internal void NotifyWorkerState(string eventType, string workerName, long? taskId = null, string? message = null)
    {
        RaiseEvent(eventType, workerName, taskId, message);
    }

    private void RaiseEvent(string eventType, string? workerName = null, long? taskId = null, string? message = null)
    {
        var handler = PoolEvent;
        if (handler == null)
        {
            return;
        }

        handler(this, new ThreadPoolEventArgs
        {
            EventType = eventType,
            WorkerName = workerName,
            TaskId = taskId,
            Message = message,
            Snapshot = GetSnapshot()
        });
    }
}
