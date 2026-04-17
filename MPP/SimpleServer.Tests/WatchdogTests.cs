using TestLib;
using TestLib.attributes;

namespace SimpleServer.Tests;

[TestClass]
[Ignore]
public class WatchdogTests
{
    [TestMethod]
    [Timeout(2000)]
    public void StuckMethod_ShouldBeDetectedByWatchdog()
    {
        while (true) 
        { 
            Thread.Sleep(1000); 
        }
    }

    [TestMethod]
    public void NormalMethod_AfterStuck()
    {
        Assert.IsTrue(true);
    }
}