using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Medical.Limbs;

/// <summary>
/// Marks a loose cyberlimb that can be rebuilt for the opposite side with a screwdriver.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ReversibleCyberLimbComponent : Component;
