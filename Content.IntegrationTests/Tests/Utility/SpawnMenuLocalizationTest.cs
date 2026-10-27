using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class SpawnMenuLocalizationTest
{
    [Test]
    public async Task ExtendedScreenshotsUseRussianNamesAndResolvableReferences()
    {
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources", "Locale", "ru-RU"));
        var translations = Path.Combine(root, "_radiant", "entities", "spawn_menu_extended.ftl");
        var ids = File.ReadLines(translations)
            .Where(line => line.StartsWith("ent-", StringComparison.Ordinal))
            .Select(line => line[4..line.IndexOf('=')].TrimEnd())
            .Concat(new[] { "ClothingBackpackDebug4", "DebugConsumer" }).ToArray();
        Assert.That(ids, Has.Length.GreaterThan(200));
        var keys = ids.ToDictionary(id => "ent-" + id, _ => 0);
        foreach (var file in Directory.EnumerateFiles(root, "*.ftl", SearchOption.AllDirectories))
        foreach (var line in File.ReadLines(file))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || !line.StartsWith("ent-", StringComparison.Ordinal))
                continue;
            var key = line[..separator].TrimEnd();
            if (keys.ContainsKey(key))
                keys[key]++;
        }
        foreach (var (key, count) in keys)
            Assert.That(count, Is.EqualTo(1), key);

        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var loc = pair.Client.ResolveDependency<ILocalizationManager>();
                var original = loc.DefaultCulture;
                try
                {
                    var russian = CultureInfo.GetCultureInfo("ru-RU");
                    if (!loc.HasCulture(russian))
                        loc.LoadCulture(russian);
                    loc.DefaultCulture = russian;
                    Assert.Multiple(() =>
                    {
                        foreach (var id in ids)
                        {
                            Assert.That(pair.Client.ProtoMan.HasIndex<EntityPrototype>(id), Is.True, id);
                            Assert.That(loc.TryGetString("ent-" + id, out var name), Is.True, id);
                            Assert.That(name?.Any(IsRussian), Is.True, id + ": " + name);
                            Assert.That(loc.GetEntityData(id).Name, Is.EqualTo(name), id + ": actual menu name");
                            Assert.That(loc.TryGetString("ent-" + id + ".desc", out var description), Is.True, id + ": description reference");
                            if (!string.IsNullOrWhiteSpace(description))
                                Assert.That(description.Any(IsRussian), Is.True, id + ": " + description);
                            if (loc.GetEntityData(id).Suffix is { } suffix)
                                foreach (var english in new[] { "Random", "Reduced", "VeryLow", "Filled", "Empty", "Hostile", "Wallmount", "Frontier", "Stash" })
                                    Assert.That(suffix, Does.Not.Contain(english), id);
                        }
                    });
                    Assert.That(loc.GetEntityData("NFMobSpawnCoalGolem").Name, Does.Contain("угольного"));
                    Assert.That(loc.GetString("ent-GrenadeCleanade"), Is.EqualTo("чистящий снаряд"),
                        "The retained translation alias also serves the current NFGrenadeCleanade prototype.");
                    Assert.That(loc.GetEntityData("MaterialClothRandomVeryLowRS").Suffix,
                        Is.EqualTo(loc.GetEntityData("MaterialBluespaceRandomVeryLow").Suffix));
                    Assert.That(loc.GetEntityData("ClothingWalletLeatherRed").Name, Does.Contain("красный"));
                    TestContext.Out.WriteLine($"Checked {ids.Length} localized entities, their descriptions, suffixes, and unique keys.");
                }
                finally
                {
                    loc.DefaultCulture = original;
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }

        static bool IsRussian(char character) => character is >= '\u0410' and <= '\u044f' or '\u0401' or '\u0451';
    }

    [Test]
    public async Task ScreenshotItemsUseRussianNamesAndExistingAnaloguesWithoutDuplicateKeys()
    {
        var names = new Dictionary<string, string>
        {
            ["MaterialBluespaceRandomVeryReducedRS"] = "блюспейс кристалл",
            ["MaterialBluespaceRandomVeryLowRS"] = "блюспейс кристалл",
            ["MaterialBonesRandomVeryReducedRS"] = "кости",
            ["MaterialBonesRandomVeryLowRS"] = "кости",
            ["NFHolopadBarrier"] = "голопад",
            ["NFHolopadAcolithBridge"] = "голопад",
            ["NFHolopadAcolithCommandant"] = "голопад",
            ["NFHolopadAcolith"] = "голопад",
            ["NFHolopadAcolithRND"] = "голопад",
            ["NFHolopadShipMachineCircuitboard"] = "плата блюспейс голопада",
            ["BoneGelCyber"] = "флакон костного геля",
            ["BookRandomSimple"] = "книга",
            ["ChemistryBottleMysteryLiquid"] = "бутылочка",
            ["ChemistryBottleStelloxadone"] = "бутылочка",
            ["MagazineCaselessRifle10x24RS"] = "коробчатый магазин (.25 безгильзовые)",
            ["BoxCartridgeRGBB"] = "коробка шариков RGBB",
            ["NFBoxDonkSoftBox"] = "коробка поролоновых дротиков",
            ["NFBoxCartridgeCap"] = "коробка пистонов",
            ["BoxSeparatistSlaveKit"] = "подарочный набор с шоковым ошейником",
            ["BoxSeparatistFlashKit"] = "подарочный набор со вспышками",
            ["OrganShelegBrain"] = "мозг",
        };
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources", "Locale", "ru-RU"));
        var counts = names.Keys.ToDictionary(id => "ent-" + id, _ => 0);
        foreach (var file in Directory.EnumerateFiles(root, "*.ftl", SearchOption.AllDirectories))
        foreach (var line in File.ReadLines(file))
        {
            if (!line.StartsWith("ent-", StringComparison.Ordinal) || line.IndexOf('=') is not (> 0 and var separator))
                continue;
            var key = line[..separator].TrimEnd();
            if (counts.ContainsKey(key))
                counts[key]++;
        }
        foreach (var (key, count) in counts)
            Assert.That(count, Is.EqualTo(1), key + " must be defined once, rather than duplicated in a second file.");

        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var loc = pair.Client.ResolveDependency<ILocalizationManager>();
                var original = loc.DefaultCulture;
                try
                {
                    var russian = CultureInfo.GetCultureInfo("ru-RU");
                    if (!loc.HasCulture(russian))
                        loc.LoadCulture(russian);
                    loc.DefaultCulture = russian;
                    var prototypes = pair.Client.ProtoMan;
                    foreach (var (id, expected) in names)
                    {
                        Assert.That(prototypes.HasIndex<EntityPrototype>(id), Is.True, id);
                        Assert.That(loc.TryGetString("ent-" + id, out var name), Is.True, id);
                        Assert.That(name, Is.EqualTo(expected), id);
                        var data = loc.GetEntityData(id);
                        Assert.That(data.Name, Is.EqualTo(expected), id + ": actual spawn-menu name");
                        Assert.That(data.Desc, Does.Not.Contain("Resources/Locale/"), id);
                    }
                    foreach (var (id, analogue) in new[]
                    {
                        ("MaterialBluespaceRandomVeryReducedRS", "MaterialBluespaceRandomReduced"),
                        ("MaterialBluespaceRandomVeryLowRS", "MaterialBluespaceRandomVeryLow"),
                        ("MaterialBonesRandomVeryReducedRS", "MaterialBonesRandomReduced"),
                        ("MaterialBonesRandomVeryLowRS", "MaterialBonesRandomVeryLow"),
                        ("BookRandomSimple", "BookRandom"),
                    })
                        Assert.That(loc.GetEntityData(id).Suffix, Is.EqualTo(loc.GetEntityData(analogue).Suffix), id);
                    Assert.That(loc.GetEntityData("NFHolopadAcolithCommandant").Suffix, Is.EqualTo("МНБ Комендант, POI"));
                    Assert.That(loc.GetEntityData("NFHolopadAcolithRND").Suffix, Is.EqualTo("МНБ Исследования, POI"));
                    Assert.That(loc.GetEntityData("ChemistryBottleMysteryLiquid").Suffix, Is.EqualTo("Неизвестная жидкость"));
                    Assert.That(loc.GetEntityData("ChemistryBottleStelloxadone").Suffix, Is.EqualTo(loc.GetString("reagent-name-stelloxadone")));
                    foreach (var id in new[] { "BoxSeparatistSlaveKit", "BoxSeparatistFlashKit" })
                    {
                        Assert.That(loc.GetEntityData(id).Suffix, Is.EqualTo("Заполненный"));
                        Assert.That(loc.GetEntityData(id).Desc, Does.Not.Contain("гранат"));
                    }
                }
                finally
                {
                    loc.DefaultCulture = original;
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
