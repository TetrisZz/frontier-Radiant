using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Movement.Components;

/// <summary>
/// Reduces a body's movement bonus while shoes cover this leg.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MovementBodyPartHinderedByShoesComponent : Component
{
    [DataField]
    public float HinderModifier;
}
