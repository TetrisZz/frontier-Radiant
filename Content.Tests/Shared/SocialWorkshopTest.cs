using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared._radiant.Dossiers;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared._radiant.Social;
using NUnit.Framework;
using Robust.Shared.Maths;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class SocialWorkshopTest
{
    [TestCase("2026-10-21T12:30:00+00:00", "21.10.3026")]
    [TestCase("2026-12-31T21:01:00+00:00", "01.01.3027")]
    [TestCase("2028-02-29T10:00:00+00:00", "29.02.3028")]
    public void PrintDateUsesMoscowDayAndYearPlusOneThousand(string utc, string expected)
        => Assert.That(PaperworkClock.Date(DateTimeOffset.Parse(utc)), Is.EqualTo(expected));

    [TestCase(0, "00:00:00")]
    [TestCase(-10, "00:00:00")]
    [TestCase(3723, "01:02:03")]
    [TestCase(90001, "25:00:01")]
    public void ShiftClockDoesNotWrapAtMidnight(int seconds, string expected)
        => Assert.That(PaperworkClock.Shift(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));

    [TestCase("FishBass", "FishBass", true, false, false, false, true)]
    [TestCase("FishBass", "FishBass", false, true, false, false, true)]
    [TestCase("FishBass", "FishTropical", true, false, false, false, false)]
    [TestCase("FishBass", "FishBass", true, true, false, false, false)]
    [TestCase("FishBass", "FishBass", true, false, true, false, false)]
    [TestCase("FishBass", "FishBass", true, false, false, true, false)]
    public void FishPairRequiresSameSpeciesOppositeSexAndAdults(string a, string b,
        bool af, bool bf, bool aj, bool bj, bool expected)
        => Assert.That(FishBreedingRules.Compatible(a, b, af, bf, aj, bj), Is.EqualTo(expected));

    [Test]
    public void InheritedColorIsStableAndOrderIndependent()
    {
        for (var a = 0; a < 3; a++)
        for (var b = 0; b < 3; b++)
            Assert.That(FishBreedingRules.Tint(a, b), Is.EqualTo(FishBreedingRules.Tint(b, a)));
        Assert.That(FishBreedingRules.Tint(1, 1), Is.Not.EqualTo(Color.White));
        Assert.That(FishBreedingRules.Tint(2, 2), Is.Not.EqualTo(FishBreedingRules.Tint(1, 1)));
        Assert.That(FishBreedingRules.Tint(1, 2), Is.EqualTo(Color.White));
    }

    [TestCase(0, 0, "A", true)]
    [TestCase(5, 5, "A", true)]
    [TestCase(-1, 0, "A", false)]
    [TestCase(6, 0, "A", false)]
    [TestCase(0, -1, "A", false)]
    [TestCase(0, 6, "A", false)]
    [TestCase(0, 0, "   ", false)]
    public void TattooInputRejectsInvalidBodyZonesColorsAndBlankText(int zone, int color, string text, bool expected)
        => Assert.That(TextTattooRules.Valid(new TextTattoo { Zone = zone, Color = color, Text = text }), Is.EqualTo(expected));

    [Test]
    public void TattooTextIsBoundedAndSurvivesCharacterDossierJson()
    {
        var original = new TextTattoo { Text = new string('A', TextTattooRules.MaximumLength), Zone = 4, Color = 2 };
        Assert.That(TextTattooRules.Valid(original), Is.True);
        Assert.That(TextTattooRules.Valid(new TextTattoo { Text = original.Text + "A" }), Is.False);
        var dossier = new DossierRecord { Tattoos = [original], MedicalNotes = "Unrelated notes" };
        var saved = JsonSerializer.Serialize(dossier);
        var restored = JsonSerializer.Deserialize<DossierRecord>(saved)!;
        Assert.That(restored.Tattoos.Single().Text, Is.EqualTo(original.Text));
        Assert.That(restored.Tattoos.Single().Zone, Is.EqualTo(original.Zone));
        Assert.That(restored.Tattoos.Single().Color, Is.EqualTo(original.Color));
        Assert.That(restored.MedicalNotes, Is.EqualTo(dossier.MedicalNotes));
        Assert.That(JsonSerializer.Deserialize<DossierRecord>("{}")!.Tattoos, Is.Empty);
        var clone = original.Clone();
        clone.Text = "Changed";
        Assert.That(original.Text, Is.Not.EqualTo(clone.Text));
    }

    [Test]
    public void NewPaperworkAndConsentStatesRoundTripThroughNetworkSerializer()
    {
        var roots = new[] { typeof(PhotocopierUiState), typeof(PhotocopierPrintMessage), typeof(PhotocopierUiKey),
            typeof(DocumentFormEntry), typeof(TattooUiState), typeof(TattooUiKey), typeof(TextTattoo),
            typeof(TattooProposeMessage), typeof(TattooConsentMessage), typeof(SaunaVisuals),
            typeof(HairAccessoryUiKey), typeof(HairAccessoryUiState), typeof(HairAccessorySelectMessage),
            typeof(FishScannerUiKey), typeof(FishScannerUiState), typeof(FishScanEntry),
            typeof(DancePoleUiKey), typeof(DancePoleUiState), typeof(DancePoleMessage), typeof(PoleDanceMove),
            typeof(DancePoleAction), typeof(DancePoleVisuals) };
        var serializer = new NetSerializer.Serializer(roots);
        using var stream = new MemoryStream();
        serializer.Serialize(stream, new TattooUiState("Patient", 20, new TextTattoo { Text = "Remember", Zone = 3, Color = 4 }, false));
        stream.Position = 0;
        var tattoo = (TattooUiState) serializer.Deserialize(stream);
        Assert.That(tattoo.Tattoo!.Text, Is.EqualTo("Remember"));
        Assert.That(tattoo.Tattoo.Zone, Is.EqualTo(3));
        Assert.That(tattoo.Tattoo.Color, Is.EqualTo(4));
        stream.SetLength(0);
        stream.Position = 0;
        serializer.Serialize(stream, new PhotocopierUiState([new("GeneralDocument", "General", "Frontier", "Content")], 12, 9, true, false));
        stream.Position = 0;
        var copier = (PhotocopierUiState) serializer.Deserialize(stream);
        Assert.That(copier.Forms.Single().Content, Is.EqualTo("Content"));
        Assert.That(copier.Paper, Is.EqualTo(12));
        Assert.That(copier.Ready, Is.False);
        stream.SetLength(0);
        stream.Position = 0;
        serializer.Serialize(stream, new HairAccessoryUiState(["HumanHairBun"], "HumanHairBun"));
        stream.Position = 0;
        var hair = (HairAccessoryUiState) serializer.Deserialize(stream);
        Assert.That(hair.Styles, Is.EqualTo(new[] { "HumanHairBun" }));
        Assert.That(hair.Selected, Is.EqualTo("HumanHairBun"));
        stream.SetLength(0);
        stream.Position = 0;
        var fish = new FishScanEntry("Bass", "fish-scanner-female", true, true, true, "fish-scanner-alive", 0, 25, Color.White);
        serializer.Serialize(stream, new FishScannerUiState("Tank", true, 100, 30, 4, [fish], 80));
        stream.Position = 0;
        var scan = (FishScannerUiState) serializer.Deserialize(stream);
        Assert.That(scan.Fish.Single(), Is.EqualTo(fish));
        Assert.That(scan.Charge, Is.EqualTo(80));
        stream.SetLength(0);
        stream.Position = 0;
        serializer.Serialize(stream, new DancePoleUiState(null, PoleDanceMove.Turn, 1, true, 2));
        stream.Position = 0;
        var pole = (DancePoleUiState) serializer.Deserialize(stream);
        Assert.That(pole.Move, Is.EqualTo(PoleDanceMove.Turn));
        Assert.That(pole.Light, Is.True);
    }

    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));

    [Test]
    public void FishScannerIsInServiceTechfabStaticRecipesAndPrintsWithoutABattery()
    {
        YamlMappingNode Find(string path, string id)
        {
            var yaml = new YamlStream();
            using var reader = File.OpenText(Path.Combine(Resources, "Prototypes", path));
            yaml.Load(reader);
            return ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
                .Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("id")]).Value == id);
        }
        var recipe = Find("_radiant/Recipes/Lathes/fishing.yml", "FishScanner");
        Assert.That(((YamlScalarNode) recipe.Children[new YamlScalarNode("result")]).Value, Is.EqualTo("FishScannerEmpty"));
        var pack = Find("_NF/Recipes/Lathes/Packs/misc.yml", "NFServiceToolsStatic");
        Assert.That(((YamlSequenceNode) pack.Children[new YamlScalarNode("recipes")]).Children
            .Cast<YamlScalarNode>().Select(node => node.Value), Does.Contain("FishScanner"));
        var lathe = Find("_NF/Entities/Structures/Machines/lathe.yml", "ServiceTechFab");
        var component = ((YamlSequenceNode) lathe.Children[new YamlScalarNode("components")]).Children
            .Cast<YamlMappingNode>().Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "Lathe");
        Assert.That(((YamlSequenceNode) component.Children[new YamlScalarNode("staticPacks")]).Children
            .Cast<YamlScalarNode>().Select(node => node.Value), Does.Contain("NFServiceToolsStatic"));
    }

    [TestCase("tag", "_radiant/Clothing/Neck/hateredsoul_dogtag_1.rsi/icon.png")]
    [TestCase("recorder", "Objects/Materials/Scrap/generic.rsi/junk-pai.png")]
    public void StoryRelicsUseTheReplacementSpritesWithoutChangingSourcePixels(string state, string source)
    {
        var actual = File.ReadAllBytes(Path.Combine(Resources, "Textures/_radiant/Objects/Misc/story_relics.rsi", state + ".png"));
        var expected = File.ReadAllBytes(Path.Combine(Resources, "Textures", source));
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void GamesVendorStocksAllFourGiftBoxesAndStylesExist()
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(Resources, "Prototypes/Catalog/VendingMachines/Inventories/games.yml")))
            yaml.Load(reader);
        var inventory = (YamlMappingNode) ((YamlMappingNode) ((YamlSequenceNode) yaml.Documents[0].RootNode).Children[0])
            .Children[new YamlScalarNode("startingInventory")];
        foreach (var id in new[] { "BoxQuestionDeck", "BoxDareDeck", "BoxAdultQuestionDeck", "BoxBlankCardDeck" })
            Assert.That(inventory.Children.ContainsKey(new YamlScalarNode(id)), Is.True, id);
        var ids = new HashSet<string>();
        foreach (var path in new[] { "Entities/Mobs/Customization/Markings/human_hair.yml", "_radiant/Entities/Mobs/Customization/Markings/additional_hair.yml" })
        {
            using var reader = File.OpenText(Path.Combine(Resources, "Prototypes", path));
            yaml.Load(reader);
            foreach (var marking in ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>())
                ids.Add(((YamlScalarNode) marking.Children[new YamlScalarNode("id")]).Value!);
        }
        foreach (var style in new HairAccessoryComponent().Styles)
            Assert.That(ids, Does.Contain(style));
        Assert.That(new HairAccessoryComponent().Styles, Does.Not.Contain("HumanHairLong"));
    }


    [Test]
    public void EveryFormHasBothTranslationsAndNoUnresolvedTemplateTokens()
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources, "Prototypes/_radiant/Paperwork/forms.yml"));
        yaml.Load(reader);
        var forms = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
        Assert.That(forms.Length, Is.EqualTo(16));
        foreach (var locale in new[] { "ru-RU", "en-US" })
        {
            var text = File.ReadAllText(Path.Combine(Resources, "Locale", locale, "_radiant/paperwork.ftl"));
            foreach (var form in forms)
            foreach (var key in new[] { "name", "category", "letterhead", "content" })
            {
                var id = ((YamlScalarNode) form.Children[new YamlScalarNode(key)]).Value;
                Assert.That(text, Does.Contain(id + " ="), locale + ": " + id);
            }
            foreach (var key in new[] { "$date", "$time", "$ship" })
                Assert.That(text, Does.Contain(key));
            Assert.That(text, Does.Not.Contain("___").And.Not.Contain("\u2014").And.Not.Contain("\u2013"));
        }
    }

    [TestCase("Structures/Machines/photocopier.rsi", "photocopier", 1)]
    [TestCase("Objects/Devices/printer_toner.rsi", "toner", 1)]
    [TestCase("Clothing/Head/HairAccessories/hair_tie.rsi", "hair_clip_equipped", 4)]
    [TestCase("Objects/Tools/tattoo_machine.rsi", "machine", 1)]
    [TestCase("Objects/Specific/Fishing/fish_scanner.rsi", "scanner", 1)]
    [TestCase("Structures/Furniture/sauna_heater.rsi", "lit", 1)]
    [TestCase("Objects/Fun/question_cards.rsi", "card", 1)]
    [TestCase("Objects/Misc/story_relics.rsi", "recorder", 1)]
    public void ImportedSpritesKeepLicenseAndNeutralPaths(string path, string state, int directions)
    {
        var folder = Path.Combine(Resources, "Textures/_radiant", path);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
        Assert.That(json.RootElement.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
        Assert.That(json.RootElement.GetProperty("copyright").GetString(), Is.Not.Empty);
        var entry = json.RootElement.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("name").GetString() == state);
        Assert.That(entry.TryGetProperty("directions", out var dirs) ? dirs.GetInt32() : 1, Is.EqualTo(directions));
        Assert.That(File.Exists(Path.Combine(folder, state + ".png")), Is.True);
    }
}
