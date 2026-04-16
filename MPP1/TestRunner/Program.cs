using System.Reflection;

string assemblyPath = "../../../../SimpleServer.Tests/bin/Debug/net9.0/SimpleServer.Tests.dll";

if (!File.Exists(assemblyPath))
{
    Console.WriteLine($"Assembly not found: {assemblyPath}");
    return;
}

var assembly = Assembly.LoadFrom(assemblyPath);
// var runner = new TestRunner.TestRunner();
var runner = new TestRunner.TestRunner(maxDegreeOfParallelism: 2);

Console.WriteLine("\n--- PARALLEL RUN ---");
await runner.RunAsync(assembly, parallel: true);

Console.WriteLine("--- SEQUENTIAL RUN ---");
await runner.RunAsync(assembly, parallel: false);