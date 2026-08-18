using Content.Shared.Damage;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._radiant.Power.Components;

[RegisterComponent]
public sealed partial class BorgRepairPodComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    [DataField]
    public float RepairInterval = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextRepairTime = TimeSpan.Zero;
}
