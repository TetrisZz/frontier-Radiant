using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Abilities.Vulpkanin;

/// <summary>
/// Grants vulpkanin the feral surge action and configures its buff values.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedVulpkaninAbilitySystem))]
public sealed partial class VulpkaninFeralAbilityComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionVulpkaninFeral";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public float SpeedModifier = 1.12f;

    [DataField]
    public DamageSpecifier BonusDamage = new();

    [DataField]
    public SoundSpecifier? ActivateSound;

    [DataField]
    public LocId AlreadyActivePopup = "vulpkanin-feral-already-active";
}

public sealed partial class VulpkaninFeralEvent : InstantActionEvent;
