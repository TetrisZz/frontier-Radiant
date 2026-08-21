using Content.Shared.DoAfter;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Vehicles;

[RegisterComponent]
public sealed partial class VehicleTrickComponent : Component
{
    [DataField]
    public float Duration = 0.8f;

    [DataField]
    public float FlipChance = 0.1f;

    [DataField]
    public float FailureChance = 0.15f;

    [DataField]
    public float DetachOnFailureChance = 0.1f;

    [DataField]
    public EntProtoId GripPrototype = "VehicleTrickSkateboardGripRS";

    [DataField]
    public float Cooldown = 2f;

    public TimeSpan NextTrick;
    public EntityUid? User;
    public bool IsFlip;
    public bool IsFailure;
    public readonly List<EntityUid> TemporaryGrips = new();
}

[Serializable, NetSerializable]
public sealed partial class VehicleTrickDoAfterEvent : SimpleDoAfterEvent;
