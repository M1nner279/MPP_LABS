using TestLib.attributes;
using SimpleServer.Core;
using SimpleServer.Http;

namespace SimpleServer.Tests.Context;

[SharedContext(typeof(SimpleHttpServer))]
public class TestServerContext
{
    public SimpleHttpServer Server { get; private set; } = null!;

    [SharedContextInitialize]
    public void Init()
    {
        Server = new SimpleHttpServer();
        
        Server.Register("/ping", _ =>
            Task.FromResult(new HttpResponse
            {
                StatusCode = 200,
                Body = "pong"
            }));
            
        Console.WriteLine("[CONTEXT] Shared Server initialized.");
    }

    [SharedContextCleanUp]
    public void Cleanup()
    {
        Console.WriteLine("[CONTEXT] Shared Server disposed.");
    }
}