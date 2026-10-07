using Content.Shared._Goobstation.Languages;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class SpeciesLanguagePreviewTest
{
    [TestCase("Human", "Общесолнечный")]
    [TestCase("Reptilian", "Синта'Унати")]
    [TestCase("Vox", "Вокс-пиджин")]
    [TestCase("Diona", "Корневой язык")]
    [TestCase("SlimePerson", "Бабблилиш")]
    [TestCase("Moth", "Моффик")]
    [TestCase("Arachnid", "Щёлкающий")]
    [TestCase("Vulpkanin", "Канилунц")]
    [TestCase("Tajaran", "Сиик'тайр")]
    [TestCase("Resomi", "Счечи")]
    [TestCase("Feroxi", "Нехина")]
    [TestCase("Shadowkin", "Сумеречный")]
    [TestCase("Dwarf", "Кхаздар")]
    [TestCase("Oni", "Кансэй")]
    [TestCase("Harpy", "Аэрийский")]
    [TestCase("Goblin", "Крикли")]
    [TestCase("Sheleg", "Шелар")]
    [TestCase("DemonSpecies", "Арканийский")]
    [TestCase("Felinid", "НекоМетрический")]
    [TestCase("UnknownSpecies", null)]
    public void LobbyLookupMatchesSpeechLanguages(string species, string? expected)
    {
        Assert.That(SpeciesLanguageUtility.GetNativeLanguage(species), Is.EqualTo(expected));
    }
}
