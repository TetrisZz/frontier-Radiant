using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Abilities.Shadowkin;

/// <summary>
/// Gives a shadowkin a limited short-range jump through shadows.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShadowkinShadeStepComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionShadowkinShadeStep";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField, AutoNetworkedField]
    public float Energy = 90f;

    [DataField]
    public float MaxEnergy = 90f;

    [DataField]
    public float EnergyCost = 30f;

    [DataField]
    // Radiant Sector: a fully depleted reserve refills over ten minutes.
    public float EnergyRegenPerSecond = 0.15f;

    [DataField]
    public float Range = 5f;

}

public sealed partial class ShadowkinShadeStepEvent : InstantActionEvent;
