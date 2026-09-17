namespace Content.Shared._radiant.WeaponSerial.Components;

/// <summary>
///     Marks the standalone OSK weapon registry cartridge. It is its OWN program
///     (a separate cartridge entity, not part of the wanted list cartridge) and
///     is owned by WeaponSerialSystem. A dedicated component is required because
///     Robust allows only ONE subscription per (component, event) pair, and
///     (WantedListCartridgeComponent, CartridgeUiReadyEvent) is already taken by
///     CriminalRecordsSystem.
/// </summary>
[RegisterComponent]
public sealed partial class WeaponRegistryCartridgeComponent : Component
{
}
