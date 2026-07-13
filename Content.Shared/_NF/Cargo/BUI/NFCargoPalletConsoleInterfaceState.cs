using Robust.Shared.Serialization;

namespace Content.Shared._NF.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class NFCargoPalletConsoleInterfaceState(
    int appraisal,
    int count,
    bool enabled,
    float cargoTaxRate = 0f,
    int netAmount = 0) : BoundUserInterfaceState //radiant
{
    /// <summary>
    /// The estimated appraised value of all the entities on top of pallets on the same grid as the console (before tax).
    /// </summary>
    public int Appraisal = appraisal;

    /// <summary>
    /// The number of entities on top of pallets on the same grid as the console.
    /// </summary>
    public int Count = count;

    /// <summary>
    /// True if the buttons should be enabled.
    /// </summary>
    public bool Enabled = enabled;

    public float CargoTaxRate = cargoTaxRate; // radiant

    /// <summary>
    /// The amount after tax deduction.
    /// </summary>
    public int NetAmount = netAmount; // radiant
}
