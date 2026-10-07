using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Addictions;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WithdrawalVisualsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Intensity;
}
