#pragma warning disable CS8603

using Pilynth.Attributes;

namespace Pilynth.Fabric;

[JavaBind("net.minecraft.class_2378")]
public interface Registry
{
    [JavaBind("net.minecraft.class_2378.method_39197")]
    public static object Register(Registry registry, RegistryKey identifier, object thing) { return new object(); }
}

[JavaBind("net.minecraft.class_5321")]
public abstract class RegistryKey
{
    [JavaBind("net.minecraft.class_5321.method_29179")]
    public static RegistryKey of(RegistryKey key, Identifier identifier) { return null; }
    [JavaBind("net.minecraft.class_5321.method_29180")]
    public static RegistryKey ofRegistry(Identifier registry) { return null; }
}


[JavaBind("net.minecraft.class_7922")]
public abstract class DefaultedRegistry : Registry { }

[JavaBind("net.minecraft.class_7923")]
public abstract class Registries
{
    [JavaBind("field_41178")]
    public static readonly DefaultedRegistry? ITEM;
}