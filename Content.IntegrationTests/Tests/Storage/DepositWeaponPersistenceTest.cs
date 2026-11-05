using System;
using System.IO;
using System.Linq;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Storage;

public sealed class DepositWeaponPersistenceTest
{
    [Test]
    public async Task EnergySmgsRestoreAndFailedWithdrawalsRetainAllSavedItems()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var db = pair.Server.ResolveDependency<IServerDbManager>();
            var em = pair.Server.EntMan;
            EntityUid user = default;
            EntityUid terminal = default;
            Guid owner = default;
            var slot = 0;
            await pair.Server.WaitAssertion(() =>
            {
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var session = players.Sessions.Single();
                owner = session.UserId.UserId;
                slot = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId).SelectedCharacterIndex;
                user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                players.SetAttachedEntity(session, user);
                terminal = em.SpawnEntity("SafetyDepositConsole", map.GridCoords);
            });

            foreach (var prototype in new[]
                     { "NFWeaponEnergySubMachineGunDeltaV", "NFWeaponEnergySubMachineGunDeltaVExpedition", "NFWeaponEnergySubMachineGunDisabler" })
            {
                var record = await db.PurchaseSafetyDepositBox(owner, slot, "Energy weapon test", "SafetyDepositBoxLarge");
                await db.ClearSafetyDepositBoxItems(record.BoxId, -1);
                var charge = 0f;
                EntityUid originalBox = default;
                await pair.Server.WaitAssertion(() =>
                {
                    originalBox = em.SpawnEntity("SafetyDepositBoxLarge", map.GridCoords);
                    var box = em.GetComponent<SafetyDepositBoxComponent>(originalBox);
                    box.BoxId = record.BoxId;
                    box.OwnerId = owner;
                    box.CharacterIndex = slot;
                    var weapon = em.SpawnEntity(prototype, map.GridCoords);
                    charge = em.GetComponent<BatteryComponent>(weapon).CurrentCharge;
                    Assert.That(em.System<SharedStorageSystem>().Insert(originalBox, weapon, out _, playSound: false), Is.True);
                    var console = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, console.BoxSlot, originalBox, user), Is.True);
                    for (var i = 0; i < 20; i++)
                        em.EventBus.RaiseLocalEvent(terminal, new SafetyDepositDepositMessage { Actor = user });
                });
                var deposited = false;
                for (var i = 0; i < 100; i++)
                {
                    await pair.RunTicksSync(3);
                    var saved = await db.GetSafetyDepositBox(record.BoxId);
                    if (saved?.LastWithdrawn == null && saved?.Items.Count == 1)
                    {
                        deposited = true;
                        break;
                    }
                }
                Assert.That(deposited, Is.True, "Repeated clicks must save the complete container once.");
                await pair.Server.WaitAssertion(() => Assert.That(em.EntityExists(originalBox), Is.False));
                await Withdraw(record.BoxId);
                await pair.Server.WaitAssertion(() =>
                {
                    var box = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == record.BoxId).Owner;
                    var weapon = em.GetComponent<StorageComponent>(box).Container.ContainedEntities.Single();
                    Assert.That(em.GetComponent<MetaDataComponent>(weapon).EntityPrototype!.ID, Is.EqualTo(prototype));
                    Assert.That(em.GetComponent<GunComponent>(weapon).FireRateModified, Is.GreaterThan(0));
                    Assert.That(em.GetComponent<BatteryComponent>(weapon).CurrentCharge, Is.EqualTo(charge));
                    em.DeleteEntity(box);
                });
            }

            string paperData = "";
            string oversizedData = "";
            await pair.Server.WaitAssertion(() =>
            {
                var paper = em.SpawnEntity("Paper", map.GridCoords);
                paperData = Save(paper);
                em.DeleteEntity(paper);
                var suit = em.SpawnEntity("ClothingOuterHardsuitJuggernaut", map.GridCoords);
                oversizedData = Save(suit);
                em.DeleteEntity(suit);
            });
            foreach (var failedData in new[] { oversizedData, "not a saved entity" })
            {
                var record = await db.PurchaseSafetyDepositBox(owner, slot, "Failed withdrawal test", "SafetyDepositBoxSmall");
                await db.DepositSafetyDepositBoxItems(record.BoxId, [paperData, failedData]);
                await pair.Server.WaitPost(() => SendWithdraw(record.BoxId));
                await pair.RunTicksSync(100);
                var saved = await db.GetSafetyDepositBox(record.BoxId);
                Assert.That(saved!.LastWithdrawn, Is.Null, "Failure must not mark the only saved copy as issued.");
                Assert.That(saved.Items.Select(i => i.EntityData), Is.EquivalentTo(new[] { paperData, failedData }));
                await pair.Server.WaitAssertion(() =>
                {
                    Assert.That(em.EntityQuery<SafetyDepositBoxComponent>().Any(b => b.BoxId == record.BoxId), Is.False,
                        "Do not issue a partial container on failure.");
                    Assert.That(em.EntityQuery<MetaDataComponent>().Any(m => m.EntityPrototype?.ID == "Paper"), Is.False,
                        "Clean up items already restored before the failure.");
                });

                // Once the incompatible/corrupt record is repaired, the same slot can be withdrawn.
                await db.DepositSafetyDepositBoxItems(record.BoxId, [paperData]);
                await Withdraw(record.BoxId);
                await pair.Server.WaitAssertion(() =>
                {
                    var box = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == record.BoxId).Owner;
                    Assert.That(em.GetComponent<StorageComponent>(box).Container.ContainedEntities, Has.Count.EqualTo(1));
                    em.DeleteEntity(box);
                });
            }

            // A missing database slot must not be reported as a successful deposit.
            EntityUid unsavedBox = default;
            EntityUid retainedPaper = default;
            await pair.Server.WaitAssertion(() =>
            {
                unsavedBox = em.SpawnEntity("SafetyDepositBoxSmall", map.GridCoords);
                var box = em.GetComponent<SafetyDepositBoxComponent>(unsavedBox);
                box.BoxId = Guid.NewGuid();
                box.OwnerId = owner;
                box.CharacterIndex = slot;
                retainedPaper = em.SpawnEntity("Paper", map.GridCoords);
                Assert.That(em.System<SharedStorageSystem>().Insert(unsavedBox, retainedPaper, out _, playSound: false), Is.True);
                var console = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, console.BoxSlot, unsavedBox, user), Is.True);
                em.EventBus.RaiseLocalEvent(terminal, new SafetyDepositDepositMessage { Actor = user });
            });
            await pair.RunTicksSync(100);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.EntityExists(unsavedBox), Is.True);
                Assert.That(em.EntityExists(retainedPaper), Is.True);
                Assert.That(em.GetComponent<StorageComponent>(unsavedBox).Container.ContainedEntities, Does.Contain(retainedPaper));
                em.DeleteEntity(unsavedBox);
            });
            await pair.ReallyBeIdle();
            await pair.CleanReturnAsync();

            string Save(EntityUid item)
            {
                using var writer = new StringWriter();
                Assert.That(em.System<MapLoaderSystem>().TrySaveEntity(item, writer), Is.True);
                return writer.ToString();
            }

            void SendWithdraw(Guid boxId) => em.EventBus.RaiseLocalEvent(terminal,
                new SafetyDepositWithdrawMessage(boxId) { Actor = user });

            async Task Withdraw(Guid boxId)
            {
                await pair.Server.WaitPost(() => SendWithdraw(boxId));
                var completed = false;
                for (var i = 0; i < 100; i++)
                {
                    await pair.RunTicksSync(3);
                    var saved = await db.GetSafetyDepositBox(boxId);
                    if (saved?.LastWithdrawn != null && saved.Items.Count == 0)
                    {
                        completed = true;
                        break;
                    }
                }
                Assert.That(completed, Is.True, "The complete saved container must be issued.");
            }
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}
