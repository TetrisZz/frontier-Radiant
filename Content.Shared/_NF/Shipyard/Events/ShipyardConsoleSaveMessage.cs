using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///     Save a ship from the console. The button validates deed ownership client-side, but server will check again.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleSaveMessage : BoundUserInterfaceMessage
{
    public ShipyardConsoleSaveMessage()
    {
    }
}