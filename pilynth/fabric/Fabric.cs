#pragma warning disable CS8603

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

[YarnBind("net.minecraft.util.Identifier")]
public class Identifier
{
    [YarnBind]
    public extern static Identifier of(string ns, string id);
    [YarnBind]
    public extern static Identifier of(string id);
}