using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.GhostSkins;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GhostSkinComponent : Component
{
    [DataField, AutoNetworkedField] public string Skin = GhostSkinRules.Default;
    [DataField] public EntProtoId Action = "ActionGhostSkin";
    [DataField] public EntityUid? ActionEntity;
}

public sealed partial class GhostSkinActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class GhostSkinRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class GhostSkinSelectEvent(NetEntity ghost, string skin) : EntityEventArgs
{
    public readonly NetEntity Ghost = ghost;
    public readonly string Skin = skin;
}

[Serializable, NetSerializable]
public sealed class GhostSkinStateEvent(NetEntity ghost, string skin, string savedSkin,
    bool hasSupporterAccess, bool saving, string? message = null) : EntityEventArgs
{
    public readonly NetEntity Ghost = ghost;
    public readonly string Skin = skin;
    public readonly string SavedSkin = savedSkin;
    public readonly bool HasSupporterAccess = hasSupporterAccess;
    public readonly bool Saving = saving;
    public readonly string? Message = message;
}
