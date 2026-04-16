using System.Diagnostics;
using System.Reflection;
using TestLib.attributes;
using TestLib.exceptions;

namespace TestRunner;

public class TestRunner
{
    // Используем Interlocked для потокобезопасного инкремента счетчиков
    private int _passed;
    private int _failed;
    private int _ignored;
    private int _errors;

    private readonly int _maxParallelism;
    private readonly SemaphoreSlim _semaphore;
    private readonly Dictionary<Type, object> _sharedContexts = new();
    private static readonly object _consoleLock = new();

    public TestRunner(int maxDegreeOfParallelism = 4)
    {
        _maxParallelism = maxDegreeOfParallelism;
        _semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
    }

    public async Task RunAsync(Assembly assembly, bool parallel = true)
    {
        _passed = _failed = _ignored = _errors = 0;
        
        // Собираем все тестовые случаи из всех классов в один список
        var testCases = GetTestCases(assembly);

        SafePrint(() => Console.WriteLine($"Starting {(parallel ? "PARALLEL" : "SEQUENTIAL")} execution of {testCases.Count} tests...\n"));

        var sw = Stopwatch.StartNew();

        if (parallel)
        {
            // ПАРАЛЛЕЛЬНЫЙ ЗАПУСК
            var tasks = testCases.Select(tc => Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    await ExecuteTestCase(tc);
                }
                finally
                {
                    _semaphore.Release();
                }
            }));
            await Task.WhenAll(tasks);
        }
        else
        {
            // ПОСЛЕДОВАТЕЛЬНЫЙ ЗАПУСК
            foreach (var tc in testCases)
            {
                await ExecuteTestCase(tc);
            }
        }

        sw.Stop();
        
        await CleanupSharedContexts();
        PrintSummary(sw.ElapsedMilliseconds);
    }
    
    private record TestCase(Type ClassType, MethodInfo Method, object[]? Parameters, string? DataRowIgnoreMessage);

    private List<TestCase> GetTestCases(Assembly assembly)
    {
        var list = new List<TestCase>();
        var classes = assembly.GetTypes().Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);

        foreach (var @class in classes)
        {
            if (@class.GetCustomAttribute<IgnoreAttribute>() != null) continue;

            var methods = @class.GetMethods().Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null);
            foreach (var method in methods)
            {
                var dataRows = method.GetCustomAttributes<DataRowAttribute>().ToList();
                if (dataRows.Any())
                {
                    foreach (var row in dataRows)
                        list.Add(new TestCase(@class, method, row.Values, row.IgnoreMessage));
                }
                else
                {
                    list.Add(new TestCase(@class, method, null, null));
                }
            }
        }
        return list;
    }

    private async Task ExecuteTestCase(TestCase tc)
    {
        // 1. Проверка Ignore
        var methodIgnore = tc.Method.GetCustomAttribute<IgnoreAttribute>();
        if (methodIgnore != null || tc.DataRowIgnoreMessage != null)
        {
            Interlocked.Increment(ref _ignored);
            SafePrint(() => PrintIgnore(FormatTestName(tc.Method, tc.Parameters), methodIgnore?.Message ?? tc.DataRowIgnoreMessage ?? "Ignored"));
            return;
        }

        // 2. Валидация
        try { ValidateMethodSignature(tc.Method); }
        catch (Exception ex) { Interlocked.Increment(ref _errors); SafePrint(() => PrintError(tc.Method.Name, ex.Message)); return; }

        // 3. Запуск с учетом Timeout
        var timeoutAttr = tc.Method.GetCustomAttribute<TimeoutAttribute>();
        int timeoutMs = timeoutAttr?.Milliseconds ?? -1;

        using var cts = new CancellationTokenSource();
        var executionTask = RunSingle(tc);

        if (timeoutMs > 0)
        {
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);
            var finishedTask = await Task.WhenAny(executionTask, timeoutTask);

            if (finishedTask == timeoutTask)
            {
                Interlocked.Increment(ref _failed);
                SafePrint(() => PrintFail(FormatTestName(tc.Method, tc.Parameters), $"Timed out after {timeoutMs}ms"));
                return;
            }

            cts.Cancel();
        }

        await executionTask;
    }

    private async Task RunSingle(TestCase tc)
{
    object? instance = null;
    try
    {
        instance = CreateInstance(tc.ClassType);
        var methods = tc.ClassType.GetMethods();
        var setup = methods.FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
        var teardown = methods.FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);

        if (setup != null) await Invoke(instance, setup);

        var result = tc.Method.Invoke(instance, tc.Parameters);
        if (result is Task t) await t;

        if (teardown != null) await Invoke(instance, teardown);

        Interlocked.Increment(ref _passed);
        SafePrint(() => PrintSuccess(FormatTestName(tc.Method, tc.Parameters)));
    }
    
    catch (TestFailedException fe) 
    {
        Interlocked.Increment(ref _failed);
        SafePrint(() => PrintFail(FormatTestName(tc.Method, tc.Parameters), fe.Message));
    }

    catch (TestIgnoredException ie)
    {
        Interlocked.Increment(ref _ignored);
        SafePrint(() => PrintIgnore(FormatTestName(tc.Method, tc.Parameters), ie.Message));
    }

    catch (TargetInvocationException ex) when (ex.InnerException is TestFailedException fe)
    {
        Interlocked.Increment(ref _failed);
        SafePrint(() => PrintFail(FormatTestName(tc.Method, tc.Parameters), fe.Message));
    }
    catch (TargetInvocationException ex) when (ex.InnerException is TestIgnoredException ie)
    {
        Interlocked.Increment(ref _ignored);
        SafePrint(() => PrintIgnore(FormatTestName(tc.Method, tc.Parameters), ie.Message));
    }

    catch (Exception ex)
    {
        Interlocked.Increment(ref _errors);
        SafePrint(() => PrintError(FormatTestName(tc.Method, tc.Parameters), ex.InnerException?.Message ?? ex.Message));
    }
}

    // ---------------- СЛУЖЕБНЫЕ МЕТОДЫ ----------------

    private object CreateInstance(Type type)
    {
        var sharedContextAttr = type.GetCustomAttribute<SharedContextAttribute>();
        if (sharedContextAttr != null)
        {
            var context = GetOrCreateSharedContext(sharedContextAttr.ContextType);
            var ctor = type.GetConstructor(new[] { sharedContextAttr.ContextType });
            if (ctor != null) return ctor.Invoke(new[] { context });
        }
        return Activator.CreateInstance(type) ?? throw new Exception("Null instance");
    }

    private object GetOrCreateSharedContext(Type contextType)
    {
        lock (_sharedContexts) // Синхронизация создания контекста
        {
            if (_sharedContexts.TryGetValue(contextType, out var ctx)) return ctx;
            
            var instance = Activator.CreateInstance(contextType)!;
            var init = contextType.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SharedContextInitializeAttribute>() != null);
            if (init != null) Invoke(instance, init).GetAwaiter().GetResult();
            
            _sharedContexts[contextType] = instance;
            return instance;
        }
    }

    private async Task CleanupSharedContexts()
    {
        foreach (var ctx in _sharedContexts.Values)
        {
            var cleanup = ctx.GetType().GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SharedContextCleanUpAttribute>() != null);
            if (cleanup != null) await Invoke(ctx, cleanup);
        }
    }

    private async Task Invoke(object instance, MethodInfo method)
    {
        var result = method.Invoke(instance, null);
        if (result is Task t) await t;
    }

    private void ValidateMethodSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) && method.ReturnType != typeof(Task))
            throw new InvalidTestSignatureException("Must return void or Task");
    }

    private string FormatTestName(MethodInfo method, object[]? parameters) =>
        parameters == null ? method.Name : $"{method.Name}({string.Join(", ", parameters)})";

    // ---------------- ВЫВОД ----------------
    private void SafePrint(Action action)
    {
        lock (_consoleLock) action();
    }
    
    private void PrintSuccess(string name)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"PASS: {name}"); 
        Console.ResetColor();
    }

    private void PrintFail(string name, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red; 
        Console.WriteLine($"FAIL: {name} -> {msg}"); 
        Console.ResetColor();
    }

    private void PrintIgnore(string name, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow; 
        Console.WriteLine($"SKIP: {name} -> {msg}"); 
        Console.ResetColor();
    }

    private void PrintError(string name, string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed; 
        Console.WriteLine($"ERROR: {name} -> {msg}"); 
        Console.ResetColor();
    }

    private void PrintSummary(long ms)
    {
        SafePrint(() => {
            Console.WriteLine($"\nSUMMARY (Time: {ms}ms)");
            Console.WriteLine($"Passed: {_passed} | Failed: {_failed} | Ignored: {_ignored} | Errors: {_errors}");
        });
    }
}