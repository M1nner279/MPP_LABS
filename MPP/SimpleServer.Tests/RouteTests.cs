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
    [Category("Smoke")]
    [Priority(1)]
    [Author("Maksim")]
    [Timeout(1000)]
    public async Task Ping_ReturnsPong()
    {
        var response = await _context.Server.RouteAsync(
            new HttpRequest("GET", "/ping"));
        
        Assert.IsNotNull(response);
        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual("pong", response.Body);
    }

    [TestMethod]
    [Category("Smoke")]
    [Priority(1)]
    [Author("Maksim")]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _context.Server.RouteAsync(
            new HttpRequest("GET", "/unknown"));

        Assert.AreEqual(404, response.StatusCode);
        Assert.AreNotEqual(200, response.StatusCode);
        Assert.AreEqual("Not Found", response.Body);
    }
}