namespace Content.Server._radiant.Donor;

[RegisterComponent]
public sealed partial class DonorKeepsakeStyleComponent : Component
{
    [DataField] public int Style;
    [DataField] public string[] Styles = Array.Empty<string>();
}

[RegisterComponent]
public sealed partial class DonorStopwatchComponent : Component
{
    [DataField] public TimeSpan Elapsed;
    public TimeSpan? Started;
}

[RegisterComponent]
public sealed partial class DonorPhotoHolderComponent : Component
{
    [DataField] public bool Closeable;
    [DataField] public string Slot = "photo";
}

[RegisterComponent]
public sealed partial class DonorPairedBraceletComponent : Component;

