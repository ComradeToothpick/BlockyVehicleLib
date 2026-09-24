using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CookingRecipeTraitRestiction;

public class RestrictedItemSlot : ItemSlot
{
    public Trait[] inventoryTraits;
    public RestrictedItemSlot(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        InventoryBase inventory = this.inventory;
        return (inventory != null ? (inventory.PutLocked ? 1 : 0) : 0) == 0 &&
               (this.CanStoreTags.IsEmpty ||
                sourceSlot?.Itemstack == null ||
                sourceSlot.Itemstack.Collectible.GetTags(sourceSlot.Itemstack).Overlaps(this.CanStoreTags)) &&
               sourceSlot?.Itemstack?.Collectible != null &&
               (sourceSlot.Itemstack.Collectible.GetStorageFlags(sourceSlot.Itemstack) & this.StorageType) > (EnumItemStorageFlags) 0 &&
               this.inventory.CanContain(this, sourceSlot) &&
               (!sourceSlot.Itemstack.Item.Code.Path.Contains("Restricted") ||
                (sourceSlot.));
    }
}