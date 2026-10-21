using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using NUnit.Framework;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Tests.Client;

[TestFixture]
public sealed class RadioCoordinatesTagTest
{
    private static Type[] ChatTags => (Type[]) typeof(ChatBox)
        .GetField("ChatMarkupTags", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Test]
    public void ChatPermitsRadioMarkersWithoutOpeningConsoleCommandLinks()
    {
        var manager = new MarkupTagManager();
        var handlers = (Dictionary<string, IMarkupTagHandler>) typeof(MarkupTagManager)
            .GetField("_markupTagTypes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var marker = new RadioCoordinatesTag();
        handlers.Add(marker.Name, marker);

        Assert.That(manager.TryGetMarkupTagHandler(marker.Name, ChatTags, out var handler), Is.True);
        Assert.That(handler, Is.SameAs(marker));
        Assert.That(manager.TryGetMarkupTagHandler("cmdlink", ChatTags, out _), Is.False);
    }

    [Test]
    public void ChatKeepsEngineDefaultFormattingAndAddsOnlyTheRadioMarker()
    {
        var entryType = typeof(OutputPanel).Assembly.GetType("Robust.Client.UserInterface.RichTextEntry", true)!;
        var defaults = (Type[]) entryType.GetField("DefaultTags", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        Assert.That(ChatTags, Is.EquivalentTo(defaults.Append(typeof(RadioCoordinatesTag))));
        Assert.That(defaults, Does.Not.Contain(typeof(RadioCoordinatesTag)),
            "The engine default must not silently replace the chat-specific whitelist");
    }

    [TestCase("Coordinates: X: 42, Y: -18")]
    [TestCase("Unknown")]
    public void LocationAndInterferenceTooltipsSurviveChatFormatting(string tooltip)
    {
        var message = new FormattedMessage();
        message.PushColor(Robust.Shared.Maths.Color.Green);
        message.AddMarkupOrThrow($"\\[Common [radiolocation=\"?\" tooltip=\"{FormattedMessage.EscapeText(tooltip)}\"/]\\] Test");
        message.Pop();
        var marker = message.Single(node => node.Name == "radiolocation" && !node.Closing);
        Assert.That(marker.Value.TryGetString(out var text), Is.True);
        Assert.That(text, Is.EqualTo("?"));
        Assert.That(marker.Attributes["tooltip"].TryGetString(out var restored), Is.True);
        Assert.That(restored, Is.EqualTo(tooltip));
    }

    [Test]
    public void MalformedMarkerDoesNotCreateAControl()
    {
        Assert.That(new RadioCoordinatesTag().TryCreateControl(
            new MarkupNode("radiolocation", new MarkupParameter("?"), null), out var control), Is.False);
        Assert.That(control, Is.Null);
    }
}
