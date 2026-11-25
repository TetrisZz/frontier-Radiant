using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Stack;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.Events;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class BlackMarketAtmTest
{
    [Test]
    public async Task BlackMarketAllowsLargeTransactionsWithoutPassportWhileLegalAtmKeepsLimit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var account = em.EnsureComponent<BankAccountComponent>(user);
                players.SetAttachedEntity(players.Sessions.Single(), user);
                var bank = em.System<BankSystem>();
                Assert.That(bank.TryBankDeposit(user, 100000), Is.True);

                foreach (var prototype in new[]
                         { "ComputerBankATM", "ComputerBlackMarketBankATM", "ComputerWallmountBlackMarketBankATM" })
                {
                    var atm = em.SpawnEntity(prototype, map.GridCoords);
                    var component = em.GetComponent<BankATMComponent>(atm);
                    var blackMarket = prototype != "ComputerBankATM";
                    Assert.That(component.BlackMarket, Is.EqualTo(blackMarket));
                    var key = blackMarket ? BankATMMenuUiKey.BlackMarket : BankATMMenuUiKey.ATM;
                    var balance = account.Balance;

                    em.EventBus.RaiseLocalEvent(atm, new BankWithdrawMessage(20000) { Actor = user, UiKey = key });
                    Assert.That(account.Balance, Is.EqualTo(balance - (blackMarket ? 20000 : 0)), prototype);

                    balance = account.Balance;
                    var cash = em.SpawnEntity("SpaceCash", map.GridCoords);
                    em.System<StackSystem>().SetCount(cash, 20000);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(atm, component.CashSlot, cash, user), Is.True);
                    em.EventBus.RaiseLocalEvent(atm, new BankDepositMessage { Actor = user, UiKey = key });
                    // Both black-market models retain their configured 15% deposit fee.
                    Assert.That(account.Balance, Is.EqualTo(balance + (blackMarket ? 17000 : 0)), prototype);
                    Assert.That(component.CashSlot.ContainerSlot!.ContainedEntity,
                        blackMarket ? Is.Null : Is.EqualTo(cash), prototype);

                    balance = account.Balance;
                    foreach (var invalid in new[] { 0, -1, balance + 1 })
                    {
                        em.EventBus.RaiseLocalEvent(atm, new BankWithdrawMessage(invalid) { Actor = user, UiKey = key });
                        Assert.That(account.Balance, Is.EqualTo(balance), prototype);
                    }
                    em.DeleteEntity(atm);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
