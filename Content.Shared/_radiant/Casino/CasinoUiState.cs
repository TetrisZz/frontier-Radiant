using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Casino;

[Serializable, NetSerializable]
public sealed class CasinoUiState : BoundUserInterfaceState
{
    public readonly int Balance;
    public readonly int BankBalance;
    public readonly int Bet;
    public readonly CasinoSymbol First;
    public readonly CasinoSymbol Second;
    public readonly CasinoSymbol Third;
    public readonly int Payout;
    public readonly CasinoSpinResult Result;

    public CasinoUiState(
        int balance,
        int bankBalance,
        int bet,
        CasinoSymbol first,
        CasinoSymbol second,
        CasinoSymbol third,
        int payout,
        CasinoSpinResult result)
    {
        Balance = balance;
        BankBalance = bankBalance;
        Bet = bet;
        First = first;
        Second = second;
        Third = third;
        Payout = payout;
        Result = result;
    }
}

[Serializable, NetSerializable]
public enum CasinoSymbol : byte
{
    Cherry,
    Clover,
    Diamond,
    Horseshoe,
    Lemon,
    Plum,
    Seven,
}

[Serializable, NetSerializable]
public enum CasinoSpinResult : byte
{
    None,
    Loss,
    Win,
    NotEnough,
    DepositSuccess,
    WithdrawSuccess,
    BankInsufficient,
    CasinoInsufficient,
    InvalidAmount,
    BankUnavailable,
    Cooldown,
}
