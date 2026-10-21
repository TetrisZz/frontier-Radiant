using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Social;

[RegisterComponent]
public sealed partial class SaunaHeaterComponent : Component
{
    [DataField] public float FuelSeconds;
    [DataField] public float MaximumFuelSeconds = 1200;
    [DataField] public bool Lit;
    [DataField] public float TargetTemperature = 323.15f;
    public float Accumulator;
}

[Serializable, NetSerializable]
public enum SaunaVisuals : byte { Lit }

[RegisterComponent]
public sealed partial class QuestionDeckComponent : Component
{
    [DataField] public List<LocId> Cards = new();
    [DataField] public bool Blank;
    [DataField] public int BlankCards = 20;
    [DataField] public List<LocId>? Remaining;
}
