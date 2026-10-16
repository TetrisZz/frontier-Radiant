using System.IO;
using System.Linq;
using Content.Shared._radiant.GhostSkins;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class GhostSkinPrototypeTest : ContentUnitTest
{
    private IPrototypeManager _prototypes;

    [OneTimeSetUp]
    public void LoadSkins()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _prototypes.Initialize();
        var file = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..",
            "Resources/Prototypes/_radiant/GhostSkins/skins.yml"));
        var yaml = new YamlStream();
        using (var reader = File.OpenText(file)) yaml.Load(reader);
        var skins = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .Where(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "ghostSkin").ToArray();
        var filtered = new YamlStream(new YamlDocument(new YamlSequenceNode(skins)));
        using var writer = new StringWriter();
        filtered.Save(writer, false);
        _prototypes.LoadString(writer.ToString());
        _prototypes.ResolveResults();
    }

    [TestCase(null, false, "GhostClassic")]
    [TestCase("RemovedSkin", true, "GhostClassic")]
    [TestCase("GhostCat", false, "GhostCat")]
    [TestCase("GhostOriginal", false, "GhostOriginal")]
    [TestCase("GhostFoxMask", false, "GhostClassic")]
    [TestCase("GhostFoxMask", true, "GhostFoxMask")]
    public void InvalidOrLockedPreferencesFallBackWithoutGrantingAccess(string saved, bool supporter, string expected)
        => Assert.That(GhostSkinRules.Resolve(_prototypes, saved, supporter), Is.EqualTo(expected));

    [Test]
    public void EngineDeserializesAllNineRsiSkinsAndTheirScales()
    {
        Assert.That(_prototypes.EnumeratePrototypes<GhostSkinPrototype>().Count(), Is.EqualTo(9));
        foreach (var skin in _prototypes.EnumeratePrototypes<GhostSkinPrototype>())
        {
            Assert.That(skin.Sprite, Is.TypeOf<SpriteSpecifier.Rsi>());
            Assert.That(skin.Scale.X, Is.EqualTo(skin.SupporterOnly ? 1f / 3f : 1f).Within(0.00001));
            Assert.That(skin.Scale.Y, Is.EqualTo(skin.Scale.X));
        }
    }

    [Test]
    public void OriginalAppearanceUsesTheExistingObserverSpriteAndTransparency()
    {
        var skin = _prototypes.Index<GhostSkinPrototype>("GhostOriginal");
        Assert.That(GhostSkinRules.CanSelect(skin, false), Is.True);
        Assert.That(skin.Sprite, Is.EqualTo(new SpriteSpecifier.Rsi(new ResPath("Mobs/Ghosts/ghost_human.rsi"), "animated")));
        Assert.That(skin.Color, Is.EqualTo(Robust.Shared.Maths.Color.FromHex("#fff8")));
    }
}
