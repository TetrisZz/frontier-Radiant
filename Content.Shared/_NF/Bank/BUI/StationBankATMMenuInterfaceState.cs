/*
 * New Frontiers - This file is licensed under AGPLv3
 * Copyright (c) 2024 New Frontiers Contributors
 * See AGPLv3.txt for details.
 */
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.BUI;

[NetSerializable, Serializable]
public sealed class StationBankATMMenuInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// bank balance of the character using the atm
    /// </summary>
    public int Balance;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// how much cash is inserted (negative values indicate that this is not valid money)
    /// </summary>
    public int Deposit;

// radiant start
    public float CargoTaxRate;
    public float AtmDepositTaxRate;
    public float VendorVatRate;
    public float ShuttleSellRate;

    public StationBankATMMenuInterfaceState(int balance, bool enabled, int deposit,
        float cargoTaxRate = 0f, float atmDepositTaxRate = 0f, float vendorVatRate = 0f,
        float shuttleSellRate = 0f)
// radiant end
    {
        Balance = balance;
        Enabled = enabled;
        Deposit = deposit;
// radiant start
        CargoTaxRate = cargoTaxRate;
        AtmDepositTaxRate = atmDepositTaxRate;
        VendorVatRate = vendorVatRate;
        ShuttleSellRate = shuttleSellRate;
// radiant end
    }
}
