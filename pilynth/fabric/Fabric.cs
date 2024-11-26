using Pilynth.Attributes;

namespace Pilynth.Fabric;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class IdentifierAttribute : Attribute
{
    public string identifier;

    public IdentifierAttribute(string identifier)
    {
        this.identifier = identifier;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class VersionAttribute : Attribute
{
    public string version;

    public VersionAttribute(string version)
    {
        this.version = version;
    }
}

[JavaBind("net.fabricmc.api.ModInitializer")]
public interface FabricMod
{
    public void onInitialize();
}

[JavaBind("org.slf4j.LoggerFactory")]
public class LoggerFactory
{
    [JavaBind("org.slf4j.LoggerFactory.getLogger")]
    public extern static Logger GetLogger(string identifier);
}

[JavaBind("org.slf4j.Logger")]
public interface Logger
{
    [JavaBind("org.slf4j.Logger.info")]
    public void Info(string message);
}

[JavaBind("net.minecraft.class_2960")]
public class Identifier
{
    [JavaBind("net.minecraft.class_2960.method_60655")]
    public static Identifier of(string ns, string id) { return new Identifier(); }
    [JavaBind("net.minecraft.class_2960.method_60654")]
    public static Identifier of(string id) { return new Identifier(); }
}