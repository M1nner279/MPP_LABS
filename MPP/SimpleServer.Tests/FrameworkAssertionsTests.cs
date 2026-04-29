using TestLib;
using TestLib.attributes;

namespace SimpleServer.Tests;

[TestClass]
public class FrameworkAssertionsTests
{
    [TestMethod]
    public void Null_And_Reference_Assertions_Work()
    {
        object? nullable = null;
        Assert.IsNull(nullable);

        var shared = new object();
        var same = shared;
        var different = new object();

        Assert.AreSame(shared, same);
        Assert.AreNotSame(shared, different);
    }

    [TestMethod]
    public void Collection_Assertions_Work()
    {
        var values = new[] { "alpha", "beta" };
        var empty = Array.Empty<int>();

        Assert.DoesNotContain("gamma", values);
        Assert.IsNotEmpty(values);
        Assert.IsEmpty(empty);
    }

    [TestMethod]
    public async Task Async_And_Exception_Assertions_Work()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Delay(20);
            throw new InvalidOperationException("async fail");
        });
    }
}
