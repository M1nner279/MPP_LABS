using ThreadPoolModule.Logging;

namespace ThreadPoolModule.ThreadPool;

internal sealed class WorkerThread
{
    private readonly DynamicThreadPool _pool;
    private readonly Thread _thread;
    private volatile bool _shouldStop;
    private volatile bool _retiredByWatchdog;
    private Action? _currentTask;

    public WorkerThread(DynamicThreadPool pool, int id)
    {
        _pool = pool;
        _thread = new Thread(WorkLoop)
        {
            IsBackground = true,
            Name = $"TP-Worker-{id}"
        };
        LastStateChangeUtc = DateTime.UtcNow;
    }

    public string Name => _thread.Name ?? "TP-Worker";
    public bool IsBusy { get; private set; }
    public DateTime LastStateChangeUtc { get; private set; }
    public ThreadState State => _thread.ThreadState;
    public bool IsAlive => _thread.IsAlive;

    public void Start() => _thread.Start();

    public void Stop()
    {
        _shouldStop = true;
    }

    public void MarkRetiredByWatchdog()
    {
        _retiredByWatchdog = true;
        _shouldStop = true;
    }

    private void WorkLoop()
    {
        Logger.Info($"{Name} started.");

        while (!_shouldStop)
        {
            if (!_pool.TryGetNextTask(this, out var task))
            {
                if (_shouldStop)
                {
                    break;
                }

                continue;
            }

            if (task is null)
            {
                continue;
            }

            IsBusy = true;
            LastStateChangeUtc = DateTime.UtcNow;
            _currentTask = task.Work;

            try
            {
                task.Work();
                _pool.RegisterTaskCompleted(success: true, wasTimeout: false);
            }
            catch (Exception ex)
            {
                _pool.RegisterTaskCompleted(success: false, wasTimeout: false);
                Logger.Error($"{Name} task #{task.Id} failed: {ex.Message}");
            }
            finally
            {
                _currentTask = null;
                IsBusy = false;
                LastStateChangeUtc = DateTime.UtcNow;
            }
        }

        if (_retiredByWatchdog)
        {
            Logger.Warn($"{Name} retired by watchdog.");
        }
        else
        {
            Logger.Info($"{Name} stopped.");
        }
    }
}
