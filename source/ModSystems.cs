using Vintagestory.API.Common;

namespace Exoskeleton;

public sealed class ExoskeletonSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Exoskeleton:Exoskeleton", typeof(Exoskeleton));
    }
}