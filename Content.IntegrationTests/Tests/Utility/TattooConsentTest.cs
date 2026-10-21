using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Content.Client._radiant.Social;
using Content.Server._radiant.Dossiers;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._radiant.Social;
using Content.Shared.Atmos;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class TattooConsentTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RecipientCanAnswerWhileMachineIsHeldByArtist(bool self)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid machine = default;
        EntityUid patient = default;
        EntityUid artist = default;
        NetEntity machineNet = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
            air.AdjustMoles(Gas.Oxygen, 21);
            air.AdjustMoles(Gas.Nitrogen, 79);
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            artist = em.SpawnEntity("MobHuman", map.GridCoords);
            patient = self ? artist : em.SpawnEntity("MobHuman", map.GridCoords);
            var players = pair.Server.ResolveDependency<IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), patient);
            em.EnsureComponent<DossierHolderComponent>(patient).Loaded = true;
            machine = em.SpawnEntity("TattooMachine", map.GridCoords);
            machineNet = em.GetNetEntity(machine);
            var hands = em.System<ServerHands>();
            hands.AddHand(artist, "tattoo-test", HandLocation.Right);
            hands.SetActiveHand(artist, "tattoo-test");
            Assert.That(hands.TryPickup(artist, machine, "tattoo-test", checkActionBlocker: false, animate: false), Is.True);
            var use = new AfterInteractEvent(artist, machine, patient, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(machine, use);
        });
        // Let the idle state reach the recipient before the artist submits a request.
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            em.EventBus.RaiseLocalEvent(machine,
                new TattooProposeMessage("Remember", 2, 1) { Actor = artist, UiKey = TattooUiKey.Editor });
            var ui = em.System<SharedUserInterfaceSystem>();
            Assert.That(ui.IsUiOpen(machine, TattooUiKey.Consent, patient), Is.True);
            var stranger = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            Assert.That(ui.TryOpenUi(machine, TattooUiKey.Consent, stranger), Is.False);
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Pending?.Text, Is.EqualTo("Remember"));
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(machine, TattooUiKey.Consent, patient), Is.True,
                "The recipient must not lose consent UI because the machine is in the artist's hand.");
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Pending?.Text, Is.EqualTo("Remember"));
        });
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.ResolveDependency<IEntityManager>();
            var uid = em.GetEntity(machineNet);
            Assert.That(em.GetComponent<UserInterfaceComponent>(uid).ClientOpenInterfaces[TattooUiKey.Consent],
                Is.TypeOf<TattooConsentBoundUserInterface>());
            Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<TattooUiState>(uid, TattooUiKey.Consent, out var state), Is.True);
            Assert.That(state!.Tattoo?.Text, Is.EqualTo("Remember"));
            Assert.That(state.Accepted, Is.False);
            var window = pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<TattooConsentWindow>().Single();
            var accept = Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("tattoo-accept"));
            Assert.That(accept.Disabled, Is.False, "A fresh request must be actionable through the recipient's actual window.");
            Click(accept);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Accepted, Is.True);
        });
        await pair.Client.WaitPost(() =>
        {
            var window = pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<TattooConsentWindow>().Single();
            Click(Descendants(window).OfType<Button>().Single(button => button.Text == Loc.GetString("tattoo-reject")));
        });
        await pair.RunTicksSync(300);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var tattoo = em.GetComponent<TattooMachineComponent>(machine);
            Assert.That(tattoo.Pending, Is.Null);
            Assert.That(tattoo.Ink, Is.EqualTo(20));
            Assert.That(em.GetComponent<DossierHolderComponent>(patient).Record.Tattoos, Is.Empty);
            em.EventBus.RaiseLocalEvent(machine,
                new TattooProposeMessage("Another request", 2, 1) { Actor = artist, UiKey = TattooUiKey.Editor });
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(machine, TattooUiKey.Consent, patient), Is.True);
            Assert.That(em.System<ServerHands>().TryDrop(artist, machine), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Pending, Is.Null);
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(machine, TattooUiKey.Consent, patient), Is.False);
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Ink, Is.EqualTo(20));
        });
        TestContext.Out.WriteLine("Consent round-trip, withdrawal, third-party rejection and cancellation after dropping the machine all passed.");
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
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
