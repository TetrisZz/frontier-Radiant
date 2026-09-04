using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Clothing.Components;

/// <summary>
/// Lets special shoes scale the movement penalty supplied by cyberlegs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OverrideShoesHinderComponent : Component
{
    [DataField("Modifier")]
    public float HinderModifier;
}
