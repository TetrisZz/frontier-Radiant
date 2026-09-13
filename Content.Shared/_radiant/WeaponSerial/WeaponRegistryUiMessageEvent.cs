using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.WeaponSerial;

/// <summary>
///     Message from the cartridge UI to the server: the player manually entered
///     (or cleared) the owner name for the given serial number. Wrapped into
///     CartridgeUiMessage by the client and relayed by CartridgeLoaderSystem.
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistryUiMessageEvent(string serial, string? owner) : CartridgeMessageEvent
{
    public readonly string Serial = serial;
    public readonly string? Owner = owner;
}
