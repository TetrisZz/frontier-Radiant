using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Interaction.Panel;
using Content.Client.Interaction.Panel.Ui;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class InteractionActionFileTest : ContentUnitTest
{
    private InteractionPanelManager _files = default!;

    protected override Assembly[] GetContentAssemblies()
        => base.GetContentAssemblies().Append(typeof(InteractionPanelPreferences).Assembly).ToArray();

    [OneTimeSetUp]
    public void InitializeSerialization()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _files = new InteractionPanelManager();
        IoCManager.InjectDependencies(_files);
    }

    [Test]
    public void ExportImportPreservesMessagesSoundTimingAndExplicitEmptyRestrictions()
    {
        var source = new InteractionPrototype
        {
            ID = "CustomWave_1", Name = "Friendly wave", Solo = false, Category = "hands",
            DoAfterDelay = 1.5f, UseDelay = TimeSpan.FromSeconds(4),
            UserMessages = ["waves to $target"], TargetMessages = ["$user waves to you"], OtherMessages = ["$user waves"],
            AllowedGenders = ["all"], AllowedSpecies = ["Human", "Diona"],
            UserBlockedClothingSlots = [], TargetBlockedClothingSlots = ["gloves"],
            InteractSound = new SoundPathSpecifier("/Audio/Effects/pop.ogg"), SoundPerceivedByOthers = false,
            TargetEntityId = ["123"], Points = 7, ArousalPoints = 9, PartnerArousalMultiplier = 0.5f
        };
        using var writer = new StringWriter();
        _files.ToDataNode(source).Write(writer);
        using var stream = new MemoryStream(EncodingHelpers.UTF8.GetBytes(writer.ToString()));
        var result = _files.FromStream(stream).Single();
        Assert.That(stream.CanRead, Is.True, "The caller owns the source stream.");
        Assert.That(result.Name, Is.EqualTo(source.Name));
        Assert.That(result.UserMessages, Is.EqualTo(source.UserMessages));
        Assert.That(result.TargetMessages, Is.EqualTo(source.TargetMessages));
        Assert.That(result.OtherMessages, Is.EqualTo(source.OtherMessages));
        Assert.That(result.DoAfterDelay, Is.EqualTo(source.DoAfterDelay));
        Assert.That(result.UseDelay, Is.EqualTo(source.UseDelay));
        Assert.That(result.UserBlockedClothingSlots, Is.Empty);
        Assert.That(result.TargetBlockedClothingSlots, Is.EqualTo(source.TargetBlockedClothingSlots));
        Assert.That(result.RequiredClothingSlots, Is.Null);
        Assert.That(result.Points, Is.EqualTo(source.Points));
        Assert.That(result.ArousalPoints, Is.EqualTo(source.ArousalPoints));
        Assert.That(result.PartnerArousalMultiplier, Is.EqualTo(source.PartnerArousalMultiplier));
        Assert.That(result.TargetEntityId, Is.EqualTo(source.TargetEntityId));
        Assert.That(((SoundPathSpecifier) result.InteractSound!).Path, Is.EqualTo(((SoundPathSpecifier) source.InteractSound).Path));
        using var second = new StringWriter();
        _files.ToDataNode(result).Write(second);
        Assert.That(second.ToString(), Is.EqualTo(writer.ToString()), "All serialized fields round-trip.");
    }

    [Test]
    public void LegacyFlatFilesAndEmptyVersionedPackagesAreSupported()
    {
        Assert.That(Read("- type: interaction\n  id: OldAction\n  name: Wave\n  userMessages: ['waves']\n").Single().ID, Is.EqualTo("OldAction"));
        Assert.That(Read("version: 1\nactions: []\n"), Is.Empty);
    }

    [TestCase("version: 9\nactions: []")]
    [TestCase("- type: entity\n  id: NotAnAction")]
    [TestCase("- id: Duplicate\n  name: Wave\n  userMessages: ['waves']\n- id: Duplicate\n  name: Other\n  userMessages: ['waves']")]
    [TestCase("- id: MissingMessage\n  name: Wave")]
    public void InvalidPackagesFailWithoutPartialResults(string yaml)
        => Assert.Throws<InvalidDataException>(() => Read(yaml));

    [Test]
    public void OversizedInputIsRejectedBeforeParsing()
    {
        using var stream = new MemoryStream(new byte[InteractionPanelManager.MaxFileBytes + 1]);
        Assert.Throws<InvalidDataException>(() => _files.FromStream(stream));
    }

    [Test]
    public void ExpandedCategoriesScrollAndWindowSizeRoundTrip()
    {
        var serialization = IoCManager.Resolve<ISerializationManager>();
        var prefs = new InteractionPanelPreferences { WindowSize = new System.Numerics.Vector2(800, 900), Scroll = 238, Expanded = ["Hands", "Favourites"] };
        using var writer = new StringWriter();
        serialization.WriteValue(prefs, notNullableOverride: true).Write(writer);
        var yaml = new YamlStream();
        using var reader = new StringReader(writer.ToString());
        yaml.Load(reader);
        var restored = serialization.Read<InteractionPanelPreferences>(yaml.Documents[0].RootNode.ToDataNode(), notNullableOverride: true);
        Assert.That(restored.WindowSize, Is.EqualTo(prefs.WindowSize));
        Assert.That(restored.Scroll, Is.EqualTo(prefs.Scroll));
        Assert.That(restored.Expanded, Is.EqualTo(prefs.Expanded));
    }

    private System.Collections.Generic.List<InteractionPrototype> Read(string yaml)
    {
        using var stream = new MemoryStream(EncodingHelpers.UTF8.GetBytes(yaml));
        return _files.FromStream(stream);
    }
}
