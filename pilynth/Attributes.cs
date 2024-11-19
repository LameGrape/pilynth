namespace Pilynth.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public class JavaBindAttribute : Attribute
{
    public readonly string name;
    public JavaBindAttribute(string name) { this.name = name; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class EntryPointAttribute : Attribute { }