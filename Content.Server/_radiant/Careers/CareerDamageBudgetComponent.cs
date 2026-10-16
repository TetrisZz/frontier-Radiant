namespace Content.Server._radiant.Careers;

/// <summary>Lifetime damage credit shared by every attacker, including after healing or revival.</summary>
[RegisterComponent]
public sealed partial class CareerDamageBudgetComponent : Component
{
    [DataField]
    public double OriginalBudget;

    [DataField]
    public double CreditedDamage;
}
