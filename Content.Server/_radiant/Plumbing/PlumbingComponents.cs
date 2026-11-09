namespace Content.Server._radiant.Plumbing;

[RegisterComponent]
public sealed partial class PlumbingTankComponent : Component
{
    /// <summary>Only allow pure water to be poured into this tank.</summary>
    [DataField]
    public bool WaterOnly;

    /// <summary>Save the tank entity, but omit its solution contents from shuttle snapshots.</summary>
    [DataField]
    public bool ClearContentsOnShipSave;
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
