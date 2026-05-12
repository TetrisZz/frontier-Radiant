using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Misc;

/// <summary>
/// Raised when the timed leash attachment action completes or is cancelled.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class PlayerLeashAttachDoAfterEvent : SimpleDoAfterEvent;
