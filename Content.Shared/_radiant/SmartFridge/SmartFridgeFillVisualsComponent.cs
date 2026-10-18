using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.SmartFridge;

[RegisterComponent, NetworkedComponent]
public sealed partial class SmartFridgeFillVisualsComponent : Component
{
    [DataField]
    public string Container = "smart_fridge_inventory";

    public static int FillLevel(int count) => count switch
    {
        <= 0 => 0,
        <= 25 => 1,
        <= 75 => 2,
        _ => 3,
    };
}

[Serializable, NetSerializable]
public enum SmartFridgeFillVisuals : byte
{
    Level,
}
