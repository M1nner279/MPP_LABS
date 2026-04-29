namespace ThreadPoolModule.Logging;

public static class Logger
{
    private static readonly object ConsoleLock = new();

    public static void Info(string message)
    {
        Log($"INFO: {message}");
    }

    public static void Error(string message)
    {
        Log($"ERROR: {message}");
    }

    public static void Debug(string message)
    {
        //Log($"DEBUG: {message}");
    }

    public static void Warn(string message)
    {
        Log($"WARN: {message}");
    }

    private static void Log(string message)
    {
        var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Thread.CurrentThread.Name ?? "main"}] {message}";

        lock (ConsoleLock)
        {
            Console.WriteLine(logMessage);
        }
    }
}