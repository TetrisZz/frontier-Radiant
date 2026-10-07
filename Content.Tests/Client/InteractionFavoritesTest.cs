using Content.Client.Interaction.Panel.Ui;
using NUnit.Framework;

namespace Content.Tests.Client;

[TestFixture]
public sealed class InteractionFavoritesTest
{
    [Test]
    public void SavedOrderSurvivesRestartAndUnknownIds()
    {
        var favorites = new InteractionFavorites();
        favorites.Add("First");
        favorites.Add("OtherServerAction");
        favorites.Add("Last");
        favorites.Move("Last", -1);
        var restarted = new InteractionFavorites();
        restarted.Load(favorites.Serialize());
        Assert.That(restarted.Ids, Is.EqualTo(new[] { "First", "Last", "OtherServerAction" }));
        restarted.Remove("Last");
        Assert.That(restarted.Ids, Is.EqualTo(new[] { "First", "OtherServerAction" }));
    }

    [Test]
    public void DuplicatesEmptyLinesAndInvalidMovesAreSafe()
    {
        var favorites = new InteractionFavorites();
        favorites.Load("\r\nFirst\r\nFirst\n\nSecond\n");
        favorites.Move("First", -1);
        favorites.Move("Second", 1);
        favorites.Move("Unknown", -1);
        favorites.Add("Second");
        Assert.That(favorites.Ids, Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(favorites.CanMove("Unknown", 1), Is.False);
        Assert.That(favorites.CanMove("First", 1), Is.True);
    }
}
