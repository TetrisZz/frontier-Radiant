using System.Linq;
using Content.Shared._radiant.Skills;
using Content.Server.Body.Components;
using Content.Server._radiant.Medical.Surgery;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.DetailExaminable;
using Content.Shared._NF.Bank.Components;
using Content.Shared._radiant;
using Content.Shared.Humanoid;
using Content.Shared.Corvax.TTS;
using Robust.Shared.Maths;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class BrainRestorationTest
{
    [TestCase(EnumERPStatus.NO, "Original description")]
    [TestCase(EnumERPStatus.HALF, "Another description")]
    [TestCase(EnumERPStatus.FULL, "")]
    public async Task TransplantRestoresAppearanceEyesAndExactOriginalVoiceInSeparateStages(EnumERPStatus status, string description)
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var pos = new EntityCoordinates(map, 0, 0);
            var donor = em.SpawnEntity("MobHuman", pos);
            var receiver = em.SpawnEntity("MobHuman", pos);
            var body = em.System<SharedBodySystem>();
            var restoration = em.System<BrainRestorationSystem>();
            var checks = em.System<SharedBrainRestorationSystem>();
            var appearance = em.System<SharedHumanoidAppearanceSystem>();
            var oldLook = em.GetComponent<HumanoidAppearanceComponent>(donor);
            var newLook = em.GetComponent<HumanoidAppearanceComponent>(receiver);
            appearance.SetSex(donor, Sex.Female);
            var voices = server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<TTSVoicePrototype>()
                .Take(2).ToArray();
            Assert.That(voices.Length, Is.EqualTo(2));
            oldLook.Voice = voices[0].ID;
            oldLook.EyeColor = Color.Blue;
            newLook.Voice = voices[1].ID;
            newLook.EyeColor = Color.Brown;
            em.System<MetaDataSystem>().SetEntityName(donor, "Original Patient");
            var donorDetail = em.EnsureComponent<DetailExaminableComponent>(donor);
            em.EnsureComponent<BankAccountComponent>(donor);
            var donorSkills = em.EnsureComponent<ProfessionalSkillsComponent>(donor);
            donorSkills.Levels = new[] { 2, 1, 4, 1, 0, 0, 0, 2 };
            var expectedSkills = donorSkills.Levels.ToArray();
            em.EnsureComponent<ProfessionalSkillsComponent>(receiver).Levels = new int[8];
            em.RemoveComponent<BankAccountComponent>(receiver);
            donorDetail.Content = description;
            donorDetail.ERPStatus = status;
            var recipientDetail = em.EnsureComponent<DetailExaminableComponent>(receiver);
            recipientDetail.Content = "Recipient description";
            recipientDetail.ERPStatus = status == EnumERPStatus.NO ? EnumERPStatus.FULL : EnumERPStatus.NO;
            var dna = em.GetComponent<DnaComponent>(donor).DNA;
            var fingerprints = em.GetComponent<FingerprintComponent>(donor).Fingerprint;
            var brain = body.GetBodyChildren(donor).SelectMany(p => body.GetPartOrgans(p.Id))
                .First(o => em.HasComponent<BrainComponent>(o.Id)).Id;
            Assert.That(body.RemoveOrgan(brain), Is.True);
            Assert.That(em.HasComponent<BrainIdentityMemoryComponent>(brain), Is.True);
            var receiverHead = body.GetBodyChildren(receiver).First(p => p.Component.PartType == BodyPartType.Head).Id;
            var spareBrain = body.GetPartOrgans(receiverHead).First(o => em.HasComponent<BrainComponent>(o.Id)).Id;
            Assert.That(body.RemoveOrgan(spareBrain), Is.True);
            Assert.That(body.InsertOrgan(receiverHead, brain, "brain"), Is.True);
            var recipientSkills = em.GetComponent<ProfessionalSkillsComponent>(receiver);
            Assert.That(recipientSkills.Levels, Is.EqualTo(expectedSkills));
            Assert.That(recipientSkills.Levels, Is.Not.SameAs(donorSkills.Levels));
            Assert.That(em.HasComponent<BankAccountComponent>(receiver), Is.True);
            // Surgery must not undo training acquired after transplantation.
            recipientSkills.Levels[0] = 3;
            appearance.SetSex(receiver, Sex.Male);
            Assert.That(checks.Failure(receiver), Is.EqualTo("restoration-wrong-sex"));
            Assert.That(restoration.Restore(receiver), Is.False);
            Assert.That(restoration.Restore(receiver, IdentityRestorationStage.Eyes), Is.False);
            Assert.That(restoration.Restore(receiver, IdentityRestorationStage.Voice), Is.False);
            Assert.That(recipientDetail.Content, Is.EqualTo("Recipient description"));
            appearance.SetSex(receiver, Sex.Female);
            newLook.Species = "Reptilian";
            Assert.That(checks.Failure(receiver), Is.EqualTo("restoration-wrong-species"));
            newLook.Species = "Human";
            var eyes = newLook.EyeColor;
            var voice = newLook.Voice;
            Assert.That(restoration.Restore(receiver), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(receiver).EntityName, Is.EqualTo("Original Patient"));
            Assert.That(recipientDetail.Content, Is.EqualTo(description));
            Assert.That(recipientSkills.Levels[0], Is.EqualTo(3));
            Assert.That(em.HasComponent<BankAccountComponent>(receiver), Is.True);
            Assert.That(recipientDetail.ERPStatus, Is.EqualTo(status));
            Assert.That(em.GetComponent<DnaComponent>(receiver).DNA, Is.EqualTo(dna));
            Assert.That(em.GetComponent<FingerprintComponent>(receiver).Fingerprint, Is.EqualTo(fingerprints));
            Assert.That(newLook.EyeColor, Is.EqualTo(eyes));
            Assert.That(newLook.Voice, Is.EqualTo(voice));
            Assert.That(restoration.OriginalVoice(receiver), Is.EqualTo(oldLook.Voice.Id));
            Assert.That(restoration.Restore(receiver, IdentityRestorationStage.Eyes), Is.True);
            Assert.That(newLook.EyeColor, Is.EqualTo(oldLook.EyeColor));
            Assert.That(newLook.Voice, Is.EqualTo(voice), "The eye stage must not change the voice.");
            Assert.That(restoration.Restore(receiver, IdentityRestorationStage.Voice), Is.True);
            Assert.That(newLook.Voice, Is.EqualTo(oldLook.Voice));
            Assert.That(em.GetComponent<TTSComponent>(receiver).VoicePrototypeId, Is.EqualTo(oldLook.Voice.Id));
            Assert.That(body.RemoveOrgan(brain), Is.True);
            Assert.That(em.GetComponent<BrainIdentityMemoryComponent>(brain).SkillLevels, Is.EqualTo(recipientSkills.Levels));
            Assert.That(em.GetComponent<ProfessionalSkillsComponent>(brain).Levels, Is.EqualTo(recipientSkills.Levels));
            Assert.That(em.GetComponent<BrainIdentityMemoryComponent>(brain).OriginalName, Is.EqualTo("Original Patient"));
            Assert.That(em.GetComponent<BrainIdentityMemoryComponent>(brain).Description, Is.EqualTo(description));
            Assert.That(em.GetComponent<BrainIdentityMemoryComponent>(brain).HadBankAccount, Is.True);
            Assert.That(em.GetComponent<BrainIdentityMemoryComponent>(brain).ErpStatus, Is.EqualTo(status));
        });
    }

    [Test]
    public async Task BoxContainsTwoCubesOfEveryLobbySpecies()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var pos = new EntityCoordinates(map, 0, 0);
            var box = em.SpawnEntity("RestorationBodyCubeBox", pos);
            var items = em.GetComponent<StorageComponent>(box).Container.ContainedEntities.ToList();
            var species = prototypes.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).ToList();
            Assert.That(items.Count, Is.EqualTo(species.Count * 2));
            foreach (var race in species)
            {
                var cubes = items.Where(i => em.GetComponent<RestorationBodyCubeComponent>(i).Species == race.ID).ToList();
                Assert.That(cubes.Count, Is.EqualTo(2), race.ID);
                foreach (var cube in cubes)
                {
                    var data = em.GetComponent<RestorationBodyCubeComponent>(cube);
                    Assert.That(data.Sex, Is.EqualTo(Sex.Male).Or.EqualTo(Sex.Female));
                    var before = em.AllEntities<HumanoidAppearanceComponent>().Select(e => e.Owner).ToHashSet();
                    var solutions = em.System<SharedSolutionContainerSystem>();
                    Assert.That(solutions.TryGetSolution(cube, "cube", out var solution), Is.True);
                    Assert.That(solutions.TryAddReagent(solution!.Value, "Water", FixedPoint2.New(1)), Is.True);
                    var hydrated = em.AllEntities<HumanoidAppearanceComponent>().Single(e => !before.Contains(e.Owner)).Owner;
                    var look = em.GetComponent<HumanoidAppearanceComponent>(hydrated);
                    Assert.That(look.Species.Id, Is.EqualTo(race.ID));
                    Assert.That(look.Sex, Is.EqualTo(data.Sex));
                    var body = em.System<SharedBodySystem>();
                    Assert.That(body.GetBodyChildren(hydrated).SelectMany(p => body.GetPartOrgans(p.Id))
                        .Any(o => em.HasComponent<BrainComponent>(o.Id)), Is.True, race.ID + " needs a mind-carrying organ");
                    Assert.That(em.GetComponent<MetaDataComponent>(hydrated).EntityName, Is.EqualTo("Urist").Or.EqualTo("Урист"));
                }
                Assert.That(cubes.Select(i => em.GetComponent<RestorationBodyCubeComponent>(i).Sex),
                    Is.EquivalentTo(new[] { Sex.Male, Sex.Female }));
            }
        });
    }
}
