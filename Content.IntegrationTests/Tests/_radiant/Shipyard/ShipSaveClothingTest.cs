using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Timing;
using Robust.Shared.Timing;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipSaveClothingTest
{
    [TestCase("ClothingOuterHardsuitEngineering", false, false)]
    [TestCase("ClothingOuterHardsuitEngineering", true, false)]
    [TestCase("ClothingOuterHardsuitNfsdCombat", false, true)]
    public async Task LoadedSuitCanDeployAndRetractHelmetAndLockerCanOpen(string suitPrototype, bool missingAction, bool legacyCooldown)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var storage = entities.System<SharedStorageSystem>();
            var stash = entities.SpawnEntity("MilitaryStorageStash", map.GridCoords);
            var suit = entities.SpawnEntity(suitPrototype, map.GridCoords);
            var toggle = entities.GetComponent<ToggleableClothingComponent>(suit);
            if (missingAction)
                entities.DeleteEntity(toggle.ActionEntity!.Value);
            else
                entities.System<SharedActionsSystem>().SetCooldown(toggle.ActionEntity, System.TimeSpan.FromHours(8));
            Assert.That(storage.Insert(stash, suit, out _, playSound: false), Is.True);

            var locker = entities.SpawnEntity("LockerWallEVAColorAtmosTechFilled", map.GridCoords);
            entities.System<LockSystem>().Unlock(locker, null);
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var delays = entities.System<UseDelaySystem>();
            // Simulate a ship saved many hours into the previous round. New-round time is
            // below these deadlines; a same-round load without these values missed the bug.
            foreach (var item in new[] { suit, locker, toggle.ClothingUid!.Value })
            {
                delays.SetLength(item, System.TimeSpan.FromSeconds(1));
                Assert.That(delays.TryGetDelayInfo(item, out var info), Is.True);
                info.StartTime = timing.CurTime + System.TimeSpan.FromHours(8);
                info.EndTime = info.StartTime + info.Length;
                Assert.That(delays.IsDelayed(item), Is.True);
            }
            // Simulate legacy saves that stripped the UI component.
            entities.RemoveComponent<UserInterfaceComponent>(locker);
            Assert.That(entities.System<ShipyardGridSaveSystem>()
                .TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            Assert.That(delays.IsDelayed(suit), Is.True, "Saving must not alter live timers.");
            if (!missingAction)
                Assert.That(entities.System<SharedActionsSystem>().IsCooldownActive(
                    entities.System<SharedActionsSystem>().GetAction(toggle.ActionEntity)!.Value.Comp), Is.True,
                    "Saving must not change the live helmet action cooldown.");
            if (legacyCooldown)
            {
                // Match the real player's older Firefighter save: the helmet action carries
                // mapInit plus a positive relative cooldown, which gets rebased in a new round.
                var legacy = new YamlStream();
                legacy.Load(new StringReader(yaml!));
                var root = (YamlMappingNode) legacy.Documents[0].RootNode;
                foreach (var group in (YamlSequenceNode) root.Children[new YamlScalarNode("entities")])
                {
                    var prototype = (YamlMappingNode) group;
                    if (!prototype.Children.TryGetValue(new YamlScalarNode("proto"), out var id)
                        || ((YamlScalarNode) id).Value != "ActionToggleSuitPiece")
                        continue;
                    foreach (YamlMappingNode entry in (YamlSequenceNode) prototype.Children[new YamlScalarNode("entities")])
                    {
                        entry.Children[new YamlScalarNode("mapInit")] = new YamlScalarNode("true");
                        foreach (YamlMappingNode comp in (YamlSequenceNode) entry.Children[new YamlScalarNode("components")])
                        {
                            if (((YamlScalarNode) comp.Children[new YamlScalarNode("type")]).Value != "Action")
                                continue;
                            comp.Children[new YamlScalarNode("cooldown")] = new YamlMappingNode(
                                "start", "139.0665276", "end", "140.0665276");
                            comp.Children[new YamlScalarNode("attachedEntity")] = new YamlScalarNode("invalid");
                        }
                    }
                }
                using var writer = new StringWriter();
                legacy.Save(new YamlMappingFix(new Emitter(writer)), false);
                yaml = writer.ToString();
            }
            var signed = entities.System<ShipSaveSigningSystem>().Sign(yaml!, 0);
            var file = AuthenticatedShipFile.FromShipFile(signed.ShipFileString());
            // Remove the live originals: no entity reference may rely on the old world.
            entities.DeleteEntity(stash);
            entities.DeleteEntity(locker);
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(file.ShipYamlString(), out _));
            Assert.That(entities.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader,
                "saved suit and locker", out var loaded, offset: new Vector2(20, 20)), Is.True);

            var loadedStash = entities.EntityQuery<StorageComponent, TransformComponent, MetaDataComponent>()
                .Single(entry => entry.Item2.GridUid == loaded.Value.Owner
                    && entry.Item3.EntityPrototype?.ID == "MilitaryStorageStash").Item1;
            var loadedSuit = loadedStash.StoredItems.Keys.Single();
            var loadedToggle = entities.GetComponent<ToggleableClothingComponent>(loadedSuit);
            Assert.That(entities.EntityExists(loadedToggle.ActionEntity), Is.True, "The helmet button must exist after loading.");
            Assert.That(loadedToggle.Container!.ContainedEntity, Is.EqualTo(loadedToggle.ClothingUid));
            Assert.That(delays.IsDelayed(loadedSuit), Is.False, "The previous round must not block using the suit.");
            Assert.That(delays.IsDelayed(loadedToggle.ClothingUid.Value), Is.False);
            Assert.That(entities.GetComponent<AttachedClothingComponent>(loadedToggle.ClothingUid!.Value).AttachedUid,
                Is.EqualTo(loadedSuit));
            var user = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(loadedSuit).Coordinates);
            var inventory = entities.System<InventorySystem>();
            Assert.That(inventory.TryEquip(user, loadedSuit, "outerClothing", force: true), Is.True);
            var actions = entities.System<SharedActionsSystem>();
            var action = actions.GetAction(loadedToggle.ActionEntity);
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.Value.Comp.Cooldown, Is.Null, "The saved helmet action cooldown must be cleared.");
            Assert.That(actions.GetActions(user).Any(entry => entry.Owner == loadedToggle.ActionEntity), Is.True,
                "The helmet button must be granted to the new wearer.");
            Assert.That(actions.GetEvent(action!.Value), Is.TypeOf<ToggleClothingEvent>(),
                "The saved button must retain the actual helmet activation event.");
            Assert.That(actions.ValidAction(action.Value), Is.True,
                "The helmet button must not be blocked by a previous-round action cooldown.");
            actions.PerformAction(user, action.Value);
            Assert.That(inventory.TryGetSlotEntity(user, "head", out var helmet), Is.True);
            Assert.That(helmet, Is.EqualTo(loadedToggle.ClothingUid));
            actions.RemoveCooldown(action.Value);
            actions.PerformAction(user, action.Value);
            Assert.That(loadedToggle.Container.ContainedEntity, Is.EqualTo(helmet));

            var loadedLocker = entities.EntityQuery<StorageComponent, TransformComponent, MetaDataComponent>()
                .Single(entry => entry.Item2.GridUid == loaded.Value.Owner
                    && entry.Item3.EntityPrototype?.ID == "LockerWallEVAColorAtmosTechFilled").Item1;
            Assert.That(delays.IsDelayed(loadedLocker.Owner), Is.False);
            Assert.That(entities.System<SharedInteractionSystem>()
                .InteractionActivate(user, loadedLocker.Owner), Is.True,
                "Opening must pass the normal interaction gate, including UseDelay.");
            Assert.That(entities.System<SharedUserInterfaceSystem>()
                .IsUiOpen(loadedLocker.Owner, StorageComponent.StorageUiKey.Key, user), Is.True);
            TestContext.Out.WriteLine($"Saved helmet deploy/retract and locker open assertions passed (missingAction={missingAction}).");
            entities.DeleteEntity(loaded.Value.Owner);
        });
        await pair.CleanReturnAsync();
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine($"Ship clothing regression failure: {exception}");
            throw;
        }
    }
}
