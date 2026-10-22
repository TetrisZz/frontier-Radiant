using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Perfume;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PerfumeComponent : Component
{
    [DataField(required: true)]
    public LocId Fragrance;

    [DataField, AutoNetworkedField]
    public int Uses = 30;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(20);

    [DataField]
    public TimeSpan ApplyDelay = TimeSpan.FromSeconds(0.75);

}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PerfumedComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocId Fragrance;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan ExpiresAt;
}

[Serializable, NetSerializable]
public sealed partial class ApplyPerfumeEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WashPerfumeEvent : SimpleDoAfterEvent;
