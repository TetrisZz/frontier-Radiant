using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.BUI;

[NetSerializable, Serializable]
public sealed class BankATMMenuInterfaceState : BoundUserInterfaceState
{
    public int Balance;
    public bool Enabled;
    public int Deposit;
    public float AtmDepositTaxRate;

    public BankATMMenuInterfaceState(int balance, bool enabled, int deposit,
        float atmDepositTaxRate = 0f) // radiant
    {
        Balance = balance;
        Enabled = enabled;
        Deposit = deposit;
        AtmDepositTaxRate = atmDepositTaxRate; // radiant
    }
}
