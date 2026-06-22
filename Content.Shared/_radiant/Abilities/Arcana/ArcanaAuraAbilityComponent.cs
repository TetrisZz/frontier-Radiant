using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Abilities.Arcana;

/// <summary>
/// Grants arcana an ambient aura action that produces scented messages nearby.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArcanaAuraAbilityComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionArcanaAura";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField, AutoNetworkedField]
    public bool Enabled;

    [DataField]
    public float Radius = 10f;

    [DataField]
    public TimeSpan PulseInterval = TimeSpan.FromSeconds(18);

    [DataField]
    public List<LocId> AuraMessages = new()
    {
        "arcana-aura-message-1",
        "arcana-aura-message-2",
        "arcana-aura-message-3",
        "arcana-aura-message-4",
        "arcana-aura-message-5",
        "arcana-aura-message-6",
        "arcana-aura-message-7",
        "arcana-aura-message-8",
        "arcana-aura-message-9",
        "arcana-aura-message-10",
        "arcana-aura-message-11",
        "arcana-aura-message-12",
    };

    [DataField]
    public LocId EnabledPopup = "arcana-aura-enabled";

    [DataField]
    public LocId DisabledPopup = "arcana-aura-disabled";

    public TimeSpan NextPulse;
}

public sealed partial class ArcanaAuraToggleEvent : InstantActionEvent;
