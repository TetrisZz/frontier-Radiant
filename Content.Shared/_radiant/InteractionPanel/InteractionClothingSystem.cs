using Content.Shared.Chat.Prototypes;
using Content.Shared.Inventory;

namespace Content.Shared.Interaction.Panel;

/// <summary>Clothing restrictions shared by the panel and authoritative execution.</summary>
public sealed class InteractionClothingSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public bool IsBlocked(EntityUid user, EntityUid? target, InteractionPrototype prototype, out bool targetBlocked)
    {
        targetBlocked = false;
        if (HasClothing(user, prototype.RequiredClothingSlots) || HasClothing(user, prototype.UserBlockedClothingSlots))
            return true;
        if (target is not { } partner)
            return false;
        targetBlocked = HasClothing(partner, prototype.RequiredClothingSlots)
                        || HasClothing(partner, prototype.OneRequiredClothingSlots)
                        || HasClothing(partner, prototype.TargetBlockedClothingSlots);
        return targetBlocked;
    }

    private bool HasClothing(EntityUid entity, List<string>? slots)
    {
        if (slots == null)
            return false;
        foreach (var slot in slots)
        {
            if (_inventory.TryGetSlotEntity(entity, slot, out _))
                return true;
        }
        return false;
    }
}
