using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared.Cabinet;

/// <summary>
/// Used for entities that can be opened, closed, and can hold one item. E.g., fire extinguisher cabinets.
/// Requires <c>OpenableComponent</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(ItemCabinetSystem))]
public sealed partial class ItemCabinetComponent : Component
{
    /// <summary>
    /// Name of the <see cref="ItemSlot"/> that stores the actual item.
    /// </summary>
    [DataField]
    public string Slot = "ItemCabinet";

    // Optional whole-cabinet states for sprites that include both the frame and its contents.
    // Cabinets with separate item/door layers retain their existing visuals when these are unset.
    [DataField] public string? EmptyOpenState;
    [DataField] public string? EmptyClosedState;
    [DataField] public ItemCabinetSpriteStates? DefaultItemStates;
    [DataField] public Dictionary<EntProtoId, ItemCabinetSpriteStates> ItemStateOverrides = new();
}

[DataDefinition]
public sealed partial class ItemCabinetSpriteStates
{
    [DataField(required: true)] public string Open = default!;
    [DataField(required: true)] public string Closed = default!;
}

[Serializable, NetSerializable]
public enum ItemCabinetVisuals : byte
{
    ContainsItem,
    Layer,
    State
}
