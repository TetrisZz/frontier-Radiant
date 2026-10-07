using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Genetics;

[Serializable, NetSerializable]
public sealed partial class GeneticTherapyDoAfterEvent : SimpleDoAfterEvent;
