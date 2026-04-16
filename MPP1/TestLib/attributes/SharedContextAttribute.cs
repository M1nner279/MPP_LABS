namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Class)]
public class SharedContextAttribute : Attribute
{
    public Type ContextType { get; }
    public SharedContextAttribute(Type contextType) => ContextType = contextType;
}