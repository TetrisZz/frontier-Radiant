using Robust.Shared.Serialization;

namespace Content.Shared._Triad.Shipyard.Save;

[Serializable, NetSerializable]
public sealed class SendShipSaveDataClientMessage : EntityEventArgs
{
    public string ShipName { get; }
    public string ShipData { get; }
    public Guid RequestId { get; }

    public SendShipSaveDataClientMessage(string shipName, string shipData, Guid requestId)
    {
        ShipName = shipName;
        ShipData = shipData;
        RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class ShipSaveWrittenMessage(Guid requestId, bool success) : EntityEventArgs
{
    public Guid RequestId { get; } = requestId;
    public bool Success { get; } = success;
}
