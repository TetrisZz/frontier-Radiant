using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Paper;

// Radiant sector: Starlight multistamp support required by the cyber stamp hand.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MultistampComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<EntityUid> Stamps = new();

    [ViewVariables, AutoNetworkedField]
    public int CurrentEntry;

    [ViewVariables, AutoNetworkedField]
    public string CurrentStampName = string.Empty;

    [ViewVariables, AutoNetworkedField]
    public bool UiUpdateNeeded;

    [DataField]
    public bool StatusShowStamp = true;

    [DataField]
    public SoundSpecifier? ChangeSound = new SoundPathSpecifier("/Audio/Machines/lightswitch.ogg");
}
