using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._radiant.Power.Components;

[RegisterComponent]
public sealed partial class BorgRepairPodComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    /// <summary>
    /// Radiant Sector: the unmodified repair amount, used when machine parts are refreshed.
    /// </summary>
    [ViewVariables]
    public DamageSpecifier BaseDamage = default!;

    /// <summary>
    /// Radiant Sector: the charger's unmodified rate, used when capacitor parts are refreshed.
    /// </summary>
    [ViewVariables]
    public float BaseChargeRate;

    /// <summary>
    /// Radiant Sector: multiplier supplied by manipulator parts.
    /// </summary>
    [ViewVariables]
    public float RepairMultiplier = 1f;

    /// <summary>
    /// Radiant Sector: multiplier supplied by capacitor parts.
    /// </summary>
    [ViewVariables]
    public float ChargeMultiplier = 1f;

    [DataField]
    public float RepairInterval = 1f;

    /// <summary>
    /// Sound played when the pod successfully restores damage to an occupant.
    /// </summary>
    [DataField]
    public SoundSpecifier? RepairSound;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextRepairTime = TimeSpan.Zero;
}
