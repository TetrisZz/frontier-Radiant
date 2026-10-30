using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.NetworkConfigurator;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.DeviceLinking;

public sealed class NetworkConfiguratorClientTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ClientLinkMultitoolTest
  parent: Multitool
  components:
  - type: NetworkConfigurator
    useDelay: 0
";

    [Test]
    public async Task RealWindowUsesCurrentPairAndClicksReachTheServerWithoutReopening()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            EntityUid user = default;
            EntityUid tool = default;
            NetEntity toolNet = default;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                players.SetAttachedEntity(players.Sessions.Single(), user);
                tool = em.SpawnEntity("ClientLinkMultitoolTest", map.GridCoords);
                toolNet = em.GetNetEntity(tool);
                Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            });
            await pair.RunTicksSync(10);

            foreach (var (sourceId, sinkId) in new[]
                { ("SignalButton", "Airlock"), ("GeneticResearchConsole", "GeneticBodyGrower"), ("computerBodyScanner", "OperatingTable") })
            {
                EntityUid source = default, sink = default, nextSource = default, nextSink = default;
                DeviceLinkUserInterfaceState previous = default!;
                uint context = 0;
                await pair.Server.WaitAssertion(() =>
                {
                    var em = pair.Server.EntMan;
                    source = em.SpawnEntity(sourceId, map.GridCoords);
                    sink = em.SpawnEntity(sinkId, map.GridCoords);
                    nextSource = em.SpawnEntity(sourceId, map.GridCoords);
                    nextSink = em.SpawnEntity(sinkId, map.GridCoords);
                    Use(source);
                    Use(sink);
                    context = em.GetComponent<NetworkConfiguratorComponent>(tool).LinkContext;
                });
                await pair.RunTicksSync(10);
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(0);
                    Click(Find<Button>(Window(), "ButtonLinkDefault"));
                });
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() => Assert.That(Links(source, sink), Is.EqualTo(1)));
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(1);
                    var ui = pair.Client.EntMan.System<SharedUserInterfaceSystem>();
                    Assert.That(ui.TryGetUiState<DeviceLinkUserInterfaceState>(pair.Client.EntMan.GetEntity(toolNet),
                        NetworkConfiguratorUiKey.Link, out var state), Is.True);
                    previous = state!;
                });

                // Replicate the closed window while retaining its previous cached state.
                await pair.Server.WaitAssertion(() => Use(nextSource));
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() =>
                {
                    Use(nextSink);
                    context = pair.Server.EntMan.GetComponent<NetworkConfiguratorComponent>(tool).LinkContext;
                });
                await pair.RunTicksSync(10);
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(0);
                    // Reproduce an old opening update arriving after the component already has the new pair.
                    var owner = pair.Client.EntMan.GetEntity(toolNet);
                    var bui = pair.Client.EntMan.GetComponent<UserInterfaceComponent>(owner)
                        .ClientOpenInterfaces[NetworkConfiguratorUiKey.Link];
                    typeof(NetworkConfiguratorBoundUserInterface).GetMethod("UpdateState", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(bui, [previous]);
                    CheckWindow(0);
                    Click(Find<Button>(Window(), "ButtonLinkDefault"));
                });
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() =>
                {
                    Assert.That(Links(source, sink), Is.EqualTo(1));
                    Assert.That(Links(nextSource, nextSink), Is.EqualTo(1));
                });
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(1);
                    Click(Find<Button>(Window(), "ButtonClear"));
                });
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() => Assert.That(Links(nextSource, nextSink), Is.Zero));
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(0);
                    Click((Button)Find<BoxContainer>(Window(), "ButtonContainerLeft").GetChild(0));
                    Click((Button)Find<BoxContainer>(Window(), "ButtonContainerRight").GetChild(0));
                });
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() => Assert.That(Links(nextSource, nextSink), Is.EqualTo(1)));
                await pair.Client.WaitAssertion(() => Click(Find<Button>(Window(), "ButtonOk")));
                await pair.RunTicksSync(10);
                await pair.Server.WaitAssertion(() =>
                {
                    Use(nextSink);
                    Use(nextSource);
                    context = pair.Server.EntMan.GetComponent<NetworkConfiguratorComponent>(tool).LinkContext;
                });
                await pair.RunTicksSync(10);
                await pair.Client.WaitAssertion(() =>
                {
                    CheckWindow(1);
                    Click(Find<Button>(Window(), "ButtonOk"));
                });
                await pair.RunTicksSync(10);

                void CheckWindow(int connections)
                {
                    var menu = Window();
                    Assert.That(menu.LinkContext, Is.EqualTo(context), sourceId + ": displayed pair must match the server.");
                    Assert.That(Find<BoxContainer>(menu, "ButtonContainerLeft").ChildCount, Is.GreaterThan(0));
                    Assert.That(Find<BoxContainer>(menu, "ButtonContainerRight").ChildCount, Is.GreaterThan(0));
                    Assert.That(Find<Button>(menu, "ButtonLinkDefault").Disabled, Is.False);
                    var renderer = typeof(NetworkConfiguratorLinkMenu).GetField("_links", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
                    var rendered = (ICollection)renderer.GetType().GetField("Links")!.GetValue(renderer)!;
                    Assert.That(rendered.Count, Is.EqualTo(connections), sourceId + ": displayed links must match the current pair.");
                }
            }

            void Use(EntityUid target) => pair.Server.EntMan.EventBus.RaiseLocalEvent(tool,
                new AfterInteractEvent(user, tool, target, map.GridCoords, true));
            int Links(EntityUid source, EntityUid sink) => pair.Server.EntMan.System<SharedDeviceLinkSystem>().GetLinks(source, sink).Count;
            NetworkConfiguratorLinkMenu Window() => pair.Client.ResolveDependency<IUserInterfaceManager>()
                .WindowRoot.Children.OfType<NetworkConfiguratorLinkMenu>().Single();
        }
        finally { await pair.CleanReturnAsync(); }
    }

    private static T Find<T>(Control parent, string name) where T : Control
        => Descendants(parent).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
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
}
