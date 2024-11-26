using Pilynth.Attributes;

namespace Pilynth.Fabric;

[YarnBind("net.minecraft.item.Item")]
public class Item
{
    public Settings settings;
    public Item(Settings settings) { this.settings = settings; }

    [JavaBind("net.minecraft.class_1792$class_1793")]
    public class Settings
    {
        [JavaBind("net.minecraft.class_1792$class_1793.method_63686")]
        [NoStackpoint]
        public Settings registryKey(RegistryKey key) { return this; }
    }
}