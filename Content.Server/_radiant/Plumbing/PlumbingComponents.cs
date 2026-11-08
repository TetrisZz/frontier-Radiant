namespace Content.Server._radiant.Plumbing;

[RegisterComponent]
public sealed partial class PlumbingTankComponent : Component
{
}

[RegisterComponent]
public sealed partial class LeakingPipeComponent : Component
{
    [DataField(required: true)]
    public string Port = string.Empty;
}

[RegisterComponent]
public sealed partial class PlumbedFixtureComponent : Component
{
    [DataField]
    public bool Faucet;

    [DataField]
    public bool Waste;

    [DataField]
    public bool Flush;

    [DataField]
    public float WasteBuildup;

    public bool Clogged => WasteBuildup >= 500;
}
