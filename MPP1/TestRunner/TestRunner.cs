using System.Reflection;
using TestLib.attributes;
using TestLib.exceptions;

namespace TestRunner;

public class TestRunner
{
    private int _passed;
    private int _failed;
    private int _ignored;
    private int _errors;

    public async Task RunAsync(Assembly assembly)
    {
        var testClasses = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);

        foreach (var testClass in testClasses)
        {
            await RunTestClass(testClass);
        }

        PrintSummary();
    }

    // ---------------- CLASS ----------------

    private async Task RunTestClass(Type testClass)
    {
        var classIgnore = testClass.GetCustomAttribute<IgnoreAttribute>();

        var methods = testClass.GetMethods();
        var testMethods = methods
            .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
            .ToList();

        // IGNORE CLASS
        if (classIgnore != null)
        {
            foreach (var method in testMethods)
            {
                var dataRows = method.GetCustomAttributes<DataRowAttribute>().ToList();

                if (dataRows.Any())
                {
                    foreach (var row in dataRows)
                    {
                        _ignored++;
                        PrintIgnore(
                            FormatTestName(method, row.Values),
                            classIgnore.Message ?? "Class ignored"
                        );
                    }
                }
                else
                {
                    _ignored++;
                    PrintIgnore(method.Name, classIgnore.Message ?? "Class ignored");
                }
            }

            return;
        }

        var classInit = methods.FirstOrDefault(m => m.GetCustomAttribute<ClassInitializeAttribute>() != null);
        var classCleanup = methods.FirstOrDefault(m => m.GetCustomAttribute<ClassCleanupAttribute>() != null);

        object? instance = null;

        try
        {
            instance = CreateInstance(testClass);

            if (classInit != null)
                await Invoke(instance, classInit);
        }
        catch (TestConfigurationException ex)
        {
            _errors++;
            PrintError(testClass.Name, ex.Message);
            return;
        }

        foreach (var method in testMethods)
        {
            await RunTestMethod(testClass, method);
        }

        try
        {
            if (classCleanup != null && instance != null)
                await Invoke(instance, classCleanup);
        }
        catch (Exception ex)
        {
            _errors++;
            PrintError(testClass.Name, ex.InnerException?.Message ?? ex.Message);
        }
    }

    // ---------------- METHOD EXECUTION ----------------

    private async Task RunTestMethod(Type testClass, MethodInfo method)
    {
        var methodIgnore = method.GetCustomAttribute<IgnoreAttribute>();

        // IGNORE METHOD
        if (methodIgnore != null)
        {
            var dataRows = method.GetCustomAttributes<DataRowAttribute>().ToList();

            if (dataRows.Any())
            {
                foreach (var row in dataRows)
                {
                    _ignored++;
                    PrintIgnore(
                        FormatTestName(method, row.Values),
                        methodIgnore.Message ?? "Method ignored"
                    );
                }
            }
            else
            {
                _ignored++;
                PrintIgnore(method.Name, methodIgnore.Message ?? "Method ignored");
            }

            return;
        }

        try
        {
            ValidateMethodSignature(method);
        }
        catch (TestConfigurationException ex)
        {
            _errors++;
            PrintError(method.Name, ex.Message);
            return;
        }

        var dataRowsList = method.GetCustomAttributes<DataRowAttribute>().ToList();

        if (!dataRowsList.Any())
        {
            await RunSingle(testClass, method, null);
            return;
        }

        foreach (var row in dataRowsList)
        {
            // IGNORE DATAROW
            if (!string.IsNullOrWhiteSpace(row.IgnoreMessage))
            {
                _ignored++;
                PrintIgnore(
                    FormatTestName(method, row.Values),
                    row.IgnoreMessage
                );
                continue;
            }

            try
            {
                ValidateParameters(method, row.Values);
                await RunSingle(testClass, method, row.Values);
            }
            catch (TestConfigurationException ex)
            {
                _errors++;
                PrintError(
                    FormatTestName(method, row.Values),
                    ex.Message
                );
            }
        }
    }

    // ---------------- SINGLE TEST ----------------

    private async Task RunSingle(Type testClass, MethodInfo method, object[]? parameters)
    {
        object instance;

        try
        {
            instance = CreateInstance(testClass);
        }
        catch (TestConfigurationException ex)
        {
            _errors++;
            PrintError(method.Name, ex.Message);
            return;
        }

        var methods = testClass.GetMethods();

        var setup = methods.FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
        var teardown = methods.FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);

        try
        {
            if (setup != null)
                await Invoke(instance, setup);

            var result = method.Invoke(instance, parameters);

            if (result is Task task)
                await task;

            _passed++;
            PrintSuccess(FormatTestName(method, parameters));
        }
        catch (TestIgnoredException ex)
        {
            _ignored++;
            PrintIgnore(FormatTestName(method, parameters), ex.Message);
        }
        catch (TestFailedException ex)
        {
            _failed++;
            PrintFail(FormatTestName(method, parameters), ex.Message);
        }
        catch (TestConfigurationException ex)
        {
            _errors++;
            PrintError(FormatTestName(method, parameters), ex.Message);
        }
        catch (Exception ex)
        {
            _errors++;
            PrintError(
                FormatTestName(method, parameters),
                ex.InnerException?.Message ?? ex.Message
            );
        }
        finally
        {
            try
            {
                if (teardown != null)
                    await Invoke(instance, teardown);
            }
            catch (Exception ex)
            {
                _errors++;
                PrintError(method.Name, $"TearDown failed: {ex.Message}");
            }
        }
    }

    // ---------------- VALIDATION ----------------

    private void ValidateMethodSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) &&
            method.ReturnType != typeof(Task))
        {
            throw new InvalidTestSignatureException(
                $"Method {method.Name} must return void or Task");
        }
    }

    private void ValidateParameters(MethodInfo method, object[] values)
    {
        var parameters = method.GetParameters();

        if (parameters.Length != values.Length)
        {
            throw new InvalidTestDataException(
                $"Parameter count mismatch. Expected {parameters.Length}, got {values.Length}");
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            if (values[i] == null) continue;

            if (!parameters[i].ParameterType.IsAssignableFrom(values[i].GetType()))
            {
                throw new InvalidTestDataException(
                    $"Parameter type mismatch at index {i}");
            }
        }
    }

    // ---------------- UTIL ----------------

    private object CreateInstance(Type type)
    {
        try
        {
            return Activator.CreateInstance(type)
                ?? throw new Exception("Instance is null");
        }
        catch (Exception ex)
        {
            throw new TestInstantiationException(
                $"Failed to create instance of {type.Name}", ex);
        }
    }

    private async Task Invoke(object instance, MethodInfo method)
    {
        var result = method.Invoke(instance, null);

        if (result is Task task)
            await task;
    }

    private string FormatTestName(MethodInfo method, object[]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
            return method.Name;

        var args = string.Join(", ", parameters.Select(p => p?.ToString() ?? "null"));
        return $"{method.Name}({args})";
    }

    // ---------------- OUTPUT ----------------

    private void PrintSuccess(string name)
        => Console.WriteLine($"PASS: {name}");

    private void PrintFail(string name, string message)
        => Console.WriteLine($"FAIL: {name} -> {message}");

    private void PrintIgnore(string name, string message)
        => Console.WriteLine($"SKIP: {name} -> {message}");

    private void PrintError(string name, string message)
        => Console.WriteLine($"ERROR: {name} -> {message}");

    private void PrintSummary()
    {
        Console.WriteLine("\nSUMMARY");
        Console.WriteLine($"Passed:  {_passed}");
        Console.WriteLine($"Failed:  {_failed}");
        Console.WriteLine($"Ignored: {_ignored}");
        Console.WriteLine($"Errors:  {_errors}");
    }
}