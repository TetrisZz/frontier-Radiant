using Content.Shared._NF.Bank.Components;

namespace Content.Shared._NF.Bank.Components;

[RegisterComponent]
public sealed partial class SectorTaxRatesComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<SectorBankAccount, float> CargoSaleTaxRates = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<SectorBankAccount, float> AtmDepositTaxRates = new();

    /// <summary>
    /// VAT rate applied to vending machine purchases (added to the price).
    /// Single float (0-1 range) since VAT is typically a flat rate per jurisdiction.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float VendorVatRate = 0.1f;

    /// <summary>
    /// Shuttle sell rate (0-1). What % of appraised value the player gets.
    /// The remainder goes to the Frontier sector account.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float ShuttleSellRate = 0.9f;
}
