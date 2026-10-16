using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Messenger;
using Content.Shared._radiant.Messenger;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class MessengerLayoutTest
{
    [Test]
    public async Task SidebarAndComposerFitCompactPdaWithoutResettingConversation()
    {
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/MessengerLayoutTestPrototypes"),
                MountOptions = new MountOptions
                {
                    DirMounts = new()
                    {
                        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"))
                    }
                }
            },
        };
        options.BeforeStart += () =>
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.DoAutoRegistrations();
            factory.GenerateNetIds();
        };
        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await client.WaitAssertion(() =>
        {
            using var ui = new MessengerUiFragment();
            var send = (TextureButton) Descendants(ui).Single(c => c.Name == "MessengerSend");
            Assert.That(send.Disabled, Is.True, "No sending before the first server state");
            var peer = new NetEntity(42);
            var contact = new MessengerContact(peer, new string('W', 100), true, 12, new string('W', 500));
            var messages = new List<MessengerMessage>
            {
                new(peer, contact.Name, new string('W', 500), TimeSpan.FromMinutes(123), true),
                new(peer, contact.Name, "[color=red]Literal message[/color]", TimeSpan.FromMinutes(124), false),
            };
            ui.UpdateState(new MessengerUiState([contact], messages, peer, false, null));
            foreach (var width in new[] { 440, 532, 700 })
            {
                var size = new Vector2(width, 300);
                ui.Measure(size);
                ui.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(ui.DesiredSize.X, Is.LessThanOrEqualTo(width));
                var sidebar = Descendants(ui).Single(c => c.Name == "MessengerSidebar");
                var conversation = Descendants(ui).Single(c => c.Name == "MessengerConversation");
                var composer = Descendants(ui).Single(c => c.Name == "MessengerComposer");
                Assert.That(sidebar.Size.X, Is.EqualTo(180).Within(1));
                Assert.That(conversation.GlobalPosition.X, Is.GreaterThanOrEqualTo(sidebar.GlobalPosition.X + sidebar.Size.X));
                Assert.That(composer.Size.X, Is.GreaterThan(100));
                Assert.That(composer.GlobalPosition.Y + composer.Size.Y, Is.LessThanOrEqualTo(size.Y + 1));
                Assert.That(send.Size.X, Is.EqualTo(28).Within(1));
                Assert.That(send.Size.Y, Is.EqualTo(28).Within(1));
                Assert.That(send.GlobalPosition.X + send.Size.X, Is.LessThanOrEqualTo(size.X + 1));
            }
            var history = Descendants(ui).Single(c => c.Name == "MessengerMessages");
            var firstMessage = history.Children.First();
            ui.UpdateState(new MessengerUiState([contact with { Available = false }], messages, peer, true, null));
            Assert.That(history.Children.First(), Is.SameAs(firstMessage), "Presence refresh preserves conversation controls");
            Assert.That(send.Disabled, Is.True);
            Assert.That(Descendants(ui).OfType<LineEdit>().Single(c => !c.Editable).Editable, Is.False);
            var contactsList = Descendants(ui).Single(c => c.Name == "MessengerContacts");
            Assert.That(contactsList.Children.OfType<ContainerButton>(), Is.Empty, "Offline characters are not discoverable contacts");
            var dialogsTab = (Button) Descendants(ui).Single(c => c.Name == "MessengerDialogsTab");
            dialogsTab.Pressed = true;
            Assert.That(contactsList.Children.OfType<ContainerButton>().Count(), Is.EqualTo(1), "Offline correspondence remains in chats");
            var input = (LineEdit) Descendants(ui).Single(c => c.Name == "MessengerInput");
            var other = new MessengerContact(new NetEntity(43), "Other character", true, 0, "");
            ui.UpdateState(new MessengerUiState([contact, other], messages, peer, false, null));
            input.Text = "Unsent draft";
            ui.UpdateState(new MessengerUiState([contact, other], [], other.Address, false, null));
            Assert.That(input.Text, Is.Empty);
            input.Text = "Another draft";
            ui.UpdateState(new MessengerUiState([contact, other], messages, peer, false, null));
            Assert.That(input.Text, Is.EqualTo("Unsent draft"));
            ui.UpdateState(new MessengerUiState([contact, other],
                [.. messages, new(peer, contact.Name, "Unsent draft", TimeSpan.FromMinutes(125), false)], peer, false, null));
            Assert.That(input.Text, Is.Empty, "Only confirmed delivery clears the draft");
        });
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
