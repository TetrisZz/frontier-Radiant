using Content.Shared.DoAfter;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Paperwork;

/// <summary>Enables voluntary body photocopies and configures their species-specific paper prototypes.</summary>
[RegisterComponent]
public sealed partial class PhotocopierBodyScannerComponent : Component
{
    [DataField(required: true)] public EntProtoId MalePrint;
    [DataField(required: true)] public EntProtoId FemalePrint;
    [DataField] public EntProtoId? SiliconPrint;
    [DataField] public Dictionary<ProtoId<SpeciesPrototype>, EntProtoId> SpeciesPrints = new();
    [DataField] public TimeSpan Delay = TimeSpan.FromSeconds(3);
}

/// <summary>The image belongs to the prototype, so copying and faxing must retain that prototype.</summary>
[RegisterComponent]
public sealed partial class BodyPhotocopyComponent : Component;

[Serializable, NetSerializable]
public sealed partial class PhotocopierBodyScanDoAfterEvent : SimpleDoAfterEvent;
