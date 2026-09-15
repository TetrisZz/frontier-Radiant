using Content.Shared._radiant.WeaponSerial;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._radiant.WeaponSerial;

/// <summary>
///     BUI of the weapon registration console. Keeps the window alive while the
///     UI is open: the server pushes WeaponRegistrationConsoleState on open,
///     on weapon insert/remove and after the owner is saved.
/// </summary>
[UsedImplicitly]
public sealed class WeaponRegistrationConsoleBoundUserInterface : BoundUserInterface
{
    private WeaponRegistrationConsoleWindow? _window;

    public WeaponRegistrationConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new WeaponRegistrationConsoleWindow();
        _window.OpenCentered();
        _window.OnClose += Close;
        _window.OnOwnerSave += owner => SendMessage(new WeaponRegistrationSetOwner(owner));
        _window.OnStamp += () => SendMessage(new WeaponRegistrationStampSerial());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WeaponRegistrationConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Close();
        _window = null;
    }
}
