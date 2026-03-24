﻿using System.Reflection;
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
        var sharedContext = await CreateSharedContext(assembly);

        var testClasses = DiscoverTestClasses(assembly);

        foreach (var testClass in testClasses)
        {
            await RunTestClass(testClass, sharedContext);
        }

        await DisposeSharedContext(sharedContext);

        PrintSummary();
    }

    // ---------------- DISCOVERY ----------------

    private IEnumerable<Type> DiscoverTestClasses(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);
    }

    // ---------------- SHARED CONTEXT ----------------

    private async Task<object?> CreateSharedContext(Assembly assembly)
    {
        var type = assembly.GetTypes()
            .FirstOrDefault(t => t.GetCustomAttribute<SharedContextAttribute>() != null);

        if (type == null)
            return null;

        var instance = Activator.CreateInstance(type);

        var init = type.GetMethods()
            .FirstOrDefault(m => m.GetCustomAttribute<SharedContextInitializeAttribute>() != null);

        if (init != null)
            await Invoke(instance, init);

        return instance;
    }

    private async Task DisposeSharedContext(object? instance)
    {
        if (instance == null) return;

        var cleanup = instance.GetType().GetMethods()
            .FirstOrDefault(m => m.GetCustomAttribute<SharedContextCleanupAttribute>() != null);

        if (cleanup != null)
            await Invoke(instance, cleanup);
    }

    // ---------------- CLASS EXECUTION ----------------

    private async Task RunTestClass(Type testClass, object? shared)
    {
        var methods = testClass.GetMethods();

        var classInit = methods.FirstOrDefault(m => m.GetCustomAttribute<ClassInitializeAttribute>() != null);
        var classCleanup = methods.FirstOrDefault(m => m.GetCustomAttribute<ClassCleanupAttribute>() != null);

        var instance = CreateInstance(testClass, shared);

        if (classInit != null)
            await Invoke(instance, classInit);

        var testMethods = methods
            .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null);

        foreach (var method in testMethods)
        {
            await RunTestMethod(testClass, method, shared);
        }

        if (classCleanup != null)
            await Invoke(instance, classCleanup);
    }

    // ---------------- METHOD EXECUTION ----------------

    private async Task RunTestMethod(Type testClass, MethodInfo method, object? shared)
    {
        var dataRows = method.GetCustomAttributes<DataRowAttribute>().ToList();

        if (!dataRows.Any())
        {
            await RunSingle(testClass, method, null, shared);
            return;
        }

        foreach (var row in dataRows)
        {
            if (!string.IsNullOrWhiteSpace(row.IgnoreMessage))
            {
                _ignored++;
                PrintIgnore(method.Name, row.IgnoreMessage);
                continue;
            }

            if (!ValidateParameters(method, row.Values, out string error))
            {
                _failed++;
                PrintFail(method.Name, error);
                continue;
            }

            await RunSingle(testClass, method, row.Values, shared);
        }
    }

    private async Task RunSingle(Type testClass, MethodInfo method, object[]? parameters, object? shared)
    {
        var instance = CreateInstance(testClass, shared);

        var methods = testClass.GetMethods();
        var setup = methods.FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
        var teardown = methods.FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);

        try
        {
            if (setup != null) 
            {
                await Invoke(instance, setup);
                Console.WriteLine("call");
            }

            await Invoke(instance, method, parameters);

            _passed++;
            PrintSuccess(method.Name);
        }
        catch (TestFailedException ex)
        {
            _failed++;
            PrintFail(method.Name, ex.Message);
        }
        catch (TestIgnoredException ex)
        {
            _ignored++;
            PrintIgnore(method.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _errors++;
            PrintError(method.Name, ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (teardown != null)
                await Invoke(instance, teardown);
        }
    }

    // ---------------- VALIDATION ----------------

    private bool ValidateParameters(MethodInfo method, object[] values, out string error)
    {
        var parameters = method.GetParameters();

        if (values.Length != parameters.Length)
        {
            error = $"Parameter count mismatch. Expected {parameters.Length}, got {values.Length}";
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            if (values[i] == null) continue;

            if (!parameters[i].ParameterType.IsAssignableFrom(values[i].GetType()))
            {
                error = $"Parameter type mismatch at index {i}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    // ---------------- UTIL ----------------

    private object? CreateInstance(Type type, object? shared)
    {
        var ctor = type.GetConstructors().First();

        var parameters = ctor.GetParameters();

        if (shared != null &&
            parameters.Length == 1 &&
            parameters[0].ParameterType == shared.GetType())
        {
            return Activator.CreateInstance(type, shared);
        }

        return Activator.CreateInstance(type);
    }

    private async Task Invoke(object? instance, MethodInfo method, object[]? parameters = null)
    {
        var result = method.Invoke(instance, parameters);

        if (result is Task task)
            await task;
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