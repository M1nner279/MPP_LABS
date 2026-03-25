using TestLib;
using TestLib.attributes;
using SimpleServer;
using SimpleServer.Http;

namespace SimpleServer.Tests;

[TestClass]

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

        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual("pong", response.Body);
        Assert.IsNotNull(response);
    }

    [TestMethod]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _context.Server.RouteAsync(
            new HttpRequest("GET", "/unknown"));

        Assert.AreEqual(404, response.StatusCode);
        Assert.AreNotEqual(200, response.StatusCode);
        Assert.Contains("Not", new[] { response.Body });
    }
}