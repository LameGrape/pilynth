using Pilynth.Attributes;

namespace Pilynth.Fabric;

[YarnBind("net.minecraft.block.AbstractBlock")]
public class AbstractBlock
{
    public AbstractBlock(Settings settings) { }

    [YarnBind("net.minecraft.block.AbstractBlock.Settings")]
    public class Settings
    {
        [YarnBind] public extern Settings registryKey(RegistryKey key);
        [YarnBind] public static extern Settings create();
    }
}

[YarnBind("net.minecraft.block.Block")]
public class Block : AbstractBlock
{
    public Block(Settings settings) : base(settings) { }
}