using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Misc;

[Serializable, NetSerializable]
public sealed partial class PlayerLeashEscapeDoAfterEvent : SimpleDoAfterEvent;
