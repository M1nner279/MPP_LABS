namespace TestLib.exceptions;

public class TestFailedException : TestException
{
    public TestFailedException(string message) : base(message) { }
}