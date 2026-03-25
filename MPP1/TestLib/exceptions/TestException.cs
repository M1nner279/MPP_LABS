namespace TestLib.exceptions;

public abstract class TestException : Exception
{
    protected TestException(string message) : base(message) { }

    protected TestException(string message, Exception inner)
        : base(message, inner) { }
}