using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class ClothingCatalogCleanupTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
    private static YamlNode Field(YamlMappingNode node, string key) => node.Children[new YamlScalarNode(key)];
    private static string Text(YamlMappingNode node, string key) => ((YamlScalarNode) Field(node, key)).Value!;
    private static YamlMappingNode[] Nodes(YamlNode node) => ((YamlSequenceNode) node).Children.Cast<YamlMappingNode>().ToArray();

    private static YamlNode Read(string path)
    {
        using var reader = File.OpenText(Path.Combine(Resources, path));
        var stream = new YamlStream();
        stream.Load(reader);
        return stream.Documents[0].RootNode;
    }

    [Test]
    public void ResourcePathsDoNotUseSourceServerName()
    {
        foreach (var folder in new[] { "Prototypes/_radiant", "Textures/_radiant", "Locale/ru-RU/_radiant", "Locale/en-US/_radiant" })
        {
            var root = Path.Combine(Resources, folder);
            var namedAfterSource = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Contains("bluemoon", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.That(namedAfterSource, Is.Empty, folder);
        }
    }

    [Test]
    public void RenamedCasualPrototypeFilePreservesEntityIds()
    {
        var entities = Nodes(Read("Prototypes/_radiant/Entities/Clothing/Uniforms/casual.yml"));
        Assert.That(entities.Select(n => Text(n, "id")), Is.EquivalentTo(new[]
        {
            "ClothingUniformRadiantWhiteTshirt", "ClothingUniformRadiantGrayTshirt", "ClothingUniformRadiantSlimBodyShorts",
        }));
        foreach (var locale in new[] { "ru-RU", "en-US" })
        {
            var text = File.ReadAllText(Path.Combine(Resources, "Locale", locale, "_radiant/clothing/casual.ftl"));
            foreach (var entity in entities)
                Assert.That(text, Does.Contain("ent-" + Text(entity, "id") + " = "));
        }
    }

    [TestCase("poster_bread")]
    [TestCase("poster_dymai")]
    public void RenamedPosterAssetStillHasBothStates(string state)
    {
        var posters = Nodes(Read("Prototypes/_radiant/Entities/Structures/Wallmounts/Signs/posters.yml"));
        var bread = posters.Single(n => Text(n, "id") == "PosterContrabandBreadRS");
        var sprite = Nodes(Field(bread, "components")).Single(n => Text(n, "type") == "Sprite");
        var directory = Path.Combine(Resources, "Textures", Text(sprite, "sprite"));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        Assert.That(metadata.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()), Does.Contain(state));
        Assert.That(File.Exists(Path.Combine(directory, state + ".png")), Is.True);
    }

    [Test]
    public void HoodedCoatIsInHomkomat()
    {
        var inventory = Nodes(Read("Prototypes/_radiant/Catalog/VendingMachines/Inventories/_xomkavend.yml")).Single();
        var stock = (YamlMappingNode) Field(inventory, "startingInventory");
        Assert.That(Text(stock, "ClothingOuterCoatOccult"), Is.EqualTo(uint.MaxValue.ToString()));
    }

    [TestCase("PerfumeRose", "rose")]
    [TestCase("PerfumeLavender", "lavender")]
    [TestCase("PerfumeCitrus", "citrus")]
    [TestCase("PerfumePine", "pine")]
    [TestCase("PerfumeOcean", "ocean")]
    [TestCase("PerfumeVanilla", "vanilla")]
    [TestCase("PerfumeCherryLiqueur", "rose")]
    [TestCase("PerfumeTobaccoVanilla", "vanilla")]
    [TestCase("PerfumePearSilk", "citrus")]
    [TestCase("PerfumeWhiteTea", "pine")]
    [TestCase("PerfumeMidnightCoffee", "lavender")]
    [TestCase("PerfumeSaltedSkin", "ocean")]
    public void PerfumesKeepWorldIconsButHaveNoInhandLayers(string id, string state)
    {
        var prototypes = Nodes(Read("Prototypes/_radiant/Entities/Objects/Consumable/perfume.yml"));
        var parent = prototypes.Single(n => Text(n, "id") == "BasePerfume");
        var components = Nodes(Field(parent, "components"));
        var item = components.Single(n => Text(n, "type") == "Item");
        var inhands = (YamlMappingNode) Field(item, "inhandVisuals");
        foreach (var hand in new[] { "left", "right" })
            Assert.That(((YamlSequenceNode) Field(inhands, hand)).Children, Is.Empty);
        Assert.That(item.Children.ContainsKey(new YamlScalarNode("heldPrefix")), Is.False);
        Assert.That(item.Children.ContainsKey(new YamlScalarNode("sprite")), Is.False);
        var child = prototypes.Single(n => Text(n, "id") == id);
        Assert.That(Text(child, "parent"), Is.EqualTo("BasePerfume"));
        if (child.Children.TryGetValue(new YamlScalarNode("components"), out var childComponents))
        {
            Assert.That(Nodes(childComponents).Any(n => Text(n, "type") == "Item"), Is.False);
            Assert.That(Text(Nodes(childComponents).Single(n => Text(n, "type") == "Sprite"), "state"), Is.EqualTo(state));
        }
        var sprite = components.Single(n => Text(n, "type") == "Sprite");
        var directory = Path.Combine(Resources, "Textures", Text(sprite, "sprite"));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var states = metadata.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.That(states, Does.Contain(state));
        Assert.That(states.Any(s => s!.Contains("inhand", StringComparison.Ordinal)), Is.False);
        Assert.That(File.Exists(Path.Combine(directory, state + ".png")), Is.True);
    }

    [Test]
    public void CosmeticsStocksSixDistinctLocalizedCompositions()
    {
        var entities = Nodes(Read("Prototypes/_radiant/Entities/Objects/Consumable/perfume.yml"));
        var inventory = Nodes(Read("Prototypes/_radiant/Catalog/VendingMachines/Inventories/cosmetics.yml")).Single();
        var stock = (YamlMappingNode) Field(inventory, "startingInventory");
        var perfumes = stock.Children.Keys.Cast<YamlScalarNode>().Select(n => n.Value!)
            .Where(id => id.StartsWith("Perfume", StringComparison.Ordinal)).ToArray();
        Assert.That(perfumes, Is.EquivalentTo(new[]
        {
            "PerfumeCherryLiqueur", "PerfumeTobaccoVanilla", "PerfumePearSilk", "PerfumeWhiteTea",
            "PerfumeMidnightCoffee", "PerfumeSaltedSkin",
        }));
        var fragrances = perfumes.Select(id =>
        {
            Assert.That(Text(stock, id), Is.EqualTo("6"));
            var entity = entities.Single(n => Text(n, "id") == id);
            return Text(Nodes(Field(entity, "components")).Single(n => Text(n, "type") == "Perfume"), "fragrance");
        }).ToArray();
        Assert.That(fragrances.Distinct().Count(), Is.EqualTo(6));
        foreach (var locale in new[] { "ru-RU", "en-US" })
        {
            var text = File.ReadAllText(Path.Combine(Resources, "Locale", locale, "_radiant/perfume.ftl"));
            foreach (var id in perfumes)
                Assert.That(text, Does.Contain("ent-" + id + " = "));
            foreach (var fragrance in fragrances)
                Assert.That(text, Does.Contain(fragrance + " = "));
        }
    }

    [Test]
    public void CosmeticsVendorHasCompleteDedicatedSpriteStates()
    {
        var machine = Nodes(Read("Prototypes/_radiant/Entities/Structures/cosmetics_vending.yml"))
            .Single(n => Text(n, "id") == "VendingMachineCosmetics");
        var components = Nodes(Field(machine, "components"));
        var sprite = components.Single(n => Text(n, "type") == "Sprite");
        var vendor = components.Single(n => Text(n, "type") == "VendingMachine");
        Assert.That(Text(vendor, "denyState"), Is.EqualTo("deny"));
        var directory = Path.Combine(Resources, "Textures", Text(sprite, "sprite"));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var states = metadata.RootElement.GetProperty("states").EnumerateArray().ToArray();
        Assert.That(states.Select(s => s.GetProperty("name").GetString()),
            Is.EquivalentTo(new[] { "normal", "off", "broken", "panel", "screen", "deny" }));
        foreach (var state in states)
        {
            var name = state.GetProperty("name").GetString()!;
            Assert.That(File.Exists(Path.Combine(directory, name + ".png")), Is.True, name);
            if (name is "normal" or "screen" or "deny")
                Assert.That(state.GetProperty("delays")[0].GetArrayLength(), Is.EqualTo(name == "deny" ? 2 : 6));
        }
    }

    [Test]
    public void AngularFeroxiTailIsCompletelyRemoved()
    {
        Assert.That(Directory.Exists(Path.Combine(Resources, "Textures/_radiant/Mobs/Customization/Feroxi/angular_tail.rsi")), Is.False);
        Assert.That(File.Exists(Path.Combine(Resources, "Prototypes/_radiant/Entities/Mobs/Customization/Markings/feroxi_tails.yml")), Is.False);
        foreach (var locale in new[] { "ru-RU", "en-US" })
            Assert.That(File.Exists(Path.Combine(Resources, "Locale", locale, "_radiant/markings/feroxi.ftl")), Is.False);
    }

    [Test]
    public void RetiredItemsAndTheirLoadoutsAreAbsent()
    {
        Assert.That(File.Exists(Path.Combine(Resources, "Prototypes/_radiant/Entities/Clothing/Head/blueflame.yml")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(Resources, "Textures/_radiant/Clothing/Head/Hats/blueflame.rsi")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(Resources, "Textures/_radiant/Clothing/Underwear/bottom/nekopanties.rsi")), Is.False);
        var panties = Nodes(Read("Prototypes/_radiant/Entities/Clothing/Underwear/panties.yml"));
        Assert.That(panties.Select(n => Text(n, "id")), Does.Not.Contain("ClothingUnderwearBottomPantiesNeko"));
        foreach (var path in new[]
        {
            "Prototypes/_NF/Loadouts/Jobs/Contractor/UnderwearBottom.yml",
            "Prototypes/_radiant/Loadouts/Jobs/AcorpAssistant/UnderwearBottom.yml",
            "Prototypes/_radiant/Loadouts/Jobs/basic_underwear.yml",
        })
        {
            foreach (var loadout in Nodes(Read(path)))
            {
                var equipment = (YamlMappingNode) Field(loadout, "equipment");
                if (equipment.Children.TryGetValue(new YamlScalarNode("underwearb"), out var value))
                    Assert.That(((YamlScalarNode) value).Value, Is.Not.EqualTo("ClothingUnderwearBottomPantiesNeko"));
            }
        }
    }

    [Test]
    public void MapHasNoReferencesToRemovedDresserItem()
    {
        var map = (YamlMappingNode) Read("Maps/_NF/POI/tinnia.yml");
        var groups = Nodes(Field(map, "entities"));
        Assert.That(groups.Select(n => Text(n, "proto")), Does.Not.Contain("ClothingUnderwearBottomPantiesNeko"));
        var entities = groups.SelectMany(n => Nodes(Field(n, "entities"))).ToArray();
        Assert.That(entities.Select(n => Text(n, "uid")), Does.Not.Contain("1442"));
        Assert.That(Text((YamlMappingNode) Field(map, "meta"), "entityCount"), Is.EqualTo(entities.Length.ToString()));
        var dresser = entities.Single(n => Text(n, "uid") == "1439");
        var storage = Nodes(Field(dresser, "components")).Single(n => Text(n, "type") == "Storage");
        Assert.That(((YamlMappingNode) Field(storage, "storedItems")).Children.ContainsKey(new YamlScalarNode("1442")), Is.False);
    }
}
