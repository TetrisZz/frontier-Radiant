using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.IV;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class IVDripComponent : Component
{
    [AutoNetworkedField] public EntityUid? AttachedTo;
    [DataField, AutoNetworkedField] public bool Injecting = true;
    [DataField, AutoNetworkedField] public FixedPoint2 TransferAmount = 5;
    // Collection expressions may emit CollectionsMarshal.SetCount, which the client sandbox forbids.
    [DataField] public List<FixedPoint2> TransferAmounts = new() { 1, 5, 10, 15 };
    [DataField] public TimeSpan TransferInterval = TimeSpan.FromSeconds(3);
    [DataField] public float Range = 2;
    [DataField] public string Slot = "iv_bag";
    [DataField, AutoNetworkedField] public bool Telescopic;
    [AutoNetworkedField] public bool HasBag;
    [AutoNetworkedField] public bool Flowing;
    [AutoNetworkedField] public int FillPercentage;
    [AutoNetworkedField] public Color FillColor = Color.White;
    [AutoPausedField] public TimeSpan NextTransfer;
    public EntityUid? Operator;
}

[RegisterComponent]
public sealed partial class IVBagComponent : Component
{
    [DataField] public string Solution = "iv_bag";
}

[Serializable, NetSerializable]
public enum IVDripVisuals : byte
{
    Stand,
    Liquid,
}
