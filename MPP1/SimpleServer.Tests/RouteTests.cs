using TestLib;
using TestLib.attributes;
using SimpleServer;
using SimpleServer.Http;
using SimpleServer.Tests.Context;

namespace SimpleServer.Tests;

[TestClass]
[SharedContext(typeof(TestServerContext))]
public class RouteTests
{
    private readonly TestServerContext _context;

    public RouteTests(TestServerContext context)
    {
        _context = context;
    }

    [TestMethod]
    public async Task Ping_ReturnsPong()
    {
        var response = await _context.Server.RouteAsync(
            new HttpRequest("GET", "/ping"));

        await Task.Delay(500);
        
        Assert.IsNotNull(response);
        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual("pong", response.Body);
    }

    [TestMethod]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _context.Server.RouteAsync(
            new HttpRequest("GET", "/unknown"));
        
        await Task.Delay(500);

        Assert.AreEqual(404, response.StatusCode);
        Assert.AreNotEqual(200, response.StatusCode);
        Assert.Contains("Not", new[] { response.Body });
    }
}