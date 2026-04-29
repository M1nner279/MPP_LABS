namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PriorityAttribute : Attribute
{
    public int Level { get; }

    public PriorityAttribute(int level)
    {
        Level = level;
    }
}
