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
    //private int setupCalls = 0;
    
    [SetUp]
    public void Setup()
    {
        _server = new SimpleHttpServer();
        //setupCalls++;
    }
    
    [TestMethod]
    [DataRow("/a")] // test validation param
    [DataRow("/b", 404)]
    [DataRow("/c", 500, IgnoreMessage = "Demonstration of ignored test")]
    public async Task UnknownRoutes_Return404(string path, int expected)
    {
        //var server = new SimpleHttpServer();

        var response = await _server.RouteAsync(
            new HttpRequest("GET", path));
        
        await Task.Delay(500);

        Assert.AreEqual(expected, response.StatusCode);
        Assert.IsFalse(response.StatusCode == 200);
        //Console.WriteLine(setupCalls);
    }

    [TestMethod]
    [Ignore]
    [DataRow(1)]
    public void IgnoredTest(int value)
    {
        Assert.IsTrue(value > 0);
    }
}