namespace Pilynth.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class JavaBindAttribute : Attribute
{
    public readonly string name;
    public JavaBindAttribute(string name) { this.name = name; }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Field, AllowMultiple = false)]
public class YarnBindAttribute : Attribute
{
    public readonly string name = "";
    public readonly bool useNativeName = false;
    public YarnBindAttribute(string name) { this.name = name; }
    public YarnBindAttribute() { useNativeName = true; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class EntryPointAttribute : Attribute { }