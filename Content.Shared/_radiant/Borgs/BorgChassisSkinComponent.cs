using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Borgs;

/// <summary>Overrides only appearance. The selected standard profession still supplies modules, laws, slots and stats.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BorgChassisSkinComponent : Component
{
    [DataField(required: true)]
    public string BodyState = default!;

    [DataField]
    public string? MovementState;

    [DataField]
    public string? HasMindState;

    [DataField]
    public string? NoMindState;

    [DataField]
    public string? LightState;
}
