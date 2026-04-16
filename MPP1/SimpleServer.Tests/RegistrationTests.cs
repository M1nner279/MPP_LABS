using SimpleServer.Core;
using SimpleServer.Http;
using TestLib;
using TestLib.attributes;

namespace SimpleServer.Tests;

[TestClass]
public class RegistrationTests
{
    private SimpleHttpServer _server = null!;

    [SetUp]
    public void Setup()
    {
        _server = new SimpleHttpServer();
    }

    [TearDown]
    public void TearDown()
    {
        Assert.IsNotNull(_server);
    }

    [TestMethod]
    public void DuplicateRoute_Throws()
    {
        _server.Register("/test", _ =>
            Task.FromResult(new HttpResponse()));
        
        Task.Delay(500);

        Assert.Throws<InvalidOperationException>(() =>
            _server.Register("/test", _ =>
                Task.FromResult(new HttpResponse())));
    }

    [TestMethod]
    public void RouteCount_Increases()
    {
        Assert.AreEqual(0, _server.RouteCount);

        _server.Register("/a", _ =>
            Task.FromResult(new HttpResponse()));
        
        Task.Delay(500);

        Assert.AreEqual(1, _server.RouteCount);
        Assert.IsTrue(_server.RouteCount > 0);
    }
}