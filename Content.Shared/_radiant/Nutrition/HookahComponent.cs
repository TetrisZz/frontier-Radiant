using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Nutrition;

[RegisterComponent]
public sealed partial class HookahComponent : Component
{
    [DataField]
    public float UseDelay = 2.5f;

    [DataField]
    public float PuffAmount = 2f;

    [DataField]
    public SoundSpecifier InhaleSound = new SoundPathSpecifier("/Audio/Items/drink.ogg");

    public bool InUse;

    public EntityUid? Hose;
}

[Serializable, NetSerializable]
public sealed partial class HookahDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum HookahVisuals : byte
{
    Active,
}
