namespace TestLib.attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CategoryAttribute : Attribute
{
    public string Name { get; }

    public CategoryAttribute(string name)
    {
        Name = name;
    }
}
