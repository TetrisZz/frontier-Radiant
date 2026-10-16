using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Messenger;
using Content.Server._radiant.Messenger;
using Content.Server.CartridgeLoader;
using Content.Shared._radiant.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Server.Player;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class MessengerTest
{
    [Test]
    public async Task DeliveryValidationUnreadAndCompactLayout()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var loader = entities.System<CartridgeLoaderSystem>();
                var first = entities.SpawnEntity("PassengerPDA", map.GridCoords);
                var second = entities.SpawnEntity("PassengerPDA", map.GridCoords);
                var third = entities.SpawnEntity("PassengerPDA", map.GridCoords);
                foreach (var pda in new[] { first, second, third })
                {
                    Assert.That(loader.HasProgram<MessengerCartridgeComponent>(pda), Is.False,
                        "Messenger is temporarily excluded from preinstalled PDA programs");
                    Assert.That(loader.InstallProgram(pda, "MessengerCartridge"), Is.True);
                }
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var session = players.Sessions.Single();
                var previousAttached = session.AttachedEntity;
                var character = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
                players.SetAttachedEntity(session, character);
                entities.GetComponent<PdaComponent>(first).OwnerName = "Sender";
                entities.GetComponent<PdaComponent>(second).OwnerName = "Receiver";
                entities.GetComponent<PdaComponent>(third).OwnerName = "Other";
                entities.GetComponent<PdaComponent>(first).PdaOwner = character;
                entities.GetComponent<PdaComponent>(second).PdaOwner = character;
                var messenger = entities.System<MessengerCartridgeSystem>();
                Assert.That(messenger.IsContactAvailable(second), Is.True);
                Assert.That(messenger.IsContactAvailable(third), Is.False, "An owner label is not a player character");
                Assert.That(loader.TryGetProgram<MessengerCartridgeComponent>(first, out var firstProgram, out var sender), Is.True);
                Assert.That(loader.TryGetProgram<MessengerCartridgeComponent>(second, out var secondProgram, out var receiver), Is.True);
                Assert.That(loader.TryGetProgram<MessengerCartridgeComponent>(third, out _, out var other), Is.True);
                var address = entities.GetNetEntity(second);
                void Send(string text, NetEntity? target = null)
                {
                    CartridgeMessageEvent message = new MessengerUiMessage(MessengerAction.Send, target ?? address, text)
                    {
                        LoaderUid = entities.GetNetEntity(first),
                        Actor = first,
                    };
                    entities.EventBus.RaiseLocalEvent(firstProgram!.Value, message);
                }

                sender!.Selected = address;
                receiver!.Selected = entities.GetNetEntity(first);
                entities.GetComponent<CartridgeLoaderComponent>(second).ActiveProgram = secondProgram;
                Send("   ");
                Send(new string('x', MessengerUiMessage.MaxLength + 1));
                Send("wrong recipient", entities.GetNetEntity(third));
                Assert.That(sender.Messages, Is.Empty);
                Send("Hello [color=red]friend[/color]");
                Assert.That(sender.Messages, Has.Count.EqualTo(1));
                Assert.That(receiver!.Messages, Has.Count.EqualTo(1));
                Assert.That(receiver.Messages[0].Text, Is.EqualTo("Hello [color=red]friend[/color]"));
                Assert.That(receiver.Messages[0].Incoming, Is.True);
                Assert.That(receiver.Messages[0].Peer, Is.EqualTo(entities.GetNetEntity(first)));
                Assert.That(receiver.Unread[entities.GetNetEntity(first)], Is.EqualTo(1));
                Assert.That(other!.Messages, Is.Empty, "No messages leak to a third PDA");
                Send("spam");
                Assert.That(receiver.Messages, Has.Count.EqualTo(1));
                Assert.That(sender.Error, Is.EqualTo("messenger-error-rate"));
                receiver.Muted = true;
                for (var i = 0; i < 205; i++)
                {
                    sender.NextSend = TimeSpan.Zero;
                    Send($"Message {i}");
                }
                Assert.That(sender.Messages, Has.Count.EqualTo(200));
                Assert.That(receiver.Messages, Has.Count.EqualTo(200));
                Assert.That(receiver.Unread[entities.GetNetEntity(first)], Is.EqualTo(200));
                sender.NextSend = TimeSpan.Zero;
                var ghost = entities.SpawnEntity("Observer", map.GridCoords);
                players.SetAttachedEntity(session, ghost);
                Assert.That(messenger.IsContactAvailable(second), Is.False, "Ghosting makes the original character unavailable");
                Send("ghosted recipient");
                Assert.That(receiver.Messages, Has.Count.EqualTo(200));
                Assert.That(sender.Error, Is.EqualTo("messenger-error-offline"));
                entities.GetComponent<PdaComponent>(third).PdaOwner = ghost;
                Assert.That(messenger.IsContactAvailable(third), Is.False, "Observers must not appear even with their own PDA");
                players.SetAttachedEntity(session, character);
                Assert.That(messenger.IsContactAvailable(second), Is.True, "Returning to the character restores availability");
                entities.DeleteEntity(second);
                Send("offline");
                Assert.That(sender.Messages, Has.Count.EqualTo(200));
                Assert.That(sender.Error, Is.EqualTo("messenger-error-offline"));
                players.SetAttachedEntity(session, previousAttached);
                TestContext.Out.WriteLine("Messenger delivery assertions completed");
            });
            await pair.Client.WaitAssertion(() =>
            {
                using var ui = new MessengerUiFragment();
                var peer = new NetEntity(42);
                var contacts = new List<MessengerContact>
                {
                    new(peer, "Very long contact name that must not widen the sidebar", true, 4, "Preview"),
                };
                var state = new MessengerUiState(contacts, new()
                {
                    new(peer, "Peer", new string('W', 500), TimeSpan.FromMinutes(123), true),
                    new(peer, "Peer", "[color=red]Literal text[/color]", TimeSpan.FromMinutes(124), false),
                }, peer, false, null);
                ui.UpdateState(state);
                foreach (var width in new[] { 532, 700 })
                {
                    var size = new Vector2(width, 300);
                    ui.Measure(size);
                    ui.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(ui.DesiredSize.X, Is.LessThanOrEqualTo(width), "Must fit the standard PDA");
                    Assert.That(Descendants(ui).OfType<LineEdit>().All(input => input.Size.X > 0), Is.True);
                }
                ui.UpdateState(new MessengerUiState(contacts, new(), peer, true, "messenger-error-offline"));
                Assert.That(Descendants(ui).OfType<CheckBox>().Single().Pressed, Is.False);
                TestContext.Out.WriteLine("Messenger layout assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Messenger regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
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
