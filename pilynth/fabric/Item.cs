using Pilynth.Attributes;

namespace Pilynth.Fabric;

[YarnBind("net.minecraft.item.Item")]
public class Item
{
    public Item(Settings settings) { }

    [YarnBind("net.minecraft.item.Item.Settings")]
    public class Settings
    {
        [YarnBind]
        public extern Settings registryKey(RegistryKey key);
    }
}