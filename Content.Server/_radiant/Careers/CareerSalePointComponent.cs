namespace Content.Server._radiant.Careers;

/// <summary>Explicit career sale location on a console, grid, or station.</summary>
[RegisterComponent]
public sealed partial class CareerSalePointComponent : Component
{
    [DataField]
    public string Location = "Trade";
}
