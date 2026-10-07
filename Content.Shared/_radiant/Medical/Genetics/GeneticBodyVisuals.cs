using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Genetics;

[Serializable, NetSerializable]
public enum GeneticBodyVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum GeneticTherapyVisuals : byte { Used }

[Serializable, NetSerializable]
public enum GeneticSampleVisuals : byte { Filled }
