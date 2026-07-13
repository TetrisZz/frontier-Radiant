using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.Events;

[Serializable, NetSerializable]
public sealed class StationBankSetTaxMessage : BoundUserInterfaceMessage
{
    public float CargoTaxRate;
    public float AtmDepositTaxRate;
    public float VendorVatRate;
    public float ShuttleSellRate;
    public StationBankSetTaxMessage(float cargoTaxRate, float atmDepositTaxRate, float vendorVatRate = 0f,
        float shuttleSellRate = 0f)
    {
        CargoTaxRate = cargoTaxRate;
        AtmDepositTaxRate = atmDepositTaxRate;
        VendorVatRate = vendorVatRate;
        ShuttleSellRate = shuttleSellRate;
    }
}
