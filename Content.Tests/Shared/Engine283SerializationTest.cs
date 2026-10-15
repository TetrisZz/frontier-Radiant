using System;
using System.Collections.Generic;
using System.IO;
using Content.Client.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DeviceLinking;
using Content.Shared.Guidebook;
using Content.Shared.StationRecords;
using Content.Shared.StatusIcon;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class Engine283SerializationTest : ContentUnitTest
{
    private ISerializationManager _serialization;

    [OneTimeSetUp]
    public void InitializeSerialization()
    {
        _serialization = IoCManager.Resolve<ISerializationManager>();
        _serialization.Initialize();
    }

    [Test]
    public void DevicePortsKeepInheritedFieldsAndDefaultLinks()
    {
        var sink = Read<SinkPortPrototype>("""
            id: TestSink
            name: test-sink-name
            description: test-sink-description
            """);
        var source = Read<SourcePortPrototype>("""
            id: TestSource
            name: test-source-name
            description: test-source-description
            defaultLinks: [TestSink]
            """);

        Assert.That(sink.ID, Is.EqualTo("TestSink"));
        Assert.That(sink.Name, Is.EqualTo("test-sink-name"));
        Assert.That(sink.Description, Is.EqualTo("test-sink-description"));
        Assert.That(source.ID, Is.EqualTo("TestSource"));
        Assert.That(source.Name, Is.EqualTo("test-source-name"));
        Assert.That(source.Description, Is.EqualTo("test-source-description"));
        Assert.That(source.DefaultLinks, Is.EquivalentTo(new[] { "TestSink" }));
    }

    [Test]
    public void GuidePrototypeKeepsInheritedFieldsWhenCopied()
    {
        var guide = Read<GuideEntryPrototype>("""
            id: TestGuide
            name: test-guide-name
            text: /ServerInfo/Test.xml
            children: [TestChild]
            filterEnabled: true
            ruleEntry: true
            priority: 17
            """);
        var copy = _serialization.CreateCopy(guide);

        Assert.That(copy, Is.Not.SameAs(guide));
        Assert.That(copy.ID, Is.EqualTo("TestGuide"));
        Assert.That(copy.Name, Is.EqualTo("test-guide-name"));
        Assert.That(copy.Text, Is.EqualTo(new ResPath("/ServerInfo/Test.xml")));
        Assert.That(copy.Children, Is.EqualTo(guide.Children));
        Assert.That(copy.Children, Is.Not.SameAs(guide.Children));
        Assert.That(copy.FilterEnabled, Is.True);
        Assert.That(copy.RuleEntry, Is.True);
        Assert.That(copy.Priority, Is.EqualTo(17));
    }

    [Test]
    public void JobIconKeepsFieldsAcrossBothBaseClasses()
    {
        var icon = Read<JobIconPrototype>("""
            id: TestIcon
            icon: /Textures/Interface/Test.png
            priority: 23
            visibleToGhosts: false
            jobName: test-job-name
            allowSelection: false
            """);
        var copy = _serialization.CreateCopy(icon);

        Assert.That(copy.ID, Is.EqualTo("TestIcon"));
        Assert.That(copy.Icon, Is.EqualTo(icon.Icon));
        Assert.That(copy.Priority, Is.EqualTo(23));
        Assert.That(copy.VisibleToGhosts, Is.False);
        Assert.That(copy.JobName, Is.EqualTo("test-job-name"));
        Assert.That(copy.AllowSelection, Is.False);
    }

    [Test]
    public void StationRecordRoundTripPreservesEveryField()
    {
        var record = new GeneralStationRecord
        {
            Name = "Test Person",
            Age = 58,
            JobTitle = "Doctor",
            JobIcon = "JobIconMedicalDoctor",
            JobPrototype = "MedicalDoctor",
            Species = "Human",
            Gender = Gender.Female,
            DisplayPriority = 12,
            Fingerprint = "fingerprint",
            DNA = "dna",
            RegisteredShuttle = "Test Shuttle",
        };
        var copy = _serialization.CreateCopy(record);
        var restored = _serialization.Read<GeneralStationRecord>(_serialization.WriteValue(record));

        Assert.That(copy, Is.Not.SameAs(record));
        Assert.That(copy, Is.EqualTo(record));
        Assert.That(restored, Is.EqualTo(record));
    }

    [Test]
    public void ReadonlyStationKeyKeepsBothFieldsWhenCopied()
    {
        var key = new StationRecordKey(17, new EntityUid(42));
        var copy = _serialization.CreateCopy(key);

        Assert.That(copy, Is.EqualTo(key));
        Assert.That(copy.Id, Is.EqualTo(17));
        Assert.That(copy.OriginStation, Is.EqualTo(new EntityUid(42)));
    }

    [Test]
    public void MedicalRegistryCopiesNestedRecipesAndPrivateOutputCount()
    {
        var recipe = Read<MedicalRecipeData>("""
            recipe: TestRecipe
            result: TestMedicine
            _outputCount: 7
            """);
        var entry = new MedicalGuideEntry("TestMedicine", "test-entry", new[] { recipe },
            Array.Empty<ReagentQuantity>(), null);
        var update = new MedicalGuideRegistryChangedEvent(new List<MedicalGuideEntry> { entry });
        var copy = _serialization.CreateCopy(update);

        Assert.That(copy, Is.Not.SameAs(update));
        Assert.That(copy.Changeset, Is.Not.SameAs(update.Changeset));
        Assert.That(copy.Changeset, Has.Count.EqualTo(1));
        Assert.That(copy.Changeset[0].Identifier, Is.EqualTo("test-entry"));
        var copiedRecipe = copy.Changeset[0].Recipes[0];
        Assert.That(copiedRecipe, Is.Not.SameAs(recipe));
        Assert.That(copiedRecipe.Recipe, Is.EqualTo(recipe.Recipe));
        Assert.That(copiedRecipe.Result, Is.EqualTo(recipe.Result));
        Assert.That(copiedRecipe.OutputCount, Is.EqualTo(7));
    }

    private T Read<T>(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return _serialization.Read<T>(new MappingDataNode((YamlMappingNode) stream.Documents[0].RootNode));
    }
}
