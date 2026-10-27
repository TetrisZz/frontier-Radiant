using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.NetworkConfigurator;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.VendingMachines;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.DeviceLinking;

public sealed class NetworkConfiguratorRegressionTest
{
    [Test]
    public async Task DefaultsClearAndManualLinksRemainConsistent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var maps = em.System<SharedMapSystem>();
                maps.CreateMap(out var mapId);
                var grid = maps.CreateGridEntity(mapId);
                var coordinates = new EntityCoordinates(grid.Owner, 0, 0);
                var source = em.SpawnEntity(null, coordinates);
                var sink = em.SpawnEntity(null, coordinates);
                var incompatibleSink = em.SpawnEntity(null, coordinates);
                var links = em.System<DeviceLinkSystem>();
                links.EnsureSourcePorts(source, "On", "Status");
                links.EnsureSinkPorts(sink, "On", "Off");
                links.EnsureSinkPorts(incompatibleSink, "Off");
                var sourceComp = em.GetComponent<DeviceLinkSourceComponent>(source);
                var sinkComp = em.GetComponent<DeviceLinkSinkComponent>(sink);
                var ports = links.GetSourcePorts(source);
                var defaults = links.GetDefaults(ports, sinkComp.Ports);
                Assert.That(defaults, Is.EquivalentTo(new[] { ("On", "On") }),
                    "A port without defaults must not erase the other port's compatible defaults.");
                Assert.That(links.GetDefaults(ports, em.GetComponent<DeviceLinkSinkComponent>(incompatibleSink).Ports), Is.Empty);

                links.LinkDefaults(null, source, sink);
                Assert.That(links.GetLinks(source, sink).Count, Is.EqualTo(1));
                Assert.That(sinkComp.LinkedSources, Does.Contain(source));
                var state = new DeviceLinkUserInterfaceState(links.GetSourcePortIds((source, sourceComp)),
                    links.GetSinkPortIds((sink, sinkComp)), links.GetLinks(source, sink), "source", "sink", defaults);
                Assert.That(links.ToggleLink(null, source, sink, "On", "Off"), Is.True);
                Assert.That(state.Links.Count, Is.EqualTo(1), "Previously sent UI states must be immutable snapshots.");
                Assert.That(links.GetLinks(source, sink).Count, Is.EqualTo(2));
                Assert.That(links.ToggleLink(null, source, sink, "On", "On"), Is.True);
                Assert.That(sourceComp.Outputs["On"], Does.Contain(sink),
                    "Disconnecting one sink port must not disable the source's other link to the same device.");

                links.RemoveSinkFromSource(source, sink);
                Assert.That(links.GetLinks(source, sink), Is.Empty);
                Assert.That(sinkComp.LinkedSources, Does.Not.Contain(source));
                links.LinkDefaults(null, source, sink);
                Assert.That(links.GetLinks(source, sink).Count, Is.EqualTo(1));

                Assert.That(links.SaveLinks(null, source, incompatibleSink, [("On", "On")]), Is.False);
                Assert.That(links.GetLinks(source, incompatibleSink), Is.Empty);
                Assert.That(em.GetComponent<DeviceLinkSinkComponent>(incompatibleSink).LinkedSources,
                    Does.Not.Contain(source), "Rejected links must not leave a phantom connected source.");
                maps.DeleteMap(mapId);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LinkMenuResetsSelectionAndSelectedStylesAreAvailable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var prototypes = pair.Server.ProtoMan;
                var inventory = prototypes.Index<VendingMachineInventoryPrototype>("XomkaVendInventory");
                foreach (var (entity, group, slot) in new[]
                         {
                             ("ClothingOuterCoatSkeletonTuxedo", "ContractorOuterClothing", "outerClothing"),
                             ("ClothingUniformCrimsonDress", "ContractorJumpsuit", "jumpsuit"),
                             ("ClothingUniformCorsetDressWhite", "ContractorJumpsuit", "jumpsuit"),
                             ("ClothingUniformCorsetDressBlue", "ContractorJumpsuit", "jumpsuit"),
                             ("ClothingNeckFurScarf", "ContractorNeck", "neck"),
                         })
                {
                    Assert.That(inventory.StartingInventory.ContainsKey(entity), Is.True, entity);
                    var loadout = prototypes.Index<LoadoutPrototype>("Contractor" + entity);
                    Assert.That(loadout.Equipment[slot].Id, Is.EqualTo(entity));
                    Assert.That(prototypes.Index<LoadoutGroupPrototype>(group).Loadouts,
                        Does.Contain(new ProtoId<LoadoutPrototype>(loadout.ID)));
                    var proto = prototypes.Index<EntityPrototype>(entity);
                    Assert.That(proto.TryGetComponent<ClothingComponent>(out _, pair.Server.EntMan.ComponentFactory), Is.True);
                }
            });
            await pair.Client.WaitAssertion(() =>
            {
                using var menu = new NetworkConfiguratorLinkMenu();
                var initial = new DeviceLinkUserInterfaceState(["On", "Status"], ["On", "Off"], [], "A", "B", [("On", "On")], linkContext: 17);
                menu.UpdateState(initial);
                var left = Descendants(menu).OfType<BoxContainer>().Single(control => control.Name == "ButtonContainerLeft");
                var right = Descendants(menu).OfType<BoxContainer>().Single(control => control.Name == "ButtonContainerRight");
                var defaultsButton = Descendants(menu).OfType<Button>().Single(control => control.Name == "ButtonLinkDefault");
                var toggles = new List<(string, string)>();
                var defaultsContexts = new List<uint>();
                menu.OnLinkDefaults += () => defaultsContexts.Add(menu.LinkContext);
                Click(defaultsButton);
                Assert.That(defaultsContexts, Is.EqualTo(new uint[] { 17 }));
                menu.OnToggleLink += (source, sink) =>
                {
                    toggles.Add((source, sink));
                    // State updates can rebuild the buttons while an event is being handled.
                    menu.UpdateState(initial);
                };
                Assert.That(defaultsButton.Disabled, Is.False);
                Click((Button) left.GetChild(1));
                menu.UpdateState(new DeviceLinkUserInterfaceState(["On"], ["On"], [], "C", "D", [], linkContext: 18));
                Assert.That(defaultsButton.Disabled, Is.True);
                Assert.That(menu.LinkContext, Is.EqualTo(18));
                Click((Button) right.GetChild(0));
                Assert.That(toggles, Is.Empty, "A selection belonging to the previous device must be discarded.");
                Click((Button) left.GetChild(0));
                Assert.That(toggles, Is.EquivalentTo(new[] { ("On", "On") }));
                Assert.That(left.Children.Cast<Button>().Any(button => button.Pressed), Is.False);
                Assert.That(right.Children.Cast<Button>().Any(button => button.Pressed), Is.False);

                var cache = pair.Client.ResolveDependency<IResourceCache>();
                foreach (var (path, state) in new[]
                         {
                             ("Clothing/OuterClothing/Coats/skeleton_tuxedo.rsi", "equipped-OUTERCLOTHING"),
                             ("Clothing/Uniforms/Jumpskirt/crimson_dress.rsi", "equipped-INNERCLOTHING"),
                             ("Clothing/Uniforms/Jumpskirt/corset_dress_white.rsi", "equipped-INNERCLOTHING"),
                             ("Clothing/Uniforms/Jumpskirt/corset_dress_blue.rsi", "equipped-INNERCLOTHING"),
                             ("Clothing/Neck/Scarfs/fur_scarf.rsi", "equipped-NECK"),
                         })
                {
                    var rsi = cache.GetResource<RSIResource>(new ResPath("/Textures/_radiant/" + path)).RSI;
                    Assert.That(rsi.TryGetState("icon", out _), Is.True, path);
                    Assert.That(rsi.TryGetState(state, out _), Is.True, path);
                }
                foreach (var hair in new[] { "HumanHairBeachwaveRS", "HumanHairGentleBunRS" })
                {
                    var marking = pair.Client.ProtoMan.Index<MarkingPrototype>(hair);
                    Assert.That(marking.BodyPart, Is.EqualTo(HumanoidVisualLayers.Hair));
                    Assert.That(marking.MarkingCategory, Is.EqualTo(MarkingCategories.Hair));
                    var sprite = (SpriteSpecifier.Rsi) marking.Sprites.Single();
                    var rsi = cache.GetResource<RSIResource>(new ResPath("/Textures") / sprite.RsiPath).RSI;
                    Assert.That(rsi.TryGetState(sprite.RsiState, out _), Is.True, hair);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static void Click(BaseButton button)
    {
        button.MuteSounds = true;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
        foreach (var (name, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default, button.Size / 2, button.PixelSize / 2);
            typeof(BaseButton).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [args]);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
