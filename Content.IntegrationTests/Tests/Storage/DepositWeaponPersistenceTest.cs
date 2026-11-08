using System;
using System.Collections.Generic;
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
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

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

            // The small box has just one 4x4 compartment. Auto-inserting even one
            // sheet of paper before the suit takes away the suit's only position.
            foreach (var legacy in new[] { false, true })
            {
                var record = await db.PurchaseSafetyDepositBox(owner, slot, "Full box test", "SafetyDepositBoxSmall");
                var data = new List<string>();
                var pocketLocations = new[]
                {
                    new Vector2i(0, 5), new Vector2i(1, 5), new Vector2i(2, 5),
                    new Vector2i(0, 7), new Vector2i(1, 7), new Vector2i(2, 7),
                    new Vector2i(4, 5), new Vector2i(4, 6), new Vector2i(4, 7),
                };
                var expectedLocations = pocketLocations
                    .Select(position => new ItemStorageLocation(Angle.FromDegrees(90), position))
                    .Append(new ItemStorageLocation(Angle.Zero, Vector2i.Zero))
                    .ToArray();
                if (!legacy)
                    await db.ClearSafetyDepositBoxItems(record.BoxId, -1);
                await pair.Server.WaitAssertion(() =>
                {
                    var originalBox = em.SpawnEntity("SafetyDepositBoxSmall", map.GridCoords);
                    var box = em.GetComponent<SafetyDepositBoxComponent>(originalBox);
                    box.BoxId = record.BoxId;
                    box.OwnerId = owner;
                    box.CharacterIndex = slot;
                    var storage = em.GetComponent<StorageComponent>(originalBox);
                    for (var i = 0; i < expectedLocations.Length; i++)
                    {
                        var item = em.SpawnEntity(i < pocketLocations.Length ? "Paper" : "ClothingOuterHardsuitScaf", map.GridCoords);
                        Assert.That(em.System<SharedStorageSystem>().InsertAt((originalBox, storage), item,
                            expectedLocations[i], out _, playSound: false, stackAutomatically: false), Is.True);
                        if (legacy)
                            data.Add(Save(item));
                    }

                    if (legacy)
                    {
                        em.DeleteEntity(originalBox);
                        return;
                    }

                    var console = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, console.BoxSlot, originalBox, user), Is.True);
                    em.EventBus.RaiseLocalEvent(terminal, new SafetyDepositDepositMessage { Actor = user });
                });
                if (legacy)
                    await db.DepositSafetyDepositBoxItems(record.BoxId, data);
                else
                {
                    for (var i = 0; i < 100; i++)
                    {
                        await pair.RunTicksSync(3);
                        if ((await db.GetSafetyDepositBox(record.BoxId)) is { LastWithdrawn: null, Items.Count: 10 })
                            break;
                    }
                    Assert.That((await db.GetSafetyDepositBox(record.BoxId))!.Items.Count, Is.EqualTo(10));
                }

                await Withdraw(record.BoxId);
                await pair.Server.WaitAssertion(() =>
                {
                    var restored = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == record.BoxId).Owner;
                    var storage = em.GetComponent<StorageComponent>(restored);
                    Assert.That(storage.Container.ContainedEntities, Has.Count.EqualTo(10));
                    Assert.That(storage.Container.ContainedEntities.Any(uid =>
                        em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ClothingOuterHardsuitScaf"), Is.True);
                    if (!legacy)
                        Assert.That(storage.StoredItems.Values, Is.EquivalentTo(expectedLocations));
                    em.DeleteEntity(restored);
                });
            }

            string paperData = "";
            string oversizedData = "";
            string scafData = "";
            await pair.Server.WaitAssertion(() =>
            {
                var paper = em.SpawnEntity("Paper", map.GridCoords);
                paperData = Save(paper);
                em.DeleteEntity(paper);
                var suit = em.SpawnEntity("ClothingOuterHardsuitJuggernaut", map.GridCoords);
                oversizedData = Save(suit);
                em.DeleteEntity(suit);
                var scaf = em.SpawnEntity("ClothingOuterHardsuitScaf", map.GridCoords);
                scafData = Save(scaf);
                em.DeleteEntity(scaf);
            });

            var overflowRecord = await db.PurchaseSafetyDepositBox(owner, slot, "Overflow test", "SafetyDepositBoxSmall");
            await db.DepositSafetyDepositBoxItems(overflowRecord.BoxId, [paperData, scafData, scafData, oversizedData]);
            await Withdraw(overflowRecord.BoxId);
            await pair.Server.WaitPost(() => SendWithdraw(overflowRecord.BoxId));
            await pair.RunTicksSync(100);
            await pair.Server.WaitAssertion(() =>
            {
                var box = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == overflowRecord.BoxId).Owner;
                var stored = em.GetComponent<StorageComponent>(box).Container.ContainedEntities;
                Assert.That(stored.Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID),
                    Is.EquivalentTo(new[] { "Paper", "ClothingOuterHardsuitScaf" }));
                var floorItems = em.EntityQuery<MetaDataComponent>().Where(meta =>
                    meta.EntityPrototype?.ID is "ClothingOuterHardsuitScaf" or "ClothingOuterHardsuitJuggernaut")
                    .Select(meta => meta.Owner).Where(uid => !stored.Contains(uid)).ToArray();
                Assert.That(floorItems.Length, Is.EqualTo(2), "Only the two unplaceable items should drop, with no duplicates.");
                foreach (var item in floorItems)
                {
                    Assert.That(em.System<SharedContainerSystem>().IsEntityInContainer(item), Is.False);
                    Assert.That(em.GetComponent<TransformComponent>(item).Coordinates,
                        Is.EqualTo(em.GetComponent<TransformComponent>(user).Coordinates));
                    em.DeleteEntity(item);
                }
                em.DeleteEntity(box);
            });

            {
                const string failedData = "not a saved entity";
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

                // Once the unreadable record is repaired, the same slot can be withdrawn.
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
