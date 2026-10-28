using System.Linq;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.DeviceLinking;

public sealed class MedicalNetworkConfiguratorTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: MedicalLinkMultitoolTest
  parent: Multitool
  components:
  - type: NetworkConfigurator
    useDelay: 0
";

    [Test]
    public async Task SwitchingMedicalDevicesAfterDefaultsDoesNotKeepThePreviousPair()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var ui = em.System<SharedUserInterfaceSystem>();
                var links = em.System<SharedDeviceLinkSystem>();
                var user = em.SpawnEntity("MobHuman", map.GridCoords);
                var tool = em.SpawnEntity("MedicalLinkMultitoolTest", map.GridCoords);
                Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
                var config = em.GetComponent<NetworkConfiguratorComponent>(tool);
                void Use(EntityUid target) => em.EventBus.RaiseLocalEvent(tool,
                    new AfterInteractEvent(user, tool, target, map.GridCoords, true));
                foreach (var (consoleId, deviceId, port) in new[]
                         {
                             ("GeneticResearchConsole", "GeneticBodyGrower", "GeneticCapsuleSender"),
                             ("computerBodyScanner", "OperatingTable", "BodyScannerSender"),
                         })
                {
                    var firstConsole = em.SpawnEntity(consoleId, map.GridCoords);
                    var firstDevice = em.SpawnEntity(deviceId, map.GridCoords);
                    var nextConsole = em.SpawnEntity(consoleId, map.GridCoords);
                    var nextDevice = em.SpawnEntity(deviceId, map.GridCoords);
                    Use(firstConsole);
                    Use(firstDevice);
                    Assert.That(ui.TryGetUiState<DeviceLinkUserInterfaceState>(tool, NetworkConfiguratorUiKey.Link, out var initial), Is.True);
                    Assert.That(initial!.Sources.Select(p => p.Id), Is.EquivalentTo(new[] { port }));
                    Assert.That(initial.Defaults, Has.Count.EqualTo(1));
                    Assert.That(initial.LinkContext, Is.Not.Zero);
                    Assert.That(initial.SourceAddress, Is.Not.Empty, "Devices without network addresses still need a readable identity.");
                    Assert.That(initial.SinkAddress, Is.Not.Empty);
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorLinkDefaultsMessage(initial.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(firstConsole, firstDevice), Has.Count.EqualTo(1));
                    Assert.That(ui.TryGetUiState<DeviceLinkUserInterfaceState>(tool, NetworkConfiguratorUiKey.Link, out var linked), Is.True);
                    Assert.That(linked!.Links, Has.Count.EqualTo(1));

                    Use(nextConsole);
                    Assert.That(config.ActiveDeviceLink, Is.EqualTo(nextConsole),
                        "Selecting another console must replace the previous first device, not silently keep its completed pair.");
                    Assert.That(config.DeviceLinkTarget, Is.Null);
                    Use(nextDevice);
                    Assert.That(ui.TryGetUiState<DeviceLinkUserInterfaceState>(tool, NetworkConfiguratorUiKey.Link, out var next), Is.True);
                    Assert.That(next!.Sources, Has.Length.EqualTo(1));
                    Assert.That(next.Sinks, Has.Length.EqualTo(1));
                    Assert.That(next.Links, Is.Empty, "The new device must not display the old device's connections.");
                    Assert.That(next.Defaults, Has.Count.EqualTo(1));
                    Assert.That(next.LinkContext, Is.Not.EqualTo(initial.LinkContext));
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorLinkDefaultsMessage(initial.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(nextConsole, nextDevice), Is.Empty, "A queued click from the old window must not link the new pair.");
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorLinkDefaultsMessage(next.LinkContext) { Actor = user });
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorLinkDefaultsMessage(next.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(nextConsole, nextDevice), Has.Count.EqualTo(1));
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorClearLinksMessage(initial.LinkContext) { Actor = user });
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorToggleLinkMessage(next.Defaults![0].source, next.Defaults[0].sink, initial.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(nextConsole, nextDevice), Has.Count.EqualTo(1), "Old clear/toggle commands must not edit the current pair.");
                    ui.CloseUi(tool, NetworkConfiguratorUiKey.Link, user);
                    Assert.That(config.ActiveDeviceLink, Is.Null);
                    Assert.That(config.DeviceLinkTarget, Is.Null);
                    Use(nextDevice);
                    Use(nextConsole);
                    Assert.That(ui.TryGetUiState<DeviceLinkUserInterfaceState>(tool, NetworkConfiguratorUiKey.Link, out var reopened), Is.True);
                    Assert.That(reopened!.Links, Has.Count.EqualTo(1));
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorClearLinksMessage(reopened.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(nextConsole, nextDevice), Is.Empty);
                    Assert.That(links.GetLinks(firstConsole, firstDevice), Has.Count.EqualTo(1));
                    ui.RaiseUiMessage(tool, NetworkConfiguratorUiKey.Link,
                        new NetworkConfiguratorToggleLinkMessage(reopened.Defaults![0].source, reopened.Defaults[0].sink, reopened.LinkContext) { Actor = user });
                    Assert.That(links.GetLinks(nextConsole, nextDevice), Has.Count.EqualTo(1));
                    Use(nextDevice);
                    Assert.That(config.ActiveDeviceLink, Is.Null, "Selecting the first device again must also discard its displayed pair.");
                    Assert.That(config.DeviceLinkTarget, Is.Null);
                    ui.CloseUi(tool, NetworkConfiguratorUiKey.Link, user);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
