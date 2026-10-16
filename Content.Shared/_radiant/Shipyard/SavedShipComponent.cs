using System;
using Robust.Shared.Network;

namespace Content.Shared._radiant.Shipyard;

[RegisterComponent]
public sealed partial class SavedShipComponent : Component
{
    [DataField]
    public string? VesselId;

    [DataField]
    public string SaveId = string.Empty;

    [DataField]
    public string? OwnerUserId;

    [DataField]
    public string? OwnerCharacterName;

    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerUserId) && !string.IsNullOrWhiteSpace(OwnerCharacterName);

    public bool IsOwner(NetUserId userId, string characterName) => HasOwner
        && string.Equals(OwnerUserId, userId.ToString(), StringComparison.Ordinal)
        && string.Equals(OwnerCharacterName, characterName.Trim(), StringComparison.Ordinal);
}
