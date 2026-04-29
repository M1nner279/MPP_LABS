namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class DynamicDataAttribute : Attribute
{
    public string MemberName { get; }

    public DynamicDataAttribute(string memberName)
    {
        MemberName = memberName;
    }
}
