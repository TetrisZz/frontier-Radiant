using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Paint;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WashablePaintComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;
}

[Serializable, NetSerializable]
public sealed partial class ApplyWashablePaintEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WashPaintEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum WashingMachineVisuals : byte
{
    State,
    Layer,
}

[Serializable, NetSerializable]
public enum WashingMachineState : byte
{
    OpenEmpty,
    OpenLoaded,
    ClosedEmpty,
    ClosedLoaded,
    Running,
}
