using System;
using Content.Server._radiant.Donor;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Store;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class DonorUplinkTest
{
    [Test]
    public async Task OwnerBindingGuardsOpeningAndEveryStoreMessage()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var players = await pair.Server.AddDummySessions(2);
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                var minds = entities.System<SharedMindSystem>();
                var system = entities.System<DonorUplinkSystem>();
                var ui = entities.System<UserInterfaceSystem>();
                var owner = entities.SpawnEntity("MobHuman", map.GridCoords);
                var stranger = entities.SpawnEntity("MobHuman", map.GridCoords);
                var npc = entities.SpawnEntity("MobHuman", map.GridCoords);
                var ownerMind = minds.CreateMind(players[0].UserId);
                minds.TransferTo(ownerMind, owner);
                var strangerMind = minds.CreateMind(players[1].UserId);
                minds.TransferTo(strangerMind, stranger);

                foreach (var prototype in new[] { "BaseHomUplinkRadio", "BaseHomUplinkRadio50", "BaseHomUplinkRadio100", "BaseHomUplinkRadioPearl" })
                {
                    var uid = entities.SpawnEntity(prototype, map.GridCoords);
                    var comp = entities.GetComponent<DonorUplinkComponent>(uid);
                    Entity<DonorUplinkComponent> ent = (uid, comp);
                    Assert.That(system.CanUse(ent, npc), Is.False);
                    Assert.That(comp.Owner, Is.Null, "NPCs must not claim unbound uplinks");
                    Assert.That(system.CanUse(ent, owner), Is.True);
                    Assert.That(comp.Owner, Is.EqualTo(players[0].UserId));
                    Assert.That(system.CanUse(ent, stranger), Is.False);
                    Assert.That(ui.TryOpenUi(uid, StoreUiKey.Key, stranger), Is.False, "Direct server opens must also be rejected");
                    Assert.That(ui.IsUiOpen(uid, StoreUiKey.Key, stranger), Is.False);
                    var open = new ActivatableUIOpenAttemptEvent(stranger);
                    entities.EventBus.RaiseLocalEvent(uid, open);
                    Assert.That(open.Cancelled, Is.True);

                    foreach (var message in new BoundUserInterfaceMessage[]
                    {
                        new OpenBoundInterfaceMessage(),
                        new StoreRequestUpdateInterfaceMessage(),
                        new StoreBuyListingMessage("UplinkHomDonorDecisionToken"),
                        new StoreRequestWithdrawMessage("Cristalhom", 1),
                        new StoreRequestRefundMessage(),
                    })
                    {
                        var denied = new BoundUserInterfaceMessageAttempt(stranger, uid, StoreUiKey.Key, message);
                        entities.EventBus.RaiseLocalEvent(uid, denied);
                        Assert.That(denied.Cancelled, Is.True, message.GetType().Name);
                        var allowed = new BoundUserInterfaceMessageAttempt(owner, uid, StoreUiKey.Key, message);
                        entities.EventBus.RaiseLocalEvent(uid, allowed);
                        Assert.That(allowed.Cancelled, Is.False, message.GetType().Name);
                    }
                    Assert.That(ui.TryOpenUi(uid, StoreUiKey.Key, owner), Is.True);
                    ui.CloseUi(uid, StoreUiKey.Key, owner);
                }

                var issued = entities.SpawnEntity("BaseHomUplinkRadio", map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(owner, issued), Is.True);
                Assert.That(entities.GetComponent<DonorUplinkComponent>(issued).Owner, Is.EqualTo(players[0].UserId),
                    "The first holder is bound before opening the shop");
                var replacementBody = entities.SpawnEntity("MobHuman", map.GridCoords);
                minds.TransferTo(ownerMind, replacementBody);
                Assert.That(system.CanUse((issued, entities.GetComponent<DonorUplinkComponent>(issued)), replacementBody), Is.True,
                    "Ownership follows the account, not a particular body");

                var bag = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(replacementBody, bag), Is.True);
                var nested = entities.SpawnEntity("BaseHomUplinkRadio", map.GridCoords);
                Assert.That(entities.System<SharedStorageSystem>().Insert(bag, nested, out _, playSound: false), Is.True);
                Assert.That(entities.GetComponent<DonorUplinkComponent>(nested).Owner, Is.EqualTo(players[0].UserId));

                var token = entities.SpawnEntity("DonorDecisionToken", map.GridCoords);
                var tokenComp = entities.GetComponent<DonorTrinketComponent>(token);
                var use = new UseInHandEvent(replacementBody);
                entities.EventBus.RaiseLocalEvent(token, use);
                Assert.That(use.Handled, Is.True);
                Assert.That(tokenComp.NextUse, Is.GreaterThan(TimeSpan.Zero));
                var cooldown = tokenComp.NextUse;
                entities.EventBus.RaiseLocalEvent(token, new UseInHandEvent(replacementBody));
                Assert.That(tokenComp.NextUse, Is.EqualTo(cooldown));
                TestContext.Out.WriteLine("Donor uplink ownership and trinket assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Donor uplink regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
