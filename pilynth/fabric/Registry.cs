#pragma warning disable CS8603

using Pilynth.Attributes;

namespace Pilynth.Fabric;

[YarnBind("net.minecraft.registry.Registry")]
public interface Registry
{
    [YarnBind]
    public static extern object register(Registry registry, RegistryKey identifier, object thing);
}

[YarnBind("net.minecraft.registry.RegistryKey")]
public abstract class RegistryKey
{
    [YarnBind]
    public static extern RegistryKey of(RegistryKey key, Identifier identifier);
    [YarnBind]
    public static extern RegistryKey ofRegistry(Identifier registry);
}


[YarnBind("net.minecraft.registry.DefaultedRegistry")]
public abstract class DefaultedRegistry : Registry { }

[YarnBind("net.minecraft.registry.Registries")]
public abstract class Registries
{
    [YarnBind]
    public static readonly DefaultedRegistry? ITEM;
}