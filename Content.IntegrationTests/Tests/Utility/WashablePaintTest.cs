using System;
using System.Linq;
using Content.Client.Inventory;
using Content.Shared.DisplacementMap;
using Content.Shared.DoAfter;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Server._radiant.Paint;
using Content.Server.Power.Components;
using Content.Shared._radiant.Paint;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using ContainerSystem = Robust.Server.Containers.ContainerSystem;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class WashablePaintTest
{
    [Test]
    public async Task PaintSinkWasherPowerAndVisualRestoration()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var paint = entities.System<WashablePaintSystem>();
                var washer = entities.System<WashingMachineSystem>();
                var storageSystem = entities.System<SharedEntityStorageSystem>();
                var containers = entities.System<ContainerSystem>();
                var solutions = entities.System<SharedSolutionContainerSystem>();
                var item = entities.SpawnEntity("ClothingUniformJumpsuitColorRed", map.GridCoords);
                var sink = entities.SpawnEntity("Sink", map.GridCoords);
                var drySink = entities.SpawnEntity("SinkEmpty", map.GridCoords);
                Assert.That(paint.Paint(item, Color.Blue), Is.True);
                Assert.That(entities.GetComponent<WashablePaintComponent>(item).Color, Is.EqualTo(Color.Blue));
                Assert.That(paint.Paint(item, Color.White), Is.True, "Repainting replaces the previous coating");
                Assert.That(paint.TryWashAtSink(drySink, item), Is.False);
                Assert.That(entities.HasComponent<WashablePaintComponent>(item), Is.True);
                Assert.That(solutions.TryGetSolution(sink, "tank", out _, out var tank), Is.True);
                var before = tank.Volume;
                Assert.That(paint.TryWashAtSink(sink, item), Is.True);
                Assert.That(tank.Volume, Is.EqualTo(before - 5));
                Assert.That(entities.HasComponent<WashablePaintComponent>(item), Is.False);
                Assert.That(paint.TryWashAtSink(sink, item), Is.False, "No water wasted on an unpainted item");
                Assert.That(tank.Volume, Is.EqualTo(before - 5));

                var machine = entities.SpawnEntity("WashingMachine", map.GridCoords);
                var storage = entities.GetComponent<EntityStorageComponent>(machine);
                var cycle = entities.GetComponent<WashingMachineComponent>(machine);
                var power = entities.GetComponent<ApcPowerReceiverComponent>(machine);
                var human = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
                var verbs = entities.System<Content.Server.Verbs.VerbSystem>();
                power.Powered = true;
                var emptyVerb = verbs.GetLocalVerbs(machine, human, typeof(Verb))
                    .Single(verb => verb.Text == Loc.GetString("washer-start"));
                Assert.That(emptyVerb.Disabled, Is.True, "Empty-hand context menu must still show the disabled start command");
                Assert.That(emptyVerb.Message, Is.EqualTo(Loc.GetString("washer-empty")));
                var backpack = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
                var can = entities.SpawnEntity("WashablePaintBlack", map.GridCoords);
                var toggles = entities.System<ItemToggleSystem>();
                Assert.That(toggles.IsActivated(can), Is.False, "New cans start capped");
                foreach (var blocked in new[]
                {
                    "ClothingOuterHardsuitEngineering", "ClothingHeadHelmetHardsuitEngineering", "NFWeaponPistolViper", "CombatKnife",
                    "PassengerPDA", "Paper", "PaperCargoInvoice", "BookBase", "BookSpaceEncyclopedia",
                    "EEPassportHesperia", "EEPassportTemporary", "RadiantCredentialDvbSheriff",
                    "RadiantCredentialFleetNavyKomandor", "IDCardStandard",
                    "FoodBurgerPlain", "FoodPacketChipsTrash", "PresentTrash",
                    "PottedPlant0", "PottedPlant27", "MailAdminFun", "MailLargeAdminFun",
                    "EncryptionKeyCommon", "HandheldStationMap", "HandheldStationMapUnpowered",
                    "NapkinDrum", "NapkinDrum5", "Ashtray",
                    "GasVentPump", "GasDualPortVentPump", "GasPassiveVent", "GasVentScrubber",
                    "Gauze", "Ointment", "PillDexalin", "PillCanisterDexalin", "Hypospray", "RubberStampCaptain",
                })
                {
                    var protectedItem = entities.SpawnEntity(blocked, map.GridCoords);
                    Assert.That(paint.CanPaint(protectedItem), Is.False, blocked);
                    Assert.That(paint.Paint(protectedItem, Color.Red), Is.False, blocked);
                    Assert.That(entities.HasComponent<WashablePaintComponent>(protectedItem), Is.False, blocked);
                }
                Assert.That(paint.CanPaint(entities.SpawnEntity("Crowbar", map.GridCoords)), Is.True, "Ordinary tools remain paintable");
                foreach (var clothing in new[]
                {
                    "ClothingUniformJumpsuitColorRed", "ClothingShoesColorBlack",
                    "ClothingHandsGlovesColorBlack", "ClothingHeadHatBeret", "ClothingBackpack",
                    "ClothingUnderwearBottomPantiesWhite", "ClothingUnderwearBottomBoxersWhite",
                    "ClothingUnderwearSocksNormal", "ClothingUnderwearTopBraWhite",
                })
                {
                    var wearable = entities.SpawnEntity(clothing, map.GridCoords);
                    Assert.That(paint.CanPaint(wearable), Is.True, clothing);
                    Assert.That(paint.Paint(wearable, Color.Blue), Is.True, clothing);
                    Assert.That(entities.GetComponent<WashablePaintComponent>(wearable).Color, Is.EqualTo(Color.Blue), clothing);
                    paint.Wash(wearable);
                    Assert.That(entities.HasComponent<WashablePaintComponent>(wearable), Is.False, clothing);
                    entities.DeleteEntity(wearable);
                }
                var serverHands = entities.System<Content.Server.Hands.Systems.HandsSystem>();
                serverHands.AddHand(human, "paint-test-hand", HandLocation.Right);
                serverHands.SetActiveHand(human, "paint-test-hand");
                entities.EnsureComponent<DoAfterComponent>(human);
                Assert.That(serverHands.TryPickup(human, can, "paint-test-hand", checkActionBlocker: false, animate: false), Is.True);
                var cappedUse = new InteractUsingEvent(human, can, backpack, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(backpack, cappedUse);
                Assert.That(cappedUse.Handled, Is.True);
                Assert.That(entities.GetComponent<DoAfterComponent>(human).DoAfters, Is.Empty, "A capped can cannot start painting");
                Assert.That(toggles.TryActivate(can), Is.True);
                var usePaint = new InteractUsingEvent(human, can, backpack, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(backpack, usePaint);
                Assert.That(usePaint.Handled, Is.True);
                Assert.That(entities.GetComponent<DoAfterComponent>(human).DoAfters.Values
                    .Any(action => action.Args.Event is ApplyWashablePaintEvent && action.Args.Target == backpack), Is.True,
                    "Clicking a backpack must start painting, not merely consume the click");
                Assert.That(entities.GetComponent<TransformComponent>(can).ParentUid, Is.EqualTo(human), "Paint can must not be inserted into the backpack");
                Assert.That(paint.Paint(backpack, Color.Black), Is.True);
                paint.Wash(backpack);
                Assert.That(paint.Paint(human, Color.Red), Is.False);
                Assert.That(containers.Insert(human, storage.Contents), Is.False, "No living creatures in the washer");
                paint.Paint(item, Color.Green);
                Assert.That(containers.Insert(item, storage.Contents), Is.True);
                power.Powered = false;
                Assert.That(washer.TryStart(machine), Is.False);
                var unpoweredVerb = verbs.GetLocalVerbs(machine, human, typeof(Verb))
                    .Single(verb => verb.Text == Loc.GetString("washer-start"));
                Assert.That(unpoweredVerb.Disabled, Is.True);
                Assert.That(unpoweredVerb.Message, Is.EqualTo(Loc.GetString("washer-no-power")));
                power.Powered = true;
                Assert.That(entities.GetComponent<TransformComponent>(machine).Anchored, Is.True);
                var startVerb = verbs.GetLocalVerbs(machine, human, typeof(Verb))
                    .Single(verb => verb.Text == Loc.GetString("washer-start"));
                Assert.That(startVerb.Disabled, Is.False);
                startVerb.Act!();
                Assert.That(cycle.Remaining, Is.GreaterThan(0), "Context-menu command must actually start the cycle");
                var stopVerb = verbs.GetLocalVerbs(machine, human, typeof(Verb))
                    .Single(verb => verb.Text == Loc.GetString("washer-stop"));
                Assert.That(stopVerb.Disabled, Is.False);
                Assert.That(storage.Open, Is.False);
                Assert.That(storageSystem.CanOpen(human, machine), Is.False, "Door locked during the cycle");
                washer.Update(5);
                var remaining = cycle.Remaining;
                power.Powered = false;
                washer.Update(100);
                Assert.That(cycle.Remaining, Is.EqualTo(remaining));
                Assert.That(entities.HasComponent<WashablePaintComponent>(item), Is.True);
                power.Powered = true;
                washer.Update(remaining);
                Assert.That(entities.HasComponent<WashablePaintComponent>(item), Is.False);
                Assert.That(storage.Contents.Contains(item), Is.True, "Washing must not consume or replace the item");
                Assert.That(storageSystem.CanOpen(human, machine), Is.True);
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var loot = (AllSelector) prototypes.Index<EntityTablePrototype>("MaintenanceLockerLoot").Table;
                Assert.That(loot.Children.OfType<EntSelector>().Any(entry => entry.Id == "WashablePaintRandom"), Is.True);
                var randomCan = entities.SpawnEntity("WashablePaintRandom", map.GridCoords);
                var randomPaint = entities.GetComponent<RandomPaintColorComponent>(randomCan);
                var randomColor = entities.GetComponent<PaintCanComponent>(randomCan).Color;
                Assert.That(randomPaint.Initialized, Is.True);
                Assert.That(randomPaint.Colors, Does.Contain(randomColor));
                entities.EventBus.RaiseLocalEvent(randomCan, new MapInitEvent());
                Assert.That(entities.GetComponent<PaintCanComponent>(randomCan).Color, Is.EqualTo(randomColor), "Saved cans must not reroll their colour");
                Assert.That(entities.GetComponent<MetaDataComponent>(randomCan).EntityName, Is.EqualTo(Loc.GetString("ent-WashablePaintRandom")));
                foreach (var (id, node) in new[] { ("RadiantPlywoodChair", "chair"), ("RadiantWashingMachine", "washer") })
                {
                    var graph = prototypes.Index<ConstructionGraphPrototype>(id);
                    Assert.That(graph.Edge("start", node)!.Steps, Is.Not.Empty, $"{id}: visible building materials");
                }
                entities.SpawnEntity("ChairPlywood", map.GridCoords);
                entities.SpawnEntity("VendingMachinePaint", map.GridCoords);
                entities.SpawnEntity("VendingMachineRestockPaint", map.GridCoords);
                foreach (var color in new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "White", "Black" })
                    entities.SpawnEntity("WashablePaint" + color, map.GridCoords);
                TestContext.Out.WriteLine("Paint and laundry server assertions completed");
            });
            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.ResolveDependency<IEntityManager>();
                var clientMap = entities.System<SharedMapSystem>().CreateMap();
                var coordinates = new EntityCoordinates(clientMap, 0, 0);
                foreach (var colour in new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "White", "Black", "Random" })
                {
                    var can = entities.SpawnEntity("WashablePaint" + colour, coordinates);
                    var canSprite = entities.GetComponent<SpriteComponent>(can);
                    Assert.That(((SpriteComponent.Layer) canSprite[0]).State.Name, Is.EqualTo("capped"));
                    Assert.That(((SpriteComponent.Layer) canSprite[1]).State.Name, Is.EqualTo("cap-color"));
                    if (colour == "Random")
                        Assert.That(canSprite[1].Color, Is.EqualTo(Color.FromHex("#808080")), "Mystery can must not reveal its actual colour");
                    var toggles = entities.System<ItemToggleSystem>();
                    Assert.That(toggles.TryActivate(can), Is.True);
                    entities.System<AppearanceSystem>().OnChangeData(can, canSprite);
                    Assert.That(((SpriteComponent.Layer) canSprite[0]).State.Name, Is.EqualTo("can"), "Opening must remove the cap visually");
                    Assert.That(((SpriteComponent.Layer) canSprite[1]).State.Name, Is.EqualTo("color"));
                    Assert.That(toggles.TryDeactivate(can), Is.True);
                    entities.System<AppearanceSystem>().OnChangeData(can, canSprite);
                    Assert.That(((SpriteComponent.Layer) canSprite[0]).State.Name, Is.EqualTo("capped"));
                    Assert.That(((SpriteComponent.Layer) canSprite[1]).State.Name, Is.EqualTo("cap-color"));
                    entities.DeleteEntity(can);
                }
                var uid = entities.SpawnEntity("ClothingUniformJumpsuitColorRed", coordinates);
                var sprite = entities.GetComponent<SpriteComponent>(uid);
                var originalColor = sprite.Color;
                var layers = sprite.AllLayers.Select(layer => (layer.Color, ((SpriteComponent.Layer) layer).Shader)).ToArray();
                var paint = entities.AddComponent<WashablePaintComponent>(uid);
                paint.Color = Color.Blue;
                var changed = new AfterAutoHandleStateEvent(null!);
                entities.EventBus.RaiseLocalEvent(uid, ref changed);
                Assert.That(sprite.AllLayers.Cast<SpriteComponent.Layer>().All(layer => layer.ShaderPrototype == "WashablePaint"), Is.True);
                entities.RemoveComponent<WashablePaintComponent>(uid);
                Assert.That(sprite.Color, Is.EqualTo(originalColor));
                var restored = sprite.AllLayers.Select(layer => (layer.Color, ((SpriteComponent.Layer) layer).Shader)).ToArray();
                Assert.That(restored, Is.EqualTo(layers), "Original palette and shaders must return after washing");
                var holder = entities.SpawnEntity("MobHumanDummy", coordinates);
                var inventory = entities.System<ClientInventorySystem>();
                var hands = entities.System<Content.Client.Hands.Systems.HandsSystem>();
                var holderSprite = entities.GetComponent<SpriteComponent>(holder);
                var holderInventory = entities.GetComponent<InventoryComponent>(holder);
                // Force displacement on the test dummy without changing its species.
#pragma warning disable RA0002
                holderInventory.Displacements["jumpsuit"] = new DisplacementData
                {
                    SizeMaps = new()
                    {
                        [32] = new() { RsiPath = "Mobs/Species/Vox/displacement.rsi", State = "jumpsuit" },
                    },
                };
#pragma warning restore RA0002
                Assert.That(inventory.TryEquip(holder, uid, "jumpsuit", silent: true, force: true), Is.True);
                var keys = entities.GetComponent<InventorySlotsComponent>(holder).VisualLayerKeys["jumpsuit"];
                Assert.That(keys, Is.Not.Empty);
                var worn = keys.ToDictionary(key => key, key =>
                {
                    var layer = (SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)];
                    return (layer.Color, layer.ShaderPrototype);
                });
                paint = entities.AddComponent<WashablePaintComponent>(uid);
                paint.Color = Color.Blue;
                entities.EventBus.RaiseLocalEvent(uid, ref changed);
                Assert.That(keys.Any(key => ((SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)]).ShaderPrototype == "WashablePaintDisplaced"), Is.True,
                    "The visible jumpsuit must be painted without disabling body displacement");
                Assert.That(keys.Where(key => key.EndsWith("-displacement", StringComparison.Ordinal))
                    .All(key => ((SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)]).ShaderPrototype != "WashablePaintDisplaced"), Is.True);
                entities.RemoveComponent<WashablePaintComponent>(uid);
                foreach (var (key, original) in worn)
                {
                    var layer = (SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)];
                    Assert.That((layer.Color, layer.ShaderPrototype), Is.EqualTo(original), "Worn palette restored");
                }
                foreach (var (prototype, slot) in new[] { ("ClothingShoesColorBlack", "shoes"), ("ClothingBackpack", "back") })
                {
#pragma warning disable RA0002
                    holderInventory.Displacements[slot] = new DisplacementData
                    {
                        ShaderOverride = slot == "shoes" ? "DisplacedStencilDraw" : "DisplacedDraw",
                        SizeMaps = new()
                        {
                            [32] = new() { RsiPath = "Mobs/Species/Vox/displacement.rsi", State = slot },
                        },
                    };
#pragma warning restore RA0002
                    var clothing = entities.SpawnEntity(prototype, coordinates);
                    entities.AddComponent<WashablePaintComponent>(clothing);
                    Assert.That(inventory.TryEquip(holder, clothing, slot, silent: true, force: true), Is.True);
                    var slotKeys = entities.GetComponent<InventorySlotsComponent>(holder).VisualLayerKeys[slot];
                    var paintShader = slot == "shoes" ? "WashablePaintDisplacedStencil" : "WashablePaintDisplaced";
                    Assert.That(slotKeys.Any(key => ((SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)]).ShaderPrototype == paintShader), Is.True,
                        $"Already painted {slot} must retain its colour when equipped");
                    entities.RemoveComponent<WashablePaintComponent>(clothing);
                    var originalShader = slot == "shoes" ? "DisplacedStencilDraw" : "DisplacedDraw";
                    Assert.That(slotKeys.Any(key => ((SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)]).ShaderPrototype == originalShader), Is.True,
                        $"Washing {slot} must restore its original displacement shader");
                    Assert.That(inventory.TryUnequip(holder, slot, silent: true, force: true), Is.True);
                    entities.DeleteEntity(clothing);
                }
                Assert.That(inventory.TryUnequip(holder, "jumpsuit", silent: true, force: true), Is.True);
                hands.AddHand(holder, "test-hand", HandLocation.Right);
                Assert.That(hands.TryPickup(holder, uid, checkActionBlocker: false, animate: false), Is.True);
                var heldKeys = entities.GetComponent<HandsComponent>(holder).RevealedLayers.Values.SelectMany(value => value).ToArray();
                Assert.That(heldKeys, Is.Not.Empty);
                var held = heldKeys.ToDictionary(key => key, key =>
                {
                    var layer = (SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)];
                    return (layer.Color, layer.ShaderPrototype);
                });
                entities.AddComponent<WashablePaintComponent>(uid);
                Assert.That(heldKeys.Any(key => ((SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)]).ShaderPrototype == "WashablePaint"), Is.True);
                entities.RemoveComponent<WashablePaintComponent>(uid);
                foreach (var (key, original) in held)
                {
                    var layer = (SpriteComponent.Layer) holderSprite[holderSprite.LayerMapGet(key)];
                    Assert.That((layer.Color, layer.ShaderPrototype), Is.EqualTo(original), "In-hand palette restored");
                }
                entities.DeleteEntity(uid);
                entities.DeleteEntity(holder);
                entities.DeleteEntity(clientMap);
                TestContext.Out.WriteLine("Paint visual restoration assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Paint and laundry regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
