using System.Diagnostics;
using System.Reflection;
using TestLib.attributes;
using TestLib.exceptions;
using ThreadPoolModule.ThreadPool;

namespace TestRunner;

public class TestRunner
{
    private static readonly object _consoleLock = new();

    private readonly int _maxThreads;
    private readonly int _minThreads;

    private readonly Dictionary<Type, object> _sharedContexts = new();
    private int _errors;
    private int _failed;
    private int _ignored;
    private int _passed;

    private DynamicThreadPool? _pool;

    // Конструктор по умолчанию
    public TestRunner() : this(2, 4) { }
    
    public TestRunner(int minThreads, int maxThreads)
    {
        _minThreads = minThreads;
        _maxThreads = maxThreads;
    }

    public async Task RunAsync(Assembly assembly, bool parallel = true)
    {
        _passed = _failed = _ignored = _errors = 0;

        await InitializeRequiredContexts(assembly);

        var testCases = GetTestCases(assembly);

        SafePrint(() =>
            Console.WriteLine($"\nStarting {(parallel ? "CUSTOM POOL" : "SEQUENTIAL")} execution of {testCases.Count} tests..."));

        var sw = Stopwatch.StartNew();

        if (parallel)
        {
            _pool = new DynamicThreadPool(
                minThreads: _minThreads,
                maxThreads: _maxThreads,
                idleTimeout: TimeSpan.FromSeconds(4),
                stuckWorkerTimeout: TimeSpan.FromSeconds(10),
                queueWaitScaleUpThreshold: TimeSpan.FromMilliseconds(800));

            using var countdown = new CountdownEvent(testCases.Count);

            foreach (var tc in testCases)
            {
                await Task.Yield();

                _pool.Enqueue(() =>
                {
                    try
                    {
                        ExecuteTestCaseWithTimeout(tc);
                    }
                    finally
                    {
                        countdown.Signal();
                    }
                });
            }

            await WaitCountdownAsync(countdown);
            _pool.Dispose();
            _pool = null;
        }
        else
        {
            foreach (var tc in testCases)
            {
                ExecuteTestCaseWithTimeout(tc);
            }
        }

        sw.Stop();

        await CleanupSharedContexts();
        PrintSummary(sw.ElapsedMilliseconds);
    }

    private async Task InitializeRequiredContexts(Assembly assembly)
    {
        var contextTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
            .Select(t => t.GetCustomAttribute<SharedContextAttribute>()?.ContextType)
            .Where(type => type != null)
            .Distinct();

        foreach (var ctxType in contextTypes) 
            await GetOrCreateSharedContextAsync(ctxType!);
    }

    private async Task<object> GetOrCreateSharedContextAsync(Type contextType)
    {
        lock (_sharedContexts)
        {
            if (_sharedContexts.TryGetValue(contextType, out var ctx)) return ctx;
        }

        var instance = Activator.CreateInstance(contextType)!;
        var init = contextType.GetMethods()
            .FirstOrDefault(m => m.GetCustomAttribute<SharedContextInitializeAttribute>() != null);
        
        if (init != null) await Invoke(instance, init);

        lock (_sharedContexts)
        {
            _sharedContexts[contextType] = instance;
        }
        return instance;
    }

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
                    foreach (var row in dataRows)
                        list.Add(new TestCase(@class, method, row.Values, row.IgnoreMessage));
                else
                    list.Add(new TestCase(@class, method, null, null));
            }
        }
        return list;
    }

    private void ExecuteTestCaseWithTimeout(TestCase tc)
    {
        var methodIgnore = tc.Method.GetCustomAttribute<IgnoreAttribute>();
        if (methodIgnore != null || tc.DataRowIgnoreMessage != null)
        {
            Interlocked.Increment(ref _ignored);
            SafePrint(() => PrintIgnore(FormatTestName(tc.Method, tc.Parameters),
                methodIgnore?.Message ?? tc.DataRowIgnoreMessage ?? "Ignored"));
            return;
        }

        try
        {
            ValidateMethodSignature(tc.Method);
            ValidateParameters(tc.Method, tc.Parameters);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            SafePrint(() => PrintError(tc.Method.Name, ex.Message));
            return;
        }

        var timeoutMs = tc.Method.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds ?? 3000;
        Exception? thrown = null;

        var testThread = new Thread(() =>
        {
            try
            {
                RunSingle(tc).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        })
        {
            IsBackground = true,
            Name = $"TestCase-{tc.Method.Name}"
        };

        testThread.Start();

        if (!testThread.Join(timeoutMs))
        {
            TryForceStopTestThread(testThread, timeoutMs);
            Interlocked.Increment(ref _failed);
            SafePrint(() => PrintFail(FormatTestName(tc.Method, tc.Parameters), $"Timed out after {timeoutMs}ms (forced stop requested)"));
            return;
        }

        if (thrown != null)
        {
            Interlocked.Increment(ref _errors);
            SafePrint(() => PrintError(FormatTestName(tc.Method, tc.Parameters), thrown.Message));
        }
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
        catch (Exception ex)
        {
            var actualEx = ex is TargetInvocationException ? ex.InnerException : ex;

            if (actualEx is TestFailedException fe)
            {
                Interlocked.Increment(ref _failed);
                SafePrint(() => PrintFail(FormatTestName(tc.Method, tc.Parameters), fe.Message));
            }
            else if (actualEx is TestIgnoredException ie)
            {
                Interlocked.Increment(ref _ignored);
                SafePrint(() => PrintIgnore(FormatTestName(tc.Method, tc.Parameters), ie.Message));
            }
            else
            {
                Interlocked.Increment(ref _errors);
                SafePrint(() => PrintError(FormatTestName(tc.Method, tc.Parameters), actualEx?.Message ?? "Unknown Error"));
            }
        }
    }

    // ---------------- СЛУЖЕБНЫЕ МЕТОДЫ ----------------

    private object CreateInstance(Type type)
    {
        var sharedContextAttr = type.GetCustomAttribute<SharedContextAttribute>();
        if (sharedContextAttr != null)
        {
            if (_sharedContexts.TryGetValue(sharedContextAttr.ContextType, out var context))
            {
                var ctor = type.GetConstructor(new[] { sharedContextAttr.ContextType });
                if (ctor != null) return ctor.Invoke(new[] { context });
            }
        }
        return Activator.CreateInstance(type) ?? throw new Exception("Null instance");
    }

    private async Task CleanupSharedContexts()
    {
        foreach (var ctx in _sharedContexts.Values)
        {
            var cleanup = ctx.GetType().GetMethods()
                .FirstOrDefault(m => m.GetCustomAttribute<SharedContextCleanUpAttribute>() != null);
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

    private void ValidateParameters(MethodInfo method, object[]? values)
    {
        var parameters = method.GetParameters();
        int valuesCount = values?.Length ?? 0;

        if (parameters.Length != valuesCount)
        {
            throw new InvalidTestDataException($"Parameter count mismatch. Expected {parameters.Length}, got {valuesCount}");
        }
    }
    
    private string FormatTestName(MethodInfo method, object[]? parameters)
    {
        return parameters == null ? method.Name : $"{method.Name}({string.Join(", ", parameters)})";
    }

    private void SafePrint(Action action)
    {
        lock (_consoleLock) { action(); }
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
        SafePrint(() =>
        {
            Console.WriteLine($"\nSUMMARY (Time: {ms}ms)");
            Console.WriteLine($"Passed: {_passed} | Failed: {_failed} | Ignored: {_ignored} | Errors: {_errors}");
        });
    }

    private static Task WaitCountdownAsync(CountdownEvent countdown)
    {
        while (!countdown.Wait(100))
        {
            Thread.Yield();
        }

        return Task.CompletedTask;
    }

    private void TryForceStopTestThread(Thread testThread, int timeoutMs)
    {
        try
        {
            testThread.Interrupt();
            if (!testThread.Join(250))
            {
                SafePrint(() =>
                    Console.WriteLine($"WARN: Test thread '{testThread.Name}' did not stop after timeout={timeoutMs}ms."));
            }
        }
        catch (Exception ex)
        {
            SafePrint(() =>
                Console.WriteLine($"WARN: Failed to interrupt timed-out thread '{testThread.Name}': {ex.Message}"));
        }
    }

    private record TestCase(Type ClassType, MethodInfo Method, object[]? Parameters, string? DataRowIgnoreMessage);
}