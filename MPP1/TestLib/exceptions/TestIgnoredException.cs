namespace TestLib.exceptions;

public class TestIgnoredException : TestException
{
    public TestIgnoredException(string message) : base(message) { }
}