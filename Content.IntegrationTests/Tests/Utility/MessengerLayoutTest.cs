using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Globalization;
using Content.Client.Stylesheets;
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
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Localization;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class MessengerLayoutTest
{
    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task SidebarAndComposerFitCompactPdaWithoutResettingConversation(string locale)
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
            var loc = IoCManager.Resolve<ILocalizationManager>();
            var culture = CultureInfo.GetCultureInfo(locale);
            if (!loc.HasCulture(culture))
                loc.LoadCulture(culture);
            loc.DefaultCulture = culture;
            using var ui = new MessengerUiFragment
            {
                Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet,
            };
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
                Assert.That(sidebar.Size.X, Is.EqualTo(216).Within(1));
                Assert.That(conversation.GlobalPosition.X, Is.GreaterThanOrEqualTo(sidebar.GlobalPosition.X + sidebar.Size.X));
                Assert.That(composer.Size.X, Is.GreaterThan(100));
                Assert.That(composer.GlobalPosition.Y + composer.Size.Y, Is.LessThanOrEqualTo(size.Y + 1));
                Assert.That(send.Size.X, Is.EqualTo(28).Within(1));
                Assert.That(send.Size.Y, Is.EqualTo(28).Within(1));
                Assert.That(send.GlobalPosition.X + send.Size.X, Is.LessThanOrEqualTo(size.X + 1));
                var dialogs = (Button) Descendants(ui).Single(c => c.Name == "MessengerDialogsTab");
                var contacts = (Button) Descendants(ui).Single(c => c.Name == "MessengerContactsTab");
                Assert.That(dialogs.GlobalPosition.X + dialogs.Size.X + 3, Is.LessThanOrEqualTo(contacts.GlobalPosition.X));
                foreach (var tab in new[] { dialogs, contacts })
                {
                    Assert.That(tab.Size.X, Is.EqualTo(98).Within(1));
                    Assert.That(tab.Size.Y, Is.EqualTo(32).Within(1));
                    Assert.That(tab.ClipText, Is.False);
                    var available = tab.Label.Size.X;
                    tab.Label.Measure(new Vector2(float.PositiveInfinity, 32));
                    Assert.That(tab.Label.DesiredSize.X, Is.LessThanOrEqualTo(available), tab.Text);
                    Assert.That(tab.ActualModulateSelf, Is.EqualTo(Color.White), "PDA styles must not multiply the background color");
                }
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
            Click(dialogsTab);
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
            var button = (MessengerButton) Descendants(ui).Single(c => c.Name == "MessengerDialogsTab");
            var normal = ((StyleBoxFlat) button.StyleBoxOverride!).BackgroundColor;
            SendKey(button, "KeyBindDown", BoundKeyState.Down);
            var pressed = ((StyleBoxFlat) button.StyleBoxOverride!).BackgroundColor;
            Assert.That(pressed.R + pressed.G + pressed.B, Is.LessThan(normal.R + normal.G + normal.B),
                "Pressed buttons must become darker, not brighter");
            SendKey(button, "KeyBindUp", BoundKeyState.Up);
            Assert.That(((StyleBoxFlat) button.StyleBoxOverride!).BackgroundColor, Is.EqualTo(normal),
                "Releasing the selected tab restores its selected color");
            SendKey(button, "KeyBindDown", BoundKeyState.Down);
            typeof(BaseButton).GetMethod("ControlFocusExited", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Assert.That(((StyleBoxFlat) button.StyleBoxOverride!).BackgroundColor, Is.EqualTo(normal),
                "Losing focus must not leave a button permanently dark");
        });
    }

    private static void Click(BaseButton button)
    {
        foreach (var (name, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
            SendKey(button, name, state);
    }

    private static void SendKey(BaseButton button, string name, BoundKeyState state)
    {
        button.MuteSounds = true;
        var position = button.GlobalPixelPosition + button.PixelSize / 2;
        var pointer = new ScreenCoordinates(position, button.Window?.Id ?? default);
        var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default,
            button.Size / 2, button.PixelSize / 2);
        typeof(BaseButton).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [args]);
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
