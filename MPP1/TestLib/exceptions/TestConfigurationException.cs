namespace TestLib.exceptions;

public abstract class TestConfigurationException : TestException
{
    protected TestConfigurationException(string message)
        : base(message)
    {
    }

    protected TestConfigurationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public class InvalidTestSignatureException : TestConfigurationException
{
    public InvalidTestSignatureException(string message) : base(message) { }
}

public class InvalidTestDataException : TestConfigurationException
{
    public InvalidTestDataException(string message) : base(message) { }
}

public class TestInstantiationException : TestConfigurationException
{
    public TestInstantiationException(string message, Exception inner)
        : base(message, inner) { }
}