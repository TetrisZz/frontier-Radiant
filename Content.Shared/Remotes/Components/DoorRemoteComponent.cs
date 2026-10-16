using Robust.Shared.GameStates;
using Robust.Shared.Audio;

namespace Content.Shared.Remotes.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DoorRemoteComponent : Component
{
    [DataField]
    public SoundSpecifier? SwitchSound;

    [DataField]
    public SoundSpecifier? ActionSound;

    [AutoNetworkedField]
    [DataField]
    public OperatingMode Mode = OperatingMode.OpenClose;

    /// <summary>
    /// Does the remote allow the user to manipulate doors that they have access to, even if the remote itself does not?
    /// </summary>
    [AutoNetworkedField]
    [DataField]
    public bool IncludeUserAccess = false;
}

public enum OperatingMode : byte
{
    OpenClose,
    ToggleBolts,
    ToggleEmergencyAccess,
    placeholderForUiUpdates
}
