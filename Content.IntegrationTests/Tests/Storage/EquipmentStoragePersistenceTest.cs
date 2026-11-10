using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Storage;
using Content.Shared.Stacks;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Timing;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests.Storage;

public sealed class EquipmentStoragePersistenceTest
{
    [TestCase(false, "ClothingOuterHardsuitScaf")]
    [TestCase(true, "ClothingOuterHardsuitEngineering")]
    public async Task SuitLightHelmetAndTankActionsLoseOldCooldownsAndNewCooldownsExpireOnBothPeers(bool deposit, string suitPrototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            const string tankDelayId = SharedGasTankSystem.GasTankDelay;
            var defaultLength = TimeSpan.FromSeconds(1.5);
            var tankLength = TimeSpan.FromSeconds(0.75);
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            var delays = em.System<UseDelaySystem>();
            var db = pair.Server.ResolveDependency<IServerDbManager>();
            EntityUid user = default, suit = default, tank = default, rped = default, terminal = default, box = default;
            Guid owner = default, boxId = default;
            var slot = 0;
            string ship = null!;
            await pair.Server.WaitAssertion(() =>
            {
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var session = players.Sessions.Single();
                owner = session.UserId.UserId;
                slot = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId).SelectedCharacterIndex;
                user = em.SpawnEntity("MobHuman", map.GridCoords);
                players.SetAttachedEntity(session, user);
                suit = em.SpawnEntity(suitPrototype, map.GridCoords);
                tank = em.SpawnEntity("OxygenTankFilled", map.GridCoords);
                delays.SetLength(tank, defaultLength);
                delays.SetLength(tank, tankLength, tankDelayId);
                Assert.That(delays.TryResetDelay(tank), Is.True);
                Assert.That(delays.TryResetDelay(tank, id: tankDelayId), Is.True);
                // Check the raw entity snapshot before either save path applies its legacy cleanup.
                using (var writer = new StringWriter())
                {
                    Assert.That(em.System<MapLoaderSystem>().TrySaveEntity(tank, writer), Is.True);
                    Assert.That(writer.ToString(), Does.Not.Contain("startTime:").And.Not.Contain("endTime:"));
                }
                if (!deposit)
                {
                    rped = em.SpawnEntity("RPED", map.GridCoords);
                    var part = em.SpawnEntity("SuperMatterBinStockPart", map.GridCoords);
                    em.System<StackSystem>().SetCount(part, 7);
                    Assert.That(em.System<SharedStorageSystem>().Insert(rped, part, out _, playSound: false), Is.True);
                }
                var getActions = new GetItemActionsEvent(em.System<ActionContainerSystem>(), user, tank);
                em.EventBus.RaiseLocalEvent(tank, getActions);
                var actions = em.System<SharedActionsSystem>();
                var toggle = em.GetComponent<ToggleableClothingComponent>(suit);
                var light = GetSuitLight();
                actions.SetCooldown(toggle.ActionEntity, TimeSpan.FromHours(8));
                actions.SetCooldown(em.GetComponent<GasTankComponent>(tank).ToggleActionEntity, TimeSpan.FromHours(8));
                Assert.That(light.ToggleActionEntity, Is.Not.Null);
                Assert.That(light.SelfToggleActionEntity, Is.Not.Null);
                actions.SetCooldown(light.ToggleActionEntity, TimeSpan.FromHours(8));
                actions.SetCooldown(light.SelfToggleActionEntity, TimeSpan.FromHours(8));
                if (deposit)
                {
                    terminal = em.SpawnEntity("SafetyDepositConsole", map.GridCoords);
                    box = em.SpawnEntity("SafetyDepositBoxLarge", map.GridCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(box, suit, out _, playSound: false), Is.True);
                    Assert.That(em.System<SharedStorageSystem>().Insert(box, tank, out _, playSound: false), Is.True);
                }
                else
                {
                    var stash = em.SpawnEntity("MilitaryStorageStash", map.GridCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(stash, suit, out _, playSound: false), Is.True);
                    Assert.That(em.System<SharedStorageSystem>().Insert(stash, tank, out _, playSound: false), Is.True);
                    var civilian = em.SpawnEntity("CivilianStorageStash", map.GridCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(civilian, rped, out _, playSound: false), Is.True);
                    Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                    ship = yaml!;
                    Assert.That(actions.IsCooldownActive(actions.GetAction(em.GetComponent<GasTankComponent>(tank).ToggleActionEntity)!.Value.Comp), Is.True,
                        "Saving must not clear the live item's cooldown.");
                    Assert.That(actions.IsCooldownActive(actions.GetAction(light.ToggleActionEntity)!.Value.Comp), Is.True);
                    em.DeleteEntity(stash);
                    em.DeleteEntity(civilian);
                    using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(ship, out _));
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved equipment", out _,
                        offset: new Vector2(20, 0)), Is.True);
                }
            });

            if (deposit)
            {
                var record = await db.PurchaseSafetyDepositBox(owner, slot, "Equipment test", "SafetyDepositBoxLarge");
                boxId = record.BoxId;
                await db.ClearSafetyDepositBoxItems(boxId, -1);
                await pair.Server.WaitAssertion(() =>
                {
                    var comp = em.GetComponent<SafetyDepositBoxComponent>(box);
                    comp.BoxId = boxId;
                    comp.OwnerId = owner;
                    comp.CharacterIndex = slot;
                    var console = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, console.BoxSlot, box, user), Is.True);
                    em.EventBus.RaiseLocalEvent(terminal, new SafetyDepositDepositMessage { Actor = user });
                });
                for (var i = 0; i < 100; i++)
                {
                    await pair.RunTicksSync(3);
                    if ((await db.GetSafetyDepositBox(boxId)) is { LastWithdrawn: null, Items.Count: 2 })
                        break;
                }
                var stored = await db.GetSafetyDepositBox(boxId);
                Assert.That(stored!.Items.Count, Is.EqualTo(2));
                // Old database records also need repairing on read, not just new deposits.
                var legacy = stored.Items.Select(item => WithOldCooldown(item.EntityData)).ToList();
                Assert.That(legacy.Count(data => data.Contains("start: 28799")), Is.EqualTo(2));
                await db.DepositSafetyDepositBoxItems(boxId, legacy);
                await pair.Server.WaitPost(() => em.EventBus.RaiseLocalEvent(terminal,
                    new SafetyDepositWithdrawMessage(boxId) { Actor = user }));
                for (var i = 0; i < 100; i++)
                {
                    await pair.RunTicksSync(3);
                    if ((await db.GetSafetyDepositBox(boxId)) is { LastWithdrawn: not null, Items.Count: 0 })
                        break;
                }
                Assert.That((await db.GetSafetyDepositBox(boxId))!.LastWithdrawn, Is.Not.Null);
            }

            NetEntity helmetAction = default, tankAction = default, lightAction = default, rpedEntity = default;
            await pair.Server.WaitAssertion(() =>
            {
                suit = em.EntityQuery<MetaDataComponent>().Single(m => m.EntityPrototype?.ID == suitPrototype).Owner;
                tank = em.EntityQuery<MetaDataComponent>().Single(m => m.EntityPrototype?.ID == "OxygenTankFilled").Owner;
                Assert.That(delays.IsDelayed(tank), Is.False);
                Assert.That(delays.IsDelayed(tank, tankDelayId), Is.False);
                Assert.That(delays.TryGetDelayInfo(tank, out var defaultDelay), Is.True);
                Assert.That(defaultDelay!.Length, Is.EqualTo(defaultLength));
                Assert.That(delays.TryGetDelayInfo(tank, out var namedDelay, tankDelayId), Is.True);
                Assert.That(namedDelay!.Length, Is.EqualTo(tankLength));
                var inventory = em.System<InventorySystem>();
                Assert.That(inventory.TryEquip(user, suit, "outerClothing", force: true), Is.True);
                Assert.That(inventory.TryEquip(user, tank, "suitstorage", force: true), Is.True);
                var mask = em.SpawnEntity("ClothingMaskBreath", map.GridCoords);
                Assert.That(inventory.TryEquip(user, mask, "mask", force: true), Is.True);
                var toggle = em.GetComponent<ToggleableClothingComponent>(suit);
                var gas = em.GetComponent<GasTankComponent>(tank);
                var light = GetSuitLight();
                Assert.That(gas.Air.TotalMoles, Is.GreaterThan(0));
                helmetAction = em.GetNetEntity(toggle.ActionEntity!.Value);
                tankAction = em.GetNetEntity(gas.ToggleActionEntity!.Value);
                lightAction = em.GetNetEntity(light.ToggleActionEntity!.Value);
                foreach (var id in new[] { toggle.ActionEntity, gas.ToggleActionEntity, light.ToggleActionEntity, light.SelfToggleActionEntity })
                    Assert.That(em.System<SharedActionsSystem>().GetAction(id)!.Value.Comp.Cooldown, Is.Null);
                Assert.That(em.System<SharedActionsSystem>().GetAction(light.SelfToggleActionEntity)!.Value.Comp.AttachedEntity,
                    Is.EqualTo(light.Owner));
                if (!deposit)
                {
                    rped = em.EntityQuery<MetaDataComponent>().Single(m => m.EntityPrototype?.ID == "RPED").Owner;
                    var contents = em.GetComponent<StorageComponent>(rped).Container.ContainedEntities;
                    Assert.That(contents.Count, Is.EqualTo(1));
                    Assert.That(em.GetComponent<StackComponent>(contents.Single()).Count, Is.EqualTo(7));
                    var containers = em.System<SharedContainerSystem>();
                    Assert.That(containers.TryGetContainingContainer(rped, out var container), Is.True);
                    Assert.That(containers.Remove(rped, container!, force: true), Is.True);
                    em.System<SharedTransformSystem>().SetCoordinates(rped, em.GetComponent<TransformComponent>(user).Coordinates);
                    Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, rped), Is.True);
                    em.System<SharedStorageSystem>().OpenStorageUI(rped, user);
                    rpedEntity = em.GetNetEntity(rped);
                }
            });
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
            {
                var client = pair.Client.EntMan;
                foreach (var id in new[] { helmetAction, tankAction })
                    Assert.That(client.GetComponent<ActionComponent>(client.GetEntity(id)).Cooldown, Is.Null,
                        "The client must not display the old red cooldown circle.");
                if (!deposit)
                {
                    var contents = client.GetComponent<StorageComponent>(client.GetEntity(rpedEntity));
                    Assert.That(contents.StoredItems.Count, Is.EqualTo(1), "RPED inventory must be visible on the client.");
                    Assert.That(contents.Container.ContainedEntities.Count, Is.EqualTo(1));
                    Assert.That(client.GetComponent<StackComponent>(contents.Container.ContainedEntities.Single()).Count, Is.EqualTo(7));
                }
            });
            await pair.Server.WaitAssertion(() =>
            {
                var actions = em.System<SharedActionsSystem>();
                actions.PerformAction(user, actions.GetAction(em.GetEntity(helmetAction))!.Value);
                actions.PerformAction(user, actions.GetAction(em.GetEntity(tankAction))!.Value);
                Assert.That(em.GetComponent<GasTankComponent>(tank).IsConnected, Is.True);
                Assert.That(delays.IsDelayed(tank, tankDelayId), Is.True);
                Assert.That(delays.TryGetDelayInfo(tank, out var namedDelay, tankDelayId), Is.True);
                Assert.That(namedDelay!.EndTime - pair.Server.ResolveDependency<IGameTiming>().CurTime, Is.EqualTo(tankLength));
                Assert.That(em.System<InventorySystem>().TryGetSlotEntity(user, "head", out var helmet), Is.True);
                Assert.That(helmet, Is.EqualTo(em.GetComponent<ToggleableClothingComponent>(suit).ClothingUid));
                actions.PerformAction(user, actions.GetAction(em.GetEntity(lightAction))!.Value);
                Assert.That(GetSuitLight().Activated, Is.True);
            });
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
                Assert.That(pair.Client.EntMan.GetComponent<ActionComponent>(pair.Client.EntMan.GetEntity(lightAction)).Cooldown,
                    Is.Not.Null, "The working light button still has its normal short cooldown."));
            await pair.RunTicksSync(120);
            await pair.Server.WaitAssertion(() =>
            {
                var actions = em.System<SharedActionsSystem>();
                Assert.That(delays.IsDelayed(tank, tankDelayId), Is.False);
                foreach (var id in new[] { helmetAction, tankAction, lightAction })
                    Assert.That(actions.IsCooldownActive(actions.GetAction(em.GetEntity(id))!.Value.Comp), Is.False);
            });
            await pair.Client.WaitAssertion(() =>
            {
                var actions = pair.Client.EntMan.System<SharedActionsSystem>();
                foreach (var id in new[] { helmetAction, tankAction, lightAction })
                    Assert.That(actions.IsCooldownActive(actions.GetAction(pair.Client.EntMan.GetEntity(id))!.Value.Comp), Is.False,
                        "A new one-second cooldown must expire on the client too.");
            });
            await pair.ReallyBeIdle();
            await pair.CleanReturnAsync();

            HandheldLightComponent GetSuitLight()
            {
                if (em.TryGetComponent<HandheldLightComponent>(suit, out var light))
                    return light;

                var helmet = em.GetComponent<ToggleableClothingComponent>(suit).ClothingUid!.Value;
                return em.GetComponent<HandheldLightComponent>(helmet);
            }
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }

        static string WithOldCooldown(string yaml)
        {
            var document = new YamlStream();
            document.Load(new StringReader(yaml));
            var root = (YamlMappingNode) document.Documents[0].RootNode;
            foreach (YamlMappingNode group in (YamlSequenceNode) root.Children[new YamlScalarNode("entities")])
            foreach (YamlMappingNode entity in (YamlSequenceNode) group.Children[new YamlScalarNode("entities")])
            foreach (YamlMappingNode component in (YamlSequenceNode) entity.Children[new YamlScalarNode("components")])
            {
                var type = ((YamlScalarNode) component.Children[new YamlScalarNode("type")]).Value;
                if (type == "Action")
                    component.Children[new YamlScalarNode("cooldown")] = new YamlMappingNode("start", "28799", "end", "28800");
                if (type == "UseDelay" && component.Children.TryGetValue(new YamlScalarNode("delays"), out var delays)
                    && delays is YamlMappingNode configured)
                {
                    foreach (var entry in configured.Children.Values.OfType<YamlMappingNode>())
                    {
                        entry.Children[new YamlScalarNode("startTime")] = new YamlScalarNode("28799");
                        entry.Children[new YamlScalarNode("endTime")] = new YamlScalarNode("28800");
                    }
                }
            }
            using var writer = new StringWriter();
            document.Save(new YamlMappingFix(new Emitter(writer)), false);
            return writer.ToString();
        }
    }
}
