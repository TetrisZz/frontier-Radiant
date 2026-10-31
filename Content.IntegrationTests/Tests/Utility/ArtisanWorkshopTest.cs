using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Artisan;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Server._radiant.Artisan;
using Content.Server.Power.Components;
using Content.Shared._radiant.Artisan;
using Content.Shared._radiant.Paint;
using Content.Shared.Audio;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Labels.Components;
using Content.Shared.Guidebook;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ResourceManagement;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ArtisanWorkshopTest
{
    [Test]
    public async Task PotteryGlassAndMetalCompleteTheirRealSequencesWithHeatToolsAndFunctionalProducts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            NetEntity bottleNet = default;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var system = em.System<ArtisanSystem>();
                var containers = em.System<SharedContainerSystem>();
                var hands = em.System<SharedHandsSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var actor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                em.System<MetaDataSystem>().SetEntityName(actor, "Workshop Artisan");
                var wheel = Station("RadiantPotteryWheel");
                var kiln = Station("RadiantArtisanKiln");
                var furnace = Station("RadiantArtisanFurnace");
                var glassBench = Station("RadiantGlassworkingBench");
                var anvil = Station("RadiantArtisanAnvil");
                var bath = Station("RadiantQuenchingBath");
                var tongs = em.SpawnEntity("RadiantArtisanTongs", map.GridCoords);
                var blowpipe = em.SpawnEntity("RadiantArtisanBlowpipe", map.GridCoords);
                var paddle = em.SpawnEntity("RadiantArtisanPaddle", map.GridCoords);
                var hammer = em.SpawnEntity("RadiantArtisanHammer", map.GridCoords);
                var unsuitable = em.SpawnEntity("Paper", map.GridCoords);
                var failedInsert = new AfterInteractUsingEvent(actor, unsuitable, wheel, map.GridCoords, true);
                em.EventBus.RaiseLocalEvent(wheel, failedInsert);
                Assert.That(failedInsert.Handled, Is.True, "Unsuccessful loading must produce a handled explanation.");
                Assert.That(Items(wheel), Does.Not.Contain(unsuitable));
                var emptyTongs = new AfterInteractUsingEvent(actor, tongs, wheel, map.GridCoords, true);
                em.EventBus.RaiseLocalEvent(wheel, emptyTongs);
                Assert.That(emptyTongs.Handled, Is.True, "Using tongs on an empty station must explain the missing workpiece.");
                foreach (var recipe in pair.Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<ArtisanRecipePrototype>())
                {
                    Assert.That(recipe.Steps, Is.Not.Empty, recipe.ID);
                    var product = em.SpawnEntity(recipe.Result, map.GridCoords);
                    em.DeleteEntity(product);
                }
                Insert(wheel, em.SpawnEntity("SpaceQuartz1", map.GridCoords));
                Tank(wheel, out var wheelTank);
                solutions.TryAddReagent(wheelTank, "Water", 10);
                Begin(wheel, "ArtisanClay"); Finish(wheel);
                Assert.That(Tank(wheel, out _).Volume, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(Items(wheel).Count(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "RadiantClay"), Is.EqualTo(1));
                Begin(wheel, "ArtisanCeramicCup");
                Send(wheel, new(ArtisanAction.Recipe, "ArtisanCeramicCup"));
                Finish(wheel);
                var clay = Workpiece(wheel);
                Assert.That(em.GetComponent<ArtisanWorkpieceComponent>(clay).Stage, Is.EqualTo(1));
                em.AddComponent<WashablePaintComponent>(clay).Color = Color.CornflowerBlue;
                Insert(kiln, clay); Process(kiln, clay); Finish(kiln);
                var ceramic = Items(kiln).Single();
                Assert.That(em.EntityExists(clay), Is.False);
                Assert.That(em.GetComponent<ArtisanProductComponent>(ceramic).Glaze, Is.EqualTo(Color.CornflowerBlue));
                Assert.That(em.GetComponent<ArtisanProductComponent>(ceramic).Maker, Is.EqualTo("Workshop Artisan"));
                Assert.That(solutions.TryGetFitsInDispenser(ceramic, out var cup, out var liquid), Is.True);
                Assert.That(liquid.MaxVolume, Is.EqualTo(FixedPoint2.New(60)));
                Assert.That(solutions.TryAddReagent(cup!.Value, "Water", 30), Is.True);

                Insert(furnace, em.SpawnEntity("SheetGlass", map.GridCoords));
                var furnaceData = em.GetComponent<ArtisanStationComponent>(furnace);
                em.GetComponent<ApcPowerReceiverComponent>(furnace).Powered = false;
                Send(furnace, new(ArtisanAction.Recipe, "ArtisanGlassCup"));
                Assert.That(furnaceData.Busy, Is.False, "A furnace cannot process without power.");
                em.GetComponent<ApcPowerReceiverComponent>(furnace).Powered = true;
                furnaceData.Temperature = 20;
                Send(furnace, new(ArtisanAction.Recipe, "ArtisanGlassCup"));
                Assert.That(furnaceData.Busy, Is.False, "The charge needs working temperature.");
                system.Update(20);
                Begin(furnace, "ArtisanGlassCup"); Finish(furnace);
                var glass = Workpiece(furnace);
                Assert.That(hands.TryPickupAnyHand(actor, glass), Is.False, "Hot workpieces require tongs, including when directly picked up.");
                Send(furnace, new(ArtisanAction.Eject, item: em.GetNetEntity(glass)));
                Assert.That(Items(furnace), Does.Contain(glass));
                Carry(furnace, glassBench);
                Send(glassBench, new(ArtisanAction.Process, item: em.GetNetEntity(glass)));
                Assert.That(em.GetComponent<ArtisanStationComponent>(glassBench).Busy, Is.False, "Tongs cannot substitute for a blowpipe.");
                Hold(blowpipe); Process(glassBench, glass); Finish(glassBench);
                Process(glassBench, glass); Finish(glassBench);
                Assert.That(em.GetComponent<ArtisanWorkpieceComponent>(glass).Stage, Is.EqualTo(3));
                system.Update(130);
                Hold(paddle); Send(glassBench, new(ArtisanAction.Process, item: em.GetNetEntity(glass)));
                Assert.That(em.GetComponent<ArtisanStationComponent>(glassBench).Busy, Is.False);
                Insert(furnace, glass);
                Send(furnace, new(ArtisanAction.Reheat, item: em.GetNetEntity(glass))); Finish(furnace);
                Assert.That(em.GetComponent<ArtisanWorkpieceComponent>(glass).Stage, Is.EqualTo(3), "Reheating preserves completed operations.");
                Carry(furnace, glassBench);
                Hold(paddle); Process(glassBench, glass); Finish(glassBench);
                Carry(glassBench, kiln); Process(kiln, glass); Finish(kiln);
                var finishedGlass = Items(kiln).Single(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "RadiantGlassCup");
                Assert.That(solutions.TryGetFitsInDispenser(finishedGlass, out _, out var glassLiquid), Is.True);
                Assert.That(glassLiquid.MaxVolume, Is.EqualTo(FixedPoint2.New(60)));

                Insert(furnace, em.SpawnEntity("IngotSilver1", map.GridCoords));
                Begin(furnace, "ArtisanForgedRing"); Finish(furnace);
                var metal = Workpiece(furnace); Carry(furnace, anvil); Hold(hammer);
                for (var i = 0; i < 3; i++) { Process(anvil, metal); Finish(anvil); }
                Tank(bath, out var water); solutions.TryAddReagent(water, "Water", 10);
                Carry(anvil, bath); Process(bath, metal); Finish(bath);
                Assert.That(Tank(bath, out _).Volume, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(Items(bath).Single(item => em.HasComponent<ArtisanProductComponent>(item)), Is.Not.EqualTo(metal));
                Assert.That(Items(bath).Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID), Does.Contain("RadiantForgedRing"));

                foreach (var (result, material, count) in new[]
                {
                    ("PlasteelMace", "SheetPlasteel1", 4), ("PlasteelAxe", "SheetPlasteel1", 4),
                    ("CombatKnife", "SheetSteel1", 2), ("PlasteelSpear", "SheetPlasteel1", 3), ("PlasteelEpee", "SheetPlasteel1", 3)
                })
                {
                    var forge = Station("RadiantArtisanFurnace");
                    var forgeAnvil = Station("RadiantArtisanAnvil");
                    var quench = Station("RadiantQuenchingBath");
                    for (var index = 0; index < count; index++) Insert(forge, em.SpawnEntity(material, map.GridCoords));
                    Begin(forge, "ArtisanForge" + result); Finish(forge);
                    var blank = Workpiece(forge); Carry(forge, forgeAnvil); Hold(hammer);
                    for (var index = 0; index < 3; index++) { Process(forgeAnvil, blank); Finish(forgeAnvil); }
                    Tank(quench, out var quenchWater); solutions.TryAddReagent(quenchWater, "Water", 10);
                    Carry(forgeAnvil, quench); Process(quench, blank); Finish(quench);
                    Assert.That(Items(quench).Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID),
                        Does.Contain(result), result);
                }

                var bottle = em.SpawnEntity("RadiantArtisanBottle", map.GridCoords);
                Assert.That(solutions.TryGetSolution(bottle, "drink", out var bottleSolution, out _), Is.True);
                solutions.TryAddReagent(bottleSolution!.Value, "Water", 100);
                bottleNet = em.GetNetEntity(bottle);

                EntityUid Station(string id)
                {
                    var uid = em.SpawnEntity(id, map.GridCoords);
                    var station = em.GetComponent<ArtisanStationComponent>(uid);
                    station.Temperature = 1100;
                    if (em.TryGetComponent<ApcPowerReceiverComponent>(uid, out var power))
                    {
                        Assert.That(em.HasComponent<ExtensionCableReceiverComponent>(uid), Is.True, "Powered stations must connect to the actual APC network.");
                        power.Powered = true;
                    }
                    return uid;
                }
                IEnumerable<EntityUid> Items(EntityUid machine) => containers.GetContainer(machine, ArtisanStationComponent.Container).ContainedEntities;
                void Insert(EntityUid machine, EntityUid item) => Assert.That(containers.Insert(item, containers.GetContainer(machine, ArtisanStationComponent.Container)), Is.True);
                EntityUid Workpiece(EntityUid machine) => Items(machine).Single(item => em.HasComponent<ArtisanWorkpieceComponent>(item));
                Solution Tank(EntityUid machine, out Entity<SolutionComponent> entity)
                {
                    Assert.That(solutions.TryGetSolution(machine, ArtisanStationComponent.Solution, out var tank, out var contents), Is.True);
                    entity = tank!.Value; return contents!;
                }
                void Send(EntityUid machine, ArtisanActionMessage message)
                { message.Actor = actor; message.UiKey = ArtisanUiKey.Key; em.EventBus.RaiseLocalEvent(machine, message); }
                void Begin(EntityUid machine, string recipe)
                { Send(machine, new(ArtisanAction.Recipe, recipe)); Assert.That(em.GetComponent<ArtisanStationComponent>(machine).Busy, Is.True, recipe); }
                void Process(EntityUid machine, EntityUid item)
                { Send(machine, new(ArtisanAction.Process, item: em.GetNetEntity(item))); Assert.That(em.GetComponent<ArtisanStationComponent>(machine).Busy, Is.True); }
                void Finish(EntityUid machine)
                {
                    system.Update(em.GetComponent<ArtisanStationComponent>(machine).Remaining + 0.01f);
                    Assert.That(em.GetComponent<ArtisanStationComponent>(machine).Busy, Is.False);
                }
                void Hold(EntityUid tool)
                {
                    foreach (var held in hands.EnumerateHeld(actor).ToArray()) hands.TryDrop(actor, held);
                    Assert.That(hands.TryPickupAnyHand(actor, tool, animate: false), Is.True);
                }
                void Carry(EntityUid from, EntityUid to)
                {
                    Hold(tongs);
                    var take = new InteractUsingEvent(actor, tongs, from, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(from, take);
                    Assert.That(containers.GetContainer(tongs, ArtisanToolComponent.Container).ContainedEntities, Has.Count.EqualTo(1));
                    var put = new InteractUsingEvent(actor, tongs, to, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(to, put);
                    Assert.That(containers.GetContainer(tongs, ArtisanToolComponent.Container).ContainedEntities, Is.Empty);
                }
            });
            await pair.RunTicksSync(15);
            await pair.Client.WaitAssertion(() =>
            {
                // Check the actual client item event: world sprites alone do not validate inherited inhand states.
                var clientEm = pair.Client.EntMan;
                var cache = pair.Client.ResolveDependency<IResourceCache>();
                var bottle = clientEm.GetEntity(bottleNet);
                var bottleSprite = clientEm.GetComponent<SpriteComponent>(bottle);
                Assert.That(clientEm.System<SpriteSystem>().LayerMapTryGet((bottle, bottleSprite),
                    SolutionContainerLayers.Fill, out var fillLayer, false), Is.True);
                Assert.That(bottleSprite[fillLayer].Visible, Is.True, "A filled workshop bottle must display its liquid.");
                var bottleHeld = new GetInhandVisualsEvent(bottle, HandLocation.Left);
                clientEm.EventBus.RaiseLocalEvent(bottle, bottleHeld);
                Assert.That(bottleHeld.Layers.Any(layer => layer.Item2.State?.Contains("-fill-") == true), Is.True,
                    "Liquid must also be visible while holding the bottle.");
                foreach (var id in new[]
                {
                    "RadiantCeramicCup", "RadiantCeramicBowl", "RadiantCeramicPlate", "RadiantCeramicTray",
                    "RadiantGlassCup", "RadiantGlassBowl", "RadiantGlassPlate",
                    "RadiantArtisanUnfiredCup", "RadiantArtisanUnfiredBowl", "RadiantArtisanUnfiredPlate", "RadiantArtisanUnfiredTray",
                })
                {
                    var item = clientEm.SpawnEntity(id, Robust.Shared.Map.MapCoordinates.Nullspace);
                    foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
                    {
                        var held = new GetInhandVisualsEvent(item, hand);
                        clientEm.EventBus.RaiseLocalEvent(item, held);
                        Assert.That(held.Layers, Has.Count.EqualTo(1), $"{id}: {hand}");
                        var data = held.Layers.Single().Item2;
                        Assert.That(data.RsiPath, Is.Not.Null, "The layer must explicitly select its RSI instead of inheriting a previous item's sprite.");
                        var rsi = cache.GetResource<RSIResource>(new ResPath("/Textures") / data.RsiPath!).RSI;
                        Assert.That(rsi.TryGetState(data.State!, out _), Is.True, $"{id}: {hand}, {data.State}");
                    }
                    clientEm.DeleteEntity(item);
                }
                var guide = pair.Client.ResolveDependency<IPrototypeManager>().Index<GuideEntryPrototype>("RadiantArtisanWorkshop");
                using var reader = pair.Client.ResolveDependency<IResourceManager>().ContentFileReadText(guide.Text);
                Assert.That(pair.Client.ResolveDependency<DocumentParsingManager>().TryAddMarkup(new Document(), reader.ReadToEnd()), Is.True);
                using var window = new ArtisanWindow { Stylesheet = new StyleNano(cache).Stylesheet };
                var state = new ArtisanUiState("Workshop", ArtisanStationKind.Bottler, "Ready", 0.25f, 900, "Wine: 90",
                    new() { new("MissingClay", "Clay vessel", false, "Load clay first") },
                    new() { new(new NetEntity(1), "A very long handmade vessel name", "Next operation", true, false, true) }, "North", 30, false);
                window.SetState(state);
                state.Recipes[0].Result = "RadiantCeramicCup";
                state.Recipes[0].Requirements.Add(new("Clay: 0 / 1", "RadiantClay", false));
                window.SetState(state);
                Assert.That(Descendants(window).OfType<EntityPrototypeView>().Count(), Is.GreaterThanOrEqualTo(2),
                    "The recipe must preview both its result and its material.");
                state.Recipes[0].Requirements[0].Text = "Clay: 1 / 1";
                state.Recipes[0].Requirements[0].Satisfied = true;
                window.SetState(state);
                Assert.That(Descendants(window).OfType<RichTextLabel>().Any(label => label.GetMessage() == "Clay: 1 / 1"), Is.True,
                    "Ingredient counts must update even if the recipe remains blocked for a different reason.");
                var dose = Descendants(window).OfType<SpinBox>().Single();
                foreach (var size in new[] { new Vector2(480, 600), new Vector2(580, 680), new Vector2(760, 740) })
                {
                    window.SetSize = size;
                    window.Measure(size); window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(dose.Width, Is.GreaterThanOrEqualTo(160), "The dose field must not collapse between its label and other buttons.");
                    Assert.That(dose.LineEditControl.Width, Is.GreaterThanOrEqualTo(60));
                    Assert.That(dose.GlobalPosition.X + dose.Width, Is.LessThanOrEqualTo(size.X));
                }
                Assert.That(Descendants(window).OfType<ProgressBar>().Single().Value, Is.EqualTo(0.25));
                Assert.That(Descendants(window).OfType<LineEdit>().Single(edit => edit.Name == "BatchName").Text, Is.EqualTo("North"));
                Assert.That(Descendants(window).OfType<RichTextLabel>().Any(label => label.GetMessage() == "Load clay first"), Is.True,
                    "Disabled recipes must show the missing requirement directly, without needing a hover tooltip.");
                var batch = Descendants(window).OfType<LineEdit>().Single(edit => edit.Name == "BatchName");
                batch.SetText("Local label", invokeEvent: true);
                dose.Value = 37;
                window.SetState(state);
                Assert.That(batch.Text, Is.EqualTo("Local label"), "Progress updates must preserve unsaved edits even after the field loses focus.");
                Assert.That(dose.Value, Is.EqualTo(37));
                var sent = new List<ArtisanActionMessage>();
                window.Action += sent.Add;
                batch.ForceSubmitText();
                Assert.That(sent.Single().BatchName, Is.EqualTo("Local label"));
                Assert.That(sent.Single().Amount, Is.EqualTo(37));
                window.SetState(state);
                Assert.That(batch.Text, Is.EqualTo("Local label"), "An old server update must not overwrite settings awaiting confirmation.");
                state.BatchName = "Local label"; state.BottleAmount = 37;
                window.SetState(state);
                Assert.That(batch.Text, Is.EqualTo("Local label"));
            });
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var solutions = em.System<SharedSolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(em.GetEntity(bottleNet), "drink", out var solution, out _), Is.True);
                solutions.RemoveAllSolution(solution!.Value);
            });
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var bottle = em.GetEntity(bottleNet);
                var sprite = em.GetComponent<SpriteComponent>(bottle);
                Assert.That(em.System<SpriteSystem>().LayerMapTryGet((bottle, sprite), SolutionContainerLayers.Fill,
                    out var fillLayer, false), Is.True);
                Assert.That(sprite[fillLayer].Visible, Is.False, "Draining the bottle must hide the liquid layer again.");
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task FermentationAndAutomaticBottlingPreserveInputsOnCancelAndNeverRecycleFilledBottles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var system = em.System<ArtisanSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var containers = em.System<SharedContainerSystem>();
                var actor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var barrel = em.SpawnEntity("RadiantFermentationBarrel", map.GridCoords);
                var data = em.GetComponent<ArtisanStationComponent>(barrel);
                Assert.That(solutions.TryGetSolution(barrel, ArtisanStationComponent.Solution, out _, out var drink), Is.True);
                var grapes = Enumerable.Range(0, 3).Select(_ => em.SpawnEntity("FoodGrape", map.GridCoords)).ToArray();
                foreach (var grape in grapes) Insert(barrel, grape);
                Send(barrel, new(ArtisanAction.Recipe, "ArtisanBrewWine"));
                Assert.That(data.Busy, Is.True);
                Assert.That(data.LiquidLocked, Is.True);
                Assert.That(em.GetComponent<AmbientSoundComponent>(barrel).Enabled, Is.True);
                var drawingCup = em.SpawnEntity("LargeBeaker", map.GridCoords);
                em.GetComponent<SolutionTransferComponent>(drawingCup).TransferAmount = 10;
                Assert.That(solutions.TryGetRefillableSolution(drawingCup, out _, out var drawn), Is.True);
                ClickBarrel(drawingCup);
                Assert.That(drawn.Volume, Is.EqualTo(FixedPoint2.Zero), "The tap must stay locked during fermentation.");
                Assert.That(containers.Remove(grapes[0], containers.GetContainer(barrel, ArtisanStationComponent.Container)), Is.False);
                Assert.That(solutions.TryGetSolution(barrel, ArtisanStationComponent.Solution, out _, out _), Is.False, "Brewing locks external liquid transfers.");
                Send(barrel, new(ArtisanAction.Stop));
                Assert.That(data.LiquidLocked, Is.False);
                Assert.That(em.GetComponent<AmbientSoundComponent>(barrel).Enabled, Is.False);
                Assert.That(grapes.All(em.EntityExists), Is.True);
                Assert.That(drink.Volume, Is.EqualTo(FixedPoint2.Zero));
                Send(barrel, new(ArtisanAction.Recipe, "ArtisanBrewWine"));
                system.Update(181);
                Assert.That(data.Busy, Is.False);
                Assert.That(grapes.Any(em.EntityExists), Is.False);
                Assert.That(drink.GetTotalPrototypeQuantity("Wine"), Is.EqualTo(FixedPoint2.New(60)));
                Assert.That(data.DrainMode, Is.True, "A finished batch automatically switches the barrel to drawing.");
                Assert.That(em.GetComponent<AmbientSoundComponent>(barrel).Enabled, Is.False);
                ClickBarrel(drawingCup); ClickBarrel(drawingCup);
                Assert.That(drawn.GetTotalPrototypeQuantity("Wine"), Is.EqualTo(FixedPoint2.New(20)), "Repeated clicks must continue filling a partially filled container.");
                Assert.That(drink.Volume, Is.EqualTo(FixedPoint2.New(40)));
                Send(barrel, new(ArtisanAction.ToggleTap));
                Assert.That(data.DrainMode, Is.False);
                ClickBarrel(drawingCup);
                Assert.That(drawn.Volume, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(drink.Volume, Is.EqualTo(FixedPoint2.New(50)), "Pour mode must still accept liquids using the ordinary chemistry interaction.");

                var beerBarrel = em.SpawnEntity("RadiantFermentationBarrel", map.GridCoords);
                Assert.That(solutions.TryGetSolution(beerBarrel, ArtisanStationComponent.Solution, out var beerTank, out var beer), Is.True);
                solutions.TryAddReagent(beerTank!.Value, "Water", 60);
                for (var i = 0; i < 3; i++) Insert(beerBarrel, em.SpawnEntity("WheatBushel", map.GridCoords));
                Send(beerBarrel, new(ArtisanAction.Recipe, "ArtisanBrewBeer")); system.Update(241);
                Assert.That(beer.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(beer.GetTotalPrototypeQuantity("Beer"), Is.EqualTo(FixedPoint2.New(120)));

                var bottler = em.SpawnEntity("RadiantAutomaticBottler", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(bottler).Powered = true;
                var source = em.SpawnEntity("LargeBeaker", map.GridCoords);
                Assert.That(solutions.TryGetFitsInDispenser(source, out var input, out var sourceLiquid), Is.True);
                solutions.TryAddReagent(input!.Value, "Wine", 75); Insert(bottler, source);
                var bottles = Enumerable.Range(0, 3).Select(_ => em.SpawnEntity("RadiantArtisanBottle", map.GridCoords)).ToArray();
                foreach (var bottle in bottles) Insert(bottler, bottle);
                Send(bottler, new(ArtisanAction.Settings, batchName: "Old batch", amount: 30));
                Send(bottler, new(ArtisanAction.Source, item: em.GetNetEntity(source)));
                Send(bottler, new(ArtisanAction.Bottle, batchName: "North batch", amount: 25, applySettings: true));
                Assert.That(em.GetComponent<ArtisanStationComponent>(bottler).Busy, Is.True);
                for (var i = 0; i < 3; i++) system.Update(3.1f);
                Assert.That(sourceLiquid.Volume, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(em.GetComponent<ArtisanStationComponent>(bottler).Busy, Is.False);
                system.Update(60);
                foreach (var bottle in bottles)
                {
                    Assert.That(solutions.TryGetFitsInDispenser(bottle, out _, out var contents), Is.True);
                    Assert.That(contents.GetTotalPrototypeQuantity("Wine"), Is.EqualTo(FixedPoint2.New(25)));
                    Assert.That(em.GetComponent<LabelComponent>(bottle).CurrentLabel, Is.EqualTo("North batch"));
                }
                void Insert(EntityUid machine, EntityUid item) => Assert.That(containers.Insert(item, containers.GetContainer(machine, ArtisanStationComponent.Container)), Is.True);
                void ClickBarrel(EntityUid vessel)
                {
                    var click = new InteractUsingEvent(actor, vessel, barrel, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(barrel, click);
                    if (click.Handled) return;
                    var after = new AfterInteractEvent(actor, vessel, barrel, map.GridCoords, true);
                    em.EventBus.RaiseLocalEvent(vessel, after);
                    Assert.That(after.Handled, Is.True);
                }
                void Send(EntityUid machine, ArtisanActionMessage message)
                { message.Actor = actor; message.UiKey = ArtisanUiKey.Key; em.EventBus.RaiseLocalEvent(machine, message); }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
