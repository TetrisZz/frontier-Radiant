using Content.Shared._NF.Bank.Components;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    /// <summary>
    /// Reconnect a restored body to the attached player's existing account.
    /// Never deposits a saved amount or replaces the authoritative profile balance.
    /// If disconnected, PlayerAttached/PreferencesLoaded will refresh it later.
    /// </summary>
    public void RestoreCharacterAccount(EntityUid body)
    {
        var account = EnsureComp<BankAccountComponent>(body);
        UpdateBankBalance(body, account);
    }
}
