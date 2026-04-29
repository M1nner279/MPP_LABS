using TestLib;
using TestLib.attributes;
using SimpleServer;
using SimpleServer.Core;
using SimpleServer.Http;

namespace SimpleServer.Tests;

[TestClass]
public class DataRowTests
{
    private SimpleHttpServer _server = null!;
    
    [SetUp]
    public void Setup()
    {
        _server = new SimpleHttpServer();
    }
    
    [TestMethod]
    [DataRow("/a", 404)]
    [DataRow("/b", 404)]
    [DataRow("/c", 500, IgnoreMessage = "Demonstration of ignored test")]
    public async Task UnknownRoutes_Return404(string path, int expected)
    {
        var response = await _server.RouteAsync(
            new HttpRequest("GET", path));

        Assert.AreEqual(expected, response.StatusCode);
        Assert.IsFalse(response.StatusCode == 200);
    }

    [TestMethod]
    [Ignore]
    [DataRow(1)]
    public void IgnoredTest(int value)
    {
        Assert.IsTrue(value > 0);
    }
}