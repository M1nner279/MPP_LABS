namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuthorAttribute : Attribute
{
    public string Name { get; }

    public AuthorAttribute(string name)
    {
        Name = name;
    }
}
