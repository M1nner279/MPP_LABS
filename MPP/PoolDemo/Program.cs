using System.Diagnostics;
using TestLib.Threading;

Console.WriteLine("=== CUSTOM THREAD POOL DEMONSTRATION ===");

// Настройки пула: 
// Мин: 2, Макс: 8, Таймаут простоя: 4 сек, Таймаут зависания: 6 сек
using var pool = new CustomThreadPool(
    minThreads: 2, 
    maxThreads: 8, 
    idleTimeoutMs: 4000, 
    stuckTimeoutMs: 6000
);

// Функция-помощник для имитации работы теста
Action CreateTask(int id, int durationMs) => () =>
{
    // Console.WriteLine($"[TASK] Task {id} started (Duration: {durationMs}ms)");
    Thread.Sleep(durationMs);
    // Console.WriteLine($"[TASK] Task {id} finished");
};

// --- СЦЕНАРИЙ 1: Единичные подачи ---
Console.WriteLine("\n>>> Scenario 1: Single submissions (Warm-up)");
for (int i = 1; i <= 3; i++)
{
    pool.Enqueue(CreateTask(i, 500));
    Thread.Sleep(300); // Подаем задачи медленно
}

Thread.Sleep(2000); // Ждем завершения

// --- СЦЕНАРИЙ 2: Пиковая нагрузка (Масштабирование вверх) ---
Console.WriteLine("\n>>> Scenario 2: Peak load (Scaling Up)");
Console.WriteLine("Submitting 20 tasks at once...");
for (int i = 10; i <= 30; i++)
{
    pool.Enqueue(CreateTask(i, 1000));
}

// Ждем, пока очередь опустеет
while (pool.QueueCount > 0) Thread.Sleep(500);

// --- СЦЕНАРИЙ 3: Интервал бездействия (Адаптивное сжатие) ---
Console.WriteLine("\n>>> Scenario 3: Idle period (Scaling Down)");
Console.WriteLine("Waiting for 7 seconds to see threads terminate...");
Thread.Sleep(7000); 

// --- СЦЕНАРИЙ 4: Зависшие потоки (Watchdog) ---
Console.WriteLine("\n>>> Scenario 4: Stuck threads detection");
pool.Enqueue(() => {
    Console.WriteLine("!!! [STUCK] This task will sleep for 20 seconds (simulating a hang)");
    Thread.Sleep(20000);
});

// Пока один поток висит, подадим еще пару обычных задач
for (int i = 100; i <= 102; i++)
{
    pool.Enqueue(CreateTask(i, 500));
}

// Ждем срабатывания Watchdog (stuckTimeoutMs = 6000)
Thread.Sleep(8000);

Console.WriteLine("\n>>> Final Summary:");
Console.WriteLine($"Current Pool Size: {pool.CurrentThreadsCount}");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();