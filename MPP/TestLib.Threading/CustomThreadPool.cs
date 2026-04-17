namespace TestLib.Threading;

public class CustomThreadPool : IDisposable
{
    private readonly Queue<Action> _taskQueue = new();
    private readonly List<Worker> _workers = new();
    private readonly object _syncLock = new();
    
    internal object SyncLock => _syncLock;
    internal int IdleTimeout => _idleTimeout;
    internal bool IsDisposing => _isDisposing;
    internal Action? TryDequeueTask()
    {
        return _taskQueue.Count > 0 ? _taskQueue.Dequeue() : null;
    }
    internal bool HasTasks() => _taskQueue.Count > 0;
    
    // Настройки
    public int MinThreads { get; }
    public int MaxThreads { get; }
    private readonly int _idleTimeout;
    private readonly int _stuckTimeout;

    private bool _isDisposing;
    private readonly Thread _watchdogThread;

    // Статистика для мониторинга
    public int CurrentThreadsCount => _workers.Count;
    public int QueueCount { get { lock (_syncLock) return _taskQueue.Count; } }
    public int ActiveTasksCount { get { lock (_syncLock) return _workers.Count(w => w.IsBusy); } }

    public CustomThreadPool(int minThreads, int maxThreads, int idleTimeoutMs = 5000, int stuckTimeoutMs = 10000)
    {
        MinThreads = minThreads;
        MaxThreads = maxThreads;
        _idleTimeout = idleTimeoutMs;
        _stuckTimeout = stuckTimeoutMs;

        // 1. Инициализация минимального набора потоков
        lock (_syncLock)
        {
            for (int i = 0; i < MinThreads; i++)
            {
                CreateWorker();
            }
        }

        // 2. Запуск Watchdog (мониторинг зависших потоков)
        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "PoolWatchdog" };
        _watchdogThread.Start();
        
        Log($"[POOL] Initialized with {MinThreads} min, {MaxThreads} max threads.");
    }

    // --- ОСНОВНОЙ МЕТОД: Подача задачи ---
    public void Enqueue(Action task)
    {
        lock (_syncLock)
        {
            if (_isDisposing) return;

            _taskQueue.Enqueue(task);
            Log($"[QUEUE] Task added. Total in queue: {_taskQueue.Count}");

            // Динамическое масштабирование (Scale Up)
            // Если все воркеры заняты и мы еще не достигли лимита MaxThreads
            if (ActiveTasksCount >= _workers.Count && _workers.Count < MaxThreads)
            {
                Log($"[SCALE-UP] All threads busy. Creating new worker...");
                CreateWorker();
            }

            // Будим один из спящих потоков
            Monitor.Pulse(_syncLock);
        }
    }

    private void CreateWorker()
    {
        // Создание и запуск нового воркера (внутри лока)
        var worker = new Worker(this, _workers.Count + 1);
        _workers.Add(worker);
        worker.Start();
    }

    internal void RemoveWorker(Worker worker)
    {
        lock (_syncLock)
        {
            _workers.Remove(worker);
        }
    }

    // --- МЕХАНИЗМ ЗАМЕНЫ ЗАВИСШИХ ПОТОКОВ (Watchdog) ---
    internal void WatchdogLoop()
    {
        while (!_isDisposing)
        {
            Thread.Sleep(500); // Проверка каждые 0.5 секунды

            lock (_syncLock)
            {
                for (int i = _workers.Count - 1; i >= 0; i--)
                {
                    var worker = _workers[i];
                    
                    // Если воркер занят и время его последней активности превысило лимит
                    if (worker.IsBusy && (DateTime.Now - worker.LastActivity).TotalMilliseconds > _stuckTimeout)
                    {
                        Log($"[WATCHDOG] Detected STUCK thread: {worker.Name}. Abandoning and replacing...");
                        
                        // Мы не можем безопасно убить поток (Abort запрещен), 
                        // поэтому мы исключаем его из пула и создаем новый на замену.
                        worker.Stop(); 
                        _workers.RemoveAt(i);
                        
                        CreateWorker(); // Восполняем мощность пула
                    }
                }
            }
        }
    }

    // --- МОНИТОРИНГ ---
    public void Log(string message)
    {
        // Потокобезопасный вывод в консоль с меткой времени
        lock (Console.Out)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message} | Threads: {CurrentThreadsCount} (Active: {ActiveTasksCount}) | Queue: {QueueCount}");
        }
    }

    public void OnTaskCompleted()
    {
        // Можно использовать для сбора метрик
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _isDisposing = true;
            foreach (var worker in _workers) worker.Stop();
            Monitor.PulseAll(_syncLock);
        }
        Log("[POOL] Shutting down...");
    }
}