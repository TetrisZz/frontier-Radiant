namespace Content.Shared._radint.Cargo.Components;

[RegisterComponent]
public sealed partial class DynamicCargoMarketGridComponent : Component
{
    [DataField]
    public float CurrentMultiplier = 1f;

    [DataField]
    public bool HasMarketRate;
}