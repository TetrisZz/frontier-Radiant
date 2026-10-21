using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Maths;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class WorkshopImportsTest
{
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(4, 5)]
    [TestCase(4, 0)]
    public void AquariumAppearanceRoundTripsThroughNetworkAndClientCopy(int count, int aliveMask)
    {
        var fish = new AquariumFishVisualState(
            Enumerable.Repeat("bass", count).ToArray(),
            Enumerable.Range(0, count).Select(i => (aliveMask & (1 << i)) != 0).ToArray(),
            Enumerable.Repeat(Color.Blue, count).ToArray(),
            Enumerable.Repeat(true, count).ToArray());
        // These are the registered payload roots, not an explicit whitelist of array types.
        var roots = new[]
        {
            typeof(AppearanceComponentState), typeof(AquariumVisuals), typeof(AquariumFishVisualState),
        };
        Assert.That(roots.All(t => Attribute.IsDefined(t, typeof(NetSerializableAttribute))), Is.True);
        var serializer = new NetSerializer.Serializer(roots);
        var state = new AppearanceComponentState(new Dictionary<Enum, object>
        {
            [AquariumVisuals.Fish] = fish,
            [AquariumVisuals.Decorations] = new[] { "rocks", "sand" },
            [AquariumVisuals.Water] = true,
        });
        using var stream = new MemoryStream();
        serializer.Serialize(stream, state);
        stream.Position = 0;
        var restored = (AppearanceComponentState) serializer.Deserialize(stream);
        var restoredFish = (AquariumFishVisualState) restored.Data[AquariumVisuals.Fish];
        Assert.That(restoredFish.States, Is.EqualTo(fish.States));
        Assert.That(restoredFish.Alive, Is.EqualTo(fish.Alive));
        Assert.That(restoredFish.Colors, Is.EqualTo(fish.Colors));
        Assert.That(restoredFish.Juveniles, Is.EqualTo(fish.Juveniles));
        Assert.That(restored.Data[AquariumVisuals.Decorations], Is.EqualTo(state.Data[AquariumVisuals.Decorations]));
        Assert.That(restored.Data[AquariumVisuals.Water], Is.True);

        Assert.That(restoredFish, Is.InstanceOf<ICloneable>(), "Appearance payloads must support client-side cloning");
        var cloneMethod = typeof(Robust.Client.GameObjects.AppearanceSystem).GetMethod(
            "CloneAppearanceData", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(cloneMethod, Is.Not.Null);
        var copied = (Dictionary<Enum, object>) cloneMethod!.Invoke(
            new Robust.Client.GameObjects.AppearanceSystem(), new object[] { restored.Data })!;
        var copiedFish = (AquariumFishVisualState) copied[AquariumVisuals.Fish];
        Assert.That(copiedFish, Is.Not.SameAs(restoredFish));
        Assert.That(copiedFish.States, Is.EqualTo(restoredFish.States).And.Not.SameAs(restoredFish.States));
        Assert.That(copiedFish.Alive, Is.EqualTo(restoredFish.Alive).And.Not.SameAs(restoredFish.Alive));
        Assert.That(copiedFish.Colors, Is.EqualTo(restoredFish.Colors).And.Not.SameAs(restoredFish.Colors));
        Assert.That(copiedFish.Juveniles, Is.EqualTo(restoredFish.Juveniles).And.Not.SameAs(restoredFish.Juveniles));
        Assert.That(copied[AquariumVisuals.Decorations], Is.EqualTo(restored.Data[AquariumVisuals.Decorations])
            .And.Not.SameAs(restored.Data[AquariumVisuals.Decorations]));
        Assert.That(copied[AquariumVisuals.Water], Is.True);
        if (count > 0)
        {
            copiedFish.States[0] = "clown";
            copiedFish.Alive[0] = !copiedFish.Alive[0];
            Assert.That(restoredFish.States[0], Is.EqualTo("bass"));
            Assert.That(restoredFish.Alive[0], Is.EqualTo((aliveMask & 1) != 0));
        }
    }

    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
    private static YamlNode Field(YamlMappingNode node, string name) => node.Children[new YamlScalarNode(name)];
    private static string Value(YamlMappingNode node, string name) => ((YamlScalarNode)Field(node, name)).Value!;
    private static YamlMappingNode[] Sequence(YamlNode node) => ((YamlSequenceNode)node).Children.Cast<YamlMappingNode>().ToArray();
    private static YamlMappingNode[] Read(string path)
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources, path));
        yaml.Load(reader);
        return Sequence(yaml.Documents[0].RootNode);
    }
    private static YamlMappingNode Component(YamlMappingNode entity, string type) => Sequence(Field(entity, "components")).Single(n => Value(n, "type") == type);

    [Test]
    public void AllEightItemsHaveMatchingConstructionGraphs()
    {
        var recipes = Read("Prototypes/_radiant/Recipes/Construction/imported_workstations.yml");
        var graphs = Read("Prototypes/_radiant/Recipes/Construction/Graphs/imported_workstations.yml");
        Assert.That(recipes.Length, Is.EqualTo(8));
        Assert.That(graphs.Length, Is.EqualTo(8));
        foreach (var recipe in recipes)
        {
            var id = Value(recipe, "id");
            Assert.That(Value(recipe, "graph"), Is.EqualTo(id));
            var graph = graphs.Single(g => Value(g, "id") == id);
            var nodes = Sequence(Field(graph, "graph"));
            var final = nodes.Single(n => Value(n, "node") == "finished");
            Assert.That(Value(final, "entity"), Is.EqualTo(id));
            var reverse = Sequence(Field(final, "edges")).Single();
            var completions = Sequence(Field(reverse, "completed"));
            Assert.That(completions.First().Tag.Value, Is.EqualTo("!type:EmptyAllContainers"), "Stored items must be ejected before dismantling");
            Assert.That(completions.Last().Tag.Value, Is.EqualTo("!type:DeleteEntity"));
        }
    }

    [TestCase("_radiant/Structures/Furniture/open_stairs.rsi", "stairs_frame", 4, 1)]
    [TestCase("_radiant/Structures/Furniture/open_stairs.rsi", "stairs_frame_wood", 4, 1)]
    [TestCase("_radiant/Structures/Storage/surgical_cart.rsi", "medicart", 4, 2)]
    [TestCase("_radiant/Objects/Fun/hourglass.rsi", "hourglass_flip", 1, 7)]
    [TestCase("_radiant/Objects/Fun/hourglass.rsi", "hourglass_1", 1, 3)]
    [TestCase("_radiant/Objects/Fun/hourglass.rsi", "hourglass_2", 1, 6)]
    [TestCase("_radiant/Objects/Fun/hourglass.rsi", "hourglass_7", 1, 3)]
    [TestCase("_radiant/Objects/Specific/Fishing/aquarium_supplies.rsi", "seaweeds_front", 1, 8)]
    public void SourceDirectionsAndAnimationsArePreserved(string folder, string name, int directions, int frames)
    {
        var directory = Path.Combine(Resources, "Textures", folder);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var root = json.RootElement;
        Assert.That(root.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
        var state = root.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        Assert.That(state.TryGetProperty("directions", out var dirs) ? dirs.GetInt32() : 1, Is.EqualTo(directions));
        if (frames > 1)
            Assert.That(state.GetProperty("delays").EnumerateArray().All(d => d.GetArrayLength() == frames), Is.True);
        var png = File.ReadAllBytes(Path.Combine(directory, name + ".png"));
        var width = Dimension(png, 16);
        var height = Dimension(png, 20);
        Assert.That(width % 32, Is.Zero);
        Assert.That(height % 32, Is.Zero);
        Assert.That(width / 32 * (height / 32), Is.GreaterThanOrEqualTo(directions * frames));
    }

    [Test]
    public void RackHasNineDistinctStatesAndEightVialCapacity()
    {
        var path = Path.Combine(Resources, "Textures/_radiant/Objects/Medical/tube_rack.rsi");
        var images = Enumerable.Range(0, 9).Select(i => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(path, i + ".png"))));
        Assert.That(images.Distinct().Count(), Is.EqualTo(9));
        var rack = Read("Prototypes/_radiant/Entities/Structures/Furniture/imported_workstations.yml").Single(n => Value(n, "id") == "RadiantTestTubeRack");
        Assert.That(Value(Component(rack, "Storage"), "hideStackVisualsWhenClosed"), Is.EqualTo("false"),
            "The item counter must not hide the entire rack when its storage window is closed");
        var counter = Component(rack, "ItemCounter");
        Assert.That(Value(counter, "amount"), Is.EqualTo("8"));
        var states = ((YamlSequenceNode)Field(counter, "layerStates")).Children.Cast<YamlScalarNode>().Select(n => n.Value).ToArray();
        Assert.That(states.Length, Is.EqualTo(9));
        var sprite = Component(rack, "Sprite");
        var layer = Sequence(Field(sprite, "layers")).Single();
        Assert.That(Value(layer, "state"), Is.EqualTo(states[0]));
        Assert.That(((YamlSequenceNode)Field(layer, "map")).Children.Cast<YamlScalarNode>().Select(n => n.Value), Does.Contain(Value(counter, "baseLayer")));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "meta.json")));
        var available = metadata.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString());
        Assert.That(available, Is.EquivalentTo(states));
    }

    [Test]
    public void BeeJarIsNotASeedAndFramesCannotBeRefilledFromOtherLiquids()
    {
        var jar = Read("Prototypes/_NF/Entities/Objects/Specific/Hydroponics/seeds.yml").Single(n => Value(n, "id") == "BeeSeeds");
        Assert.That(Value(jar, "parent"), Is.EqualTo("BaseItem"));
        var jarComponents = Sequence(Field(jar, "components")).Select(n => Value(n, "type")).ToArray();
        Assert.That(jarComponents, Does.Contain("BeeColony"));
        Assert.That(jarComponents, Does.Not.Contain("Seed"));
        Assert.That(Read("Prototypes/_NF/Hydroponics/seeds.yml").Any(n => Value(n, "id") == "bees"), Is.False);
        var frame = Read("Prototypes/_radiant/Entities/Structures/Hydroponics/apiary.yml").Single(n => Value(n, "id") == "RadiantHoneyFrame");
        Assert.That(Value(Component(frame, "SolutionTransfer"), "canReceive"), Is.EqualTo("false"));
        Assert.That(Sequence(Field(frame, "components")).Any(n => Value(n, "type") == "RefillableSolution"), Is.False);
    }

    [Test]
    public void FishUseAquariumFoodAndHaveTimeForTransport()
    {
        var fish = Read("Prototypes/_radiant/Fishing/fish.yml").Single(n => Value(n, "id") == "BaseFish");
        Assert.That(Value(Component(fish, "Hunger"), "baseDecayRate"), Is.EqualTo("0"));
        Assert.That(Value(Component(fish, "Thirst"), "baseDecayRate"), Is.EqualTo("0"));
        Assert.That(new FishComponent().AirGracePeriod, Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    [Test]
    public void EveryFishHasASeparateAquariumSprite()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures/_radiant/Objects/Specific/Fishing/aquarium_fish.rsi/meta.json")));
        var smallStates = json.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToHashSet();
        var fish = Read("Prototypes/_radiant/Fishing/fish.yml");
        var count = 0;
        foreach (var entity in fish.Where(n => !Value(n, "id").StartsWith("Base", StringComparison.Ordinal)))
        {
            var component = Component(entity, "Fish");
            var state = Value(component, "aquariumState");
            Assert.That(smallStates, Does.Contain(state));
            Assert.That(Value(Component(entity, "Sprite"), "state"), Is.EqualTo(state));
            count++;
        }
        Assert.That(count, Is.EqualTo(19));
    }

    [Test]
    public void FilledCartToolsHaveMappedOriginalSprites()
    {
        var entities = Read("Prototypes/_radiant/Entities/Structures/Furniture/imported_workstations.yml");
        var cart = entities.Single(n => Value(n, "id") == "RadiantSurgicalCartFilled");
        var tools = Sequence(Field(Component(cart, "StorageFill"), "contents")).Select(n => Value(n, "id"));
        var map = new SurgicalCartVisualsComponent().ToolStates;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures/_radiant/Structures/Storage/surgical_cart.rsi/meta.json")));
        var states = json.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToHashSet();
        foreach (var tool in tools)
        {
            Assert.That(map.ContainsKey(tool), Is.True, tool);
            Assert.That(states, Does.Contain(map[tool]));
        }
    }

    [TestCase("Prototypes/_radiant/Entities/Structures/Furniture/imported_workstations.yml", "RadiantSurgicalCart")]
    [TestCase("Prototypes/_radiant/Entities/Structures/Furniture/aquarium.yml", "RadiantAquarium")]
    [TestCase("Prototypes/_radiant/Entities/Structures/Hydroponics/apiary.yml", "RadiantBeehive")]
    public void DestructibleStructuresHaveDamageContainers(string path, string id)
    {
        var entity = Read(path).Single(n => Value(n, "id") == id);
        Assert.That(Value(Component(entity, "Damageable"), "damageContainer"), Is.EqualTo("StructuralInorganic"));
        Assert.That(Component(entity, "Destructible"), Is.Not.Null);
    }

    private static int Dimension(byte[] bytes, int start) => bytes[start] << 24 | bytes[start + 1] << 16 | bytes[start + 2] << 8 | bytes[start + 3];
}
