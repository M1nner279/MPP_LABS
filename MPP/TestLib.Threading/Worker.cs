namespace TestLib.Threading;

internal class Worker
{
private readonly CustomThreadPool _pool;
    private readonly Thread _thread;
    private bool _shouldExit = false;

    // Свойства для мониторинга и Watchdog
    public string Name => _thread.Name ?? "Unnamed";
    public bool IsBusy { get; private set; }
    public DateTime LastActivity { get; private set; }
    public ThreadState State => _thread.ThreadState;

    public Worker(CustomThreadPool pool, int id)
    {
        _pool = pool;
        _thread = new Thread(WorkLoop)
        {
            IsBackground = true, // Чтобы поток не блокировал выход из приложения
            Name = $"CustomWorker-{id}"
        };
        LastActivity = DateTime.Now;
    }

    public void Start() => _thread.Start();

    // Метод для принудительной остановки (используется при Dispose пула)
    public void Stop()
    {
        _shouldExit = true;
    }

    private void WorkLoop()
    {
        _pool.Log($"[THREAD] {Name} started.");

        while (!_shouldExit)
        {
            Action? task = null;

            // --- КРИТИЧЕСКАЯ СЕКЦИЯ: Ожидание и получение задачи ---
            lock (_pool.SyncLock)
            {
                // Пока очередь пуста, ждем сигнала или таймаута
                while (!_pool.HasTasks())
                {
                    if (_shouldExit || _pool.IsDisposing) return;

                    // Пытаемся уснуть и ждем Pulse от метода Enqueue
                    // Используем таймаут для реализации "адаптивного сжатия"
                    bool signaled = Monitor.Wait(_pool.SyncLock, _pool.IdleTimeout);

                    if (!signaled)
                    {
                        // Если вышли по таймауту (Monitor.Wait вернул false)
                        // Проверяем: можно ли завершить этот поток (сжатие пула)
                        if (_pool.CurrentThreadsCount > _pool.MinThreads)
                        {
                            _pool.RemoveWorker(this); // Удаляем себя из списка
                            _pool.Log($"[SCALE-DOWN] {Name} terminated due to idle timeout.");
                            return; // Выход из метода завершает системный поток
                        }
                    }
                }

                // Если мы здесь, значит в очереди что-то есть
                if (_pool.HasTasks())
                {
                    task = _pool.TryDequeueTask();
                    IsBusy = true;
                    LastActivity = DateTime.Now; // Обновляем "сердцебиение" для Watchdog
                }
            }

            // --- ВЫПОЛНЕНИЕ ЗАДАЧИ ---
            if (task != null)
            {
                try
                {
                    // Выполняем тест (Action)
                    task();
                }
                catch (Exception ex)
                {
                    // ОТКАЗОУСТОЙЧИВОСТЬ: ошибка в тесте не должна убивать воркер
                    _pool.Log($"[ERROR] {Name} task failed: {ex.Message}");
                }
                finally
                {
                    // Возвращаемся в состояние готовности
                    IsBusy = false;
                    LastActivity = DateTime.Now;
                    
                    // Сообщаем мониторингу, что мы закончили
                    _pool.OnTaskCompleted(); 
                }
            }
        }
    }
}