using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._radiant.WeaponSerial;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._radiant.WeaponSerial;

/// <summary>
///     OSK weapon registry program UI. A standalone UIFragment wrapper for
///     <see cref="WeaponRegistryUiFragment"/>, living on its own cartridge
///     (separate from the wanted list cartridge owned by CriminalRecordsSystem).
///     The PDA is read-only: the owner is entered in the weapon registration
///     console, the server only pushes registry snapshots here. Searching and
///     sorting happen entirely on the client — the whole registry is in memory.
/// </summary>
public sealed partial class WeaponRegistryUi : UIFragment
{
    private WeaponRegistryUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WeaponRegistryUiFragment();

        // Every time the program UI is (re)shown, ask the server for the current
        // snapshot. This covers a brand-new window open and the loader re-sending
        // its own UI state while this program stays active.
        var initialRefreshMessage = new CartridgeUiMessage(new WeaponRegistryUiMessageEvent(string.Empty, null));
        userInterface.SendMessage(initialRefreshMessage);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WeaponRegistryUiState registry)
            _fragment?.UpdateRegistry(registry.Entries);
    }
}
