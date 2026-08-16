using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Fitness;

[RegisterComponent]
public sealed partial class PunchingBagComponent : Component
{
    public TimeSpan AnimationEnd;
}

[RegisterComponent]
public sealed partial class BenchPressComponent : Component
{
    [DataField]
    public float ExerciseDuration = 6f;

    public bool InUse;

    public EntityUid? User;

    public EntityUid? BarbellVisual;
}

[RegisterComponent]
public sealed partial class ExerciseBikeComponent : Component
{
    [DataField]
    public float ExerciseDuration = 8f;

    public bool InUse;

    public EntityUid? User;
}

[Serializable, NetSerializable]
public sealed partial class BenchPressDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ExerciseBikeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum FitnessVisuals : byte
{
    Active,
}
