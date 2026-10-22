using System;
using System.Collections.Generic;
using System.IO;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Moq;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture, NonParallelizable]
public sealed class HumanoidProfileFileTest
{
    [OneTimeSetUp]
    public void InitializeSerialization()
    {
        var dependencies = IoCManager.InitThread();
        dependencies.Clear();
        dependencies.RegisterInstance<INetManager>(new Mock<INetManager>().Object);
        dependencies.Register<ILogManager, LogManager>();
        dependencies.Register<IReflectionManager, ProfileReflectionManager>();
        dependencies.Register<ISerializationManager, SerializationManager>();
        dependencies.BuildGraph();
        var reflection = dependencies.Resolve<IReflectionManager>();
        reflection.Initialize();
        reflection.LoadAssemblies([typeof(SerializationManager).Assembly, typeof(HumanoidProfileExport).Assembly]);
        IoCManager.Resolve<ISerializationManager>().Initialize();
    }

    [OneTimeTearDown]
    public void TearDown() => IoCManager.Clear();

    private sealed class ProfileReflectionManager : ReflectionManager
    {
        protected override IEnumerable<string> TypePrefixes => ["", "Robust.Shared.", "Content.Shared."];
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProfileExportRoundTrips(bool withCareerIds)
    {
        var serialization = IoCManager.Resolve<ISerializationManager>();
        var profile = new HumanoidCharacterProfile
        {
            Name = "Test character",
            Species = "Demon",
            Voice = "Elenwen",
            FlavorText = "Character description"
        };
        profile.Appearance.Markings.Add(new Marking("TestHorns", 2));
        profile.SetLoadout(new RoleLoadout("Contractor")
        {
            SelectedLoadouts = { ["ContractorHead"] = [new Loadout { Prototype = "TestHat" }] }
        });
        if (withCareerIds)
        {
            profile.Career.CharacterId = Guid.NewGuid();
            profile.Career.BoxId = Guid.NewGuid();
        }
        var source = new HumanoidProfileExport { ForkId = "test", Profile = profile };
        using var writer = new StringWriter();
        serialization.WriteValue(source, alwaysWrite: true, notNullableOverride: true).Write(writer);
        Assert.That(writer.ToString(), Is.Not.Empty);

        var yaml = new YamlStream();
        using var reader = new StringReader(writer.ToString());
        yaml.Load(reader);
        var result = serialization.Read<HumanoidProfileExport>(yaml.Documents[0].RootNode.ToDataNode(),
            notNullableOverride: true);
        Assert.That(result.Profile.Name, Is.EqualTo(profile.Name));
        Assert.That(result.Profile.Species, Is.EqualTo(profile.Species));
        Assert.That(result.Profile.Voice, Is.EqualTo(profile.Voice));
        Assert.That(result.Profile.FlavorText, Is.EqualTo(profile.FlavorText));
        Assert.That(result.Profile.SkillLevels, Is.EqualTo(profile.SkillLevels));
        Assert.That(result.Profile.Appearance.Markings, Is.EqualTo(profile.Appearance.Markings));
        Assert.That(result.Profile.Career.CharacterId, Is.EqualTo(profile.Career.CharacterId));
        Assert.That(result.Profile.Career.BoxId, Is.EqualTo(profile.Career.BoxId));
        Assert.That(result.Profile.Loadouts.Keys, Is.EqualTo(profile.Loadouts.Keys));
        Assert.That(result.Profile.Loadouts["Contractor"].SelectedLoadouts["ContractorHead"][0].Prototype,
            Is.EqualTo(profile.Loadouts["Contractor"].SelectedLoadouts["ContractorHead"][0].Prototype));
    }

    [Test]
    public void OlderCitizenshipLockIsIgnoredWhenImportingAProfile()
    {
        var serialization = IoCManager.Resolve<ISerializationManager>();
        var yaml = new YamlStream();
        using var reader = new StringReader("forkId: test\nprofile:\n  name: Legacy\n  citizenship: Hesperia\n  citizenshipLocked: true\n");
        yaml.Load(reader);
        var result = serialization.Read<HumanoidProfileExport>(yaml.Documents[0].RootNode.ToDataNode(), notNullableOverride: true);
        Assert.That(result.Profile.Citizenship, Is.EqualTo(Content.Shared._radiant.Passports.RadiantCitizenship.Hesperia));
        Assert.That(result.Profile.WithCitizenship(Content.Shared._radiant.Passports.RadiantCitizenship.Zon).Citizenship,
            Is.EqualTo(Content.Shared._radiant.Passports.RadiantCitizenship.Zon));
    }

    [Test]
    public void OlderProfilesWithoutCareerFieldsStillLoad()
    {
        var serialization = IoCManager.Resolve<ISerializationManager>();
        var yaml = new YamlStream();
        using var reader = new StringReader("forkId: test\nversion: 1\nprofile:\n  name: Legacy\n  species: Demon\n");
        yaml.Load(reader);
        var result = serialization.Read<HumanoidProfileExport>(yaml.Documents[0].RootNode.ToDataNode(),
            notNullableOverride: true);
        Assert.That(result.Profile.Name, Is.EqualTo("Legacy"));
        Assert.That(result.Profile.Career.CharacterId, Is.EqualTo(Guid.Empty));
        Assert.That(result.Profile.Career.BoxId, Is.Null);
    }

    [TestCase("")]
    [TestCase(" \n\n")]
    [TestCase("# Empty export")]
    public void EmptyFilesAreRejectedWithoutClosingTheStream(string yaml)
    {
        using var stream = new MemoryStream(EncodingHelpers.UTF8.GetBytes(yaml));
        var error = Assert.Throws<InvalidDataException>(() => new ProfileSystem().FromStream(stream, null!));
        Assert.That(error!.Message, Is.EqualTo("humanoid-profile-editor-import-empty"));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase("not a profile")]
    [TestCase("[]")]
    [TestCase("version: 1")]
    [TestCase("profile: []")]
    [TestCase("profile: {}\n---\nprofile: {}")]
    public void InvalidDocumentsAreRejected(string yaml)
    {
        using var stream = new MemoryStream(EncodingHelpers.UTF8.GetBytes(yaml));
        var error = Assert.Throws<InvalidDataException>(() => new ProfileSystem().FromStream(stream, null!));
        Assert.That(error!.Message, Is.EqualTo("humanoid-profile-editor-import-invalid"));
        Assert.That(stream.CanRead, Is.True);
    }

    private sealed class ProfileSystem : SharedHumanoidAppearanceSystem;
}
