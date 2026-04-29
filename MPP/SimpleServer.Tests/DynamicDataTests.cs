using TestLib;
using TestLib.attributes;

namespace SimpleServer.Tests;

[TestClass]
public class DynamicDataTests
{
    [TestMethod]
    [Category("DynamicData")]
    [Priority(2)]
    [Author("MPP Team")]
    [DynamicData(nameof(Cases))]
    public void GeneratedCases_Work(int input, bool expectedEven)
    {
        Assert.AreEqual(expectedEven, input % 2 == 0);
    }

    private static IEnumerable<object[]> Cases()
    {
        yield return new object[] { 1, false };
        yield return new object[] { 2, true };
        yield return new object[] { 15, false };
        yield return new object[] { 40, true };
    }
}
