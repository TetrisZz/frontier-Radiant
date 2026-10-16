using Robust.Shared.Audio;

namespace Content.Server._radiant.Paint;

[RegisterComponent]
public sealed partial class RandomPaintColorComponent : Component
{
    [DataField(required: true)]
    public List<Color> Colors = new();

    [DataField]
    public bool Initialized;
}

[RegisterComponent]
public sealed partial class PaintWashStationComponent : Component;

[RegisterComponent]
public sealed partial class WashingMachineComponent : Component
{
    [DataField]
    public SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/_radiant/Machines/WashingMachine/wash_start.ogg");

    [DataField]
    public SoundSpecifier FinishSound = new SoundPathSpecifier("/Audio/_radiant/Machines/WashingMachine/wash_end.ogg");

    [DataField]
    public float CycleDuration = 20;

    [DataField]
    public float Remaining;
}
