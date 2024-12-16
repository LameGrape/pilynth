using Pilynth.Attributes;

namespace Pilynth.Fabric;

[YarnBind("net.minecraft.item.Item")]
public class Item
{
    public Item(Settings settings) { }

    [YarnBind("net.minecraft.item.Item.Settings")]
    public class Settings
    {
        [YarnBind] public extern Settings registryKey(RegistryKey key);
    }
}

[YarnBind("net.minecraft.item.BlockItem")]
public class BlockItem : Item
{
    public BlockItem(Block block, Settings settings) : base(settings) { }
}