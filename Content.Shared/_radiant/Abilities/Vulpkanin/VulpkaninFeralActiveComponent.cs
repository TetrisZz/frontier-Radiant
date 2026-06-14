using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Abilities.Vulpkanin;

/// <summary>
/// Temporary feral surge buff applied after activating the vulpkanin ability.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class VulpkaninFeralActiveComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1.12f;

    [DataField, AutoNetworkedField]
    public DamageSpecifier BonusDamage = new();
}
