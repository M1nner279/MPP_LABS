using TestLib;
using TestLib.attributes;

namespace SimpleServer.Tests;

[TestClass]
public class ParallelLoadTests
{
    [TestMethod]
    [Timeout(2000)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    [DataRow(12)]
    public async Task SimulatedIoBoundWork_Completes(int seed)
    {
        // Имитация I/O-нагрузки, хорошо параллелится.
        var delayMs = 220 + (seed % 4) * 20;
        await Task.Delay(delayMs);

        Assert.IsTrue(delayMs >= 220);
        Assert.IsFalse(delayMs > 320);
    }
}
