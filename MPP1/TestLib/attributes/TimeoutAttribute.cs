namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Method)]
public class TimeoutAttribute : Attribute
{
    public int Milliseconds { get; }
    public TimeoutAttribute(int milliseconds) => Milliseconds = milliseconds;
}