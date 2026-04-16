using SimpleServer.Interfaces;
using SimpleServer.Http;

namespace SimpleServer.Core;

public class SimpleHttpServer : IRouter
{
    private readonly RouteTable _routeTable = new();
    private readonly MiddlewarePipeline _pipeline = new();
    private bool _isDisposed;

    public void Register(string path, Func<HttpRequest, Task<HttpResponse>> handler)
    {
        _routeTable.Add(path, handler);
    }

    public void Use(Func<HttpRequest, Func<Task<HttpResponse>>, Task<HttpResponse>> middleware)
    {
        _pipeline.Use(middleware);
    }

    public async Task<HttpResponse> RouteAsync(HttpRequest request)
    {
        if (!_routeTable.TryGet(request.Path, out var handler))
        {
            return new HttpResponse
            {
                StatusCode = 404,
                Body = "Not Found"
            };
        }

        return await _pipeline.ExecuteAsync(
            request,
            () => handler(request));
    }

    public int RouteCount => _routeTable.Count;
    
    private void CheckDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SimpleHttpServer), "Server is shut down.");
    }
    
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
    }
}