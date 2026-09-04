using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Scent.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SmellerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Sniffing;

    [DataField, AutoNetworkedField]
    public string? TrackedScentId;

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleSniff";

    [DataField]
    public EntityUid? ToggleActionEntity;

    [DataField]
    public EntProtoId TrackAction = "ActionTrackScent";

    [DataField]
    public EntityUid? TrackActionEntity;

    [DataField]
    public EntProtoId ClearAction = "ActionClearTrackedScent";

    [DataField]
    public EntityUid? ClearActionEntity;

    [DataField, AutoNetworkedField]
    public ScentPerception Perception = ScentPerception.Full;
}

public enum ScentPerception : byte
{
    Full,
    Partial,
}
