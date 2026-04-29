using System.Diagnostics;
using System.Reflection;
using ThreadPoolModule.ThreadPool;

var consoleSync = new object();

string assemblyPath = "../../../../SimpleServer.Tests/bin/Debug/net9.0/SimpleServer.Tests.dll";

if (!File.Exists(assemblyPath))
{
    WriteLineSafe(consoleSync, $"Assembly not found: {assemblyPath}");
    return;
}

var assembly = Assembly.LoadFrom(assemblyPath);

var runner = new TestRunner.TestRunner(2, 4);

WriteLineSafe(consoleSync, "--- SEQUENTIAL RUN ---");
await runner.RunAsync(assembly, parallel: false);

WriteLineSafe(consoleSync, "\n--- PARALLEL RUN ---");
await runner.RunAsync(assembly, parallel: true);

WriteLineSafe(consoleSync, "\n--- LAB4: FILTER + DYNAMIC DATA + EVENTS ---");
await RunLab4FeatureDemoAsync(assembly, consoleSync);

// WriteLineSafe(consoleSync, "\n--- UNEVEN LOAD SCENARIO (DYNAMIC POOL) ---");
// RunUnevenLoadScenarioAsync(assembly, consoleSync);
//
// WriteLineSafe(consoleSync, "\n--- SCALE-UP / SCALE-DOWN DEMO ---");
// RunScaleDownVisibilityDemo(consoleSync);

static void RunUnevenLoadScenarioAsync(Assembly assembly, object consoleSync)
{
    // Задания - это отдельные прогоны всего набора тестов 1 раз.
    // Так TestRunner не меняется по контракту: один запуск = один прогон сборки.
    var totalLaunches = 18;
    using var completion = new CountdownEvent(totalLaunches);

    using var pool = new DynamicThreadPool(
        minThreads: 2,
        maxThreads: 8,
        idleTimeout: TimeSpan.FromSeconds(3),
        stuckWorkerTimeout: TimeSpan.FromSeconds(25),
        queueWaitScaleUpThreshold: TimeSpan.FromMilliseconds(700),
        queueScaleFactor: 2,
        dequeueWaitMs: 250);

    var sw = Stopwatch.StartNew();

    // Фаза 1: одиночные подачи
    for (var i = 0; i < 4; i++)
    {
        EnqueueFullTestRun(pool, assembly, i + 1, completion, consoleSync);
        Thread.Sleep(1200);
    }

    // Фаза 2: период бездействия
    WriteLineSafe(consoleSync, "[LOAD] Idle window...");
    Thread.Sleep(2500);

    // Фаза 3: пиковая нагрузка (бурст)
    WriteLineSafe(consoleSync, "[LOAD] Burst window...");
    for (var i = 4; i < 14; i++)
    {
        EnqueueFullTestRun(pool, assembly, i + 1, completion, consoleSync);
        Thread.Sleep(30);
    }

    // Фаза 4: снова бездействие
    WriteLineSafe(consoleSync, "[LOAD] Idle window...");
    Thread.Sleep(3800);

    // Фаза 5: смешанная нагрузка (единичные + мини-бурсты)
    for (var i = 14; i < totalLaunches; i++)
    {
        EnqueueFullTestRun(pool, assembly, i + 1, completion,  consoleSync);
        Thread.Sleep(i % 2 == 0 ? 120 : 650);
    }

    while (!completion.Wait(500))
    {
        var snapshot = pool.GetSnapshot();
        var done = totalLaunches - (int)completion.CurrentCount;
        WriteLineSafe(consoleSync,
            $"[LOAD-MONITOR] done={done}/{totalLaunches}, " +
            $"workers={snapshot.WorkersTotal}, busy={snapshot.WorkersBusy}, queue={snapshot.QueueLength}, replaced={snapshot.ReplacedWorkers}");
    }

    sw.Stop();
    var final = pool.GetSnapshot();
    WriteLineSafe(consoleSync,
        $"[LOAD-RESULT] launches={totalLaunches}, elapsed={sw.ElapsedMilliseconds}ms, " +
        $"workers={final.WorkersTotal}, completed={final.CompletedTasks}, failed={final.FailedTasks}, timedout={final.TimedOutInQueueTasks}");
}

static async Task RunLab4FeatureDemoAsync(Assembly assembly, object consoleSync)
{
    // Фильтрация делегатом: запускаем только smoke + dynamic data + high priority.
    Func<TestRunner.TestRunner.TestCaseMetadata, bool> filter = metadata =>
    {
        if (metadata.Categories.Contains("DynamicData"))
        {
            return true;
        }

        return metadata.Categories.Contains("Smoke") || (metadata.Priority.HasValue && metadata.Priority.Value <= 1);
    };

    WriteLineSafe(consoleSync, "[LAB4] Filtered run started (Smoke/DynamicData/Priority<=1)...");
    var filteredRunner = new TestRunner.TestRunner(2, 4);
    await filteredRunner.RunAsync(assembly, parallel: true, filter: filter);
    WriteLineSafe(consoleSync, "[LAB4] Filtered run finished.");

    // Демонстрация событий пула.
    using var pool = new DynamicThreadPool(
        minThreads: 2,
        maxThreads: 4,
        idleTimeout: TimeSpan.FromMilliseconds(900),
        stuckWorkerTimeout: TimeSpan.FromSeconds(6),
        queueWaitScaleUpThreshold: TimeSpan.FromMilliseconds(250));

    pool.PoolEvent += (_, evt) =>
    {
        // Умеренный шум: выводим только ключевые события жизненного цикла.
        if (evt.EventType is "worker-created" or "worker-started" or "worker-scale-down" or "worker-replaced-stuck" or "task-enqueued")
        {
            WriteLineSafe(
                consoleSync,
                $"[POOL-EVENT] {evt.EventType} worker={evt.WorkerName ?? "-"} task={evt.TaskId?.ToString() ?? "-"} " +
                $"workers={evt.Snapshot.WorkersTotal} busy={evt.Snapshot.WorkersBusy} queue={evt.Snapshot.QueueLength}");
        }
    };

    using var done = new CountdownEvent(8);
    for (var i = 0; i < 8; i++)
    {
        var idx = i;
        pool.Enqueue(() =>
        {
            Thread.Sleep(200 + (idx % 3) * 80);
            done.Signal();
        });
    }

    done.Wait();
    Thread.Sleep(1500); // Даем времени на scale-down и события.
    WriteLineSafe(consoleSync, "[LAB4] Pool events demo finished.");
}

static void EnqueueFullTestRun(
    DynamicThreadPool pool,
    Assembly assembly,
    int launchId,
    CountdownEvent completion,
    object consoleSync)
{
    pool.Enqueue(() =>
    {
        try
        {
            WriteLineSafe(consoleSync, $"[LOAD] Launch #{launchId} started");
            var localRunner = new TestRunner.TestRunner(2, 4);
            localRunner.RunAsync(assembly, parallel: true).GetAwaiter().GetResult();
            WriteLineSafe(consoleSync, $"[LOAD] Launch #{launchId} finished");
        }
        catch (Exception ex)
        {
            WriteLineSafe(consoleSync, $"[LOAD] Launch #{launchId} failed: {ex.Message}");
        }
        finally
        {
            completion.Signal();
        }
    }, maxQueueWait: TimeSpan.FromSeconds(20));
}

static void RunScaleDownVisibilityDemo(object consoleSync)
{
    using var pool = new DynamicThreadPool(
        minThreads: 2,
        maxThreads: 8,
        idleTimeout: TimeSpan.FromMilliseconds(1500),
        stuckWorkerTimeout: TimeSpan.FromSeconds(15),
        queueWaitScaleUpThreshold: TimeSpan.FromMilliseconds(300),
        queueScaleFactor: 1,
        dequeueWaitMs: 150);

    const int jobs = 28;
    using var done = new CountdownEvent(jobs);

    WriteLineSafe(consoleSync, "[DEMO] Warm-up...");
    Thread.Sleep(500);

    WriteLineSafe(consoleSync, "[DEMO] Burst submit to trigger scale-up...");
    for (var i = 0; i < jobs; i++)
    {
        var idx = i + 1;
        pool.Enqueue(() =>
        {
            try
            {
                Thread.Sleep(350 + (idx % 5) * 40);
            }
            finally
            {
                done.Signal();
            }
        }, maxQueueWait: TimeSpan.FromSeconds(5));
    }

    Thread.Sleep(1000);
    var peak = pool.GetSnapshot();
    WriteLineSafe(consoleSync, $"[DEMO] After burst: workers={peak.WorkersTotal}, busy={peak.WorkersBusy}, queue={peak.QueueLength}");

    while (!done.Wait(200))
    {
        var s = pool.GetSnapshot();
        WriteLineSafe(consoleSync, $"[DEMO] Processing... workers={s.WorkersTotal}, busy={s.WorkersBusy}, queue={s.QueueLength}");
    }

    var beforeIdle = pool.GetSnapshot();
    WriteLineSafe(consoleSync, $"[DEMO] Queue drained: workers={beforeIdle.WorkersTotal}, busy={beforeIdle.WorkersBusy}, queue={beforeIdle.QueueLength}");

    WriteLineSafe(consoleSync, "[DEMO] Long idle window (expect scale-down)...");
    Thread.Sleep(6000);

    var afterIdle = pool.GetSnapshot();
    WriteLineSafe(consoleSync, $"[DEMO] After idle: workers={afterIdle.WorkersTotal}, busy={afterIdle.WorkersBusy}, queue={afterIdle.QueueLength}");
    WriteLineSafe(consoleSync, $"[DEMO] Scale-down delta: {beforeIdle.WorkersTotal} -> {afterIdle.WorkersTotal}");
}

static void WriteLineSafe(object sync, string message)
{
    lock (sync)
    {
        Console.WriteLine(message);
    }
}