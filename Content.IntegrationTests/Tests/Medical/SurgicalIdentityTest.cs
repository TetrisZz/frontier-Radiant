using System.Linq;
using Content.Server._radiant.Medical.Surgery;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._radiant;
using Content.Shared._radiant.ERP;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Corvax.TTS;
using Content.Shared.DetailExaminable;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Standing;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgicalIdentityTest
{
    [Test]
    public async Task IdentityConsentOrgansAndImprovisedQuality()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var patient = entities.SpawnEntity("MobHuman", coordinates);
            var surgeon = entities.SpawnEntity("MobHuman", coordinates);
            var body = entities.System<SharedBodySystem>();
            var surgery = entities.System<SurgerySystem>();
            var torso = body.GetBodyChildren(patient).Single(p => p.Component.PartType == BodyPartType.Torso).Id;
            var anatomy = entities.GetComponent<AdultAnatomyComponent>(patient);
            var appearance = entities.GetComponent<HumanoidAppearanceComponent>(patient);
            entities.System<SharedHumanoidAppearanceSystem>().SetSex(patient, Sex.Male);
            entities.System<StandingStateSystem>().Down(patient);
            void OpenCavity(string name)
            {
                var stepId = "SurgeryStepOpen" + name + "State";
                var step = entities.SpawnEntity(stepId, coordinates);
                var open = new SurgeryStepEvent(surgeon, patient, torso, new())
                { StepProto = stepId, SurgeryProto = "SurgeryOpen" + name };
                entities.EventBus.RaiseLocalEvent(step, ref open);
            }
            OpenCavity("Groin");
            Assert.That(surgery.HasSexSurgeryAccess(patient, torso), Is.False);
            OpenCavity("Ribcage");
            Assert.That(surgery.HasSexSurgeryAccess(patient, torso), Is.True);

            var operation = entities.SpawnEntity("SurgeryChangeSexFemale", coordinates);
            var valid = new SurgeryValidEvent(patient, torso);
            entities.EventBus.RaiseLocalEvent(operation, ref valid);
            Assert.That(valid.Cancelled, Is.True, "Existing organs must be extracted first.");
            anatomy.HasPenis = anatomy.HasVagina = anatomy.HasBreasts = false;
            valid = new SurgeryValidEvent(patient, torso);
            entities.EventBus.RaiseLocalEvent(operation, ref valid);
            Assert.That(valid.Cancelled, Is.False);

            var finalStep = entities.SpawnEntity("SurgeryStepFinalizeSexFemale", coordinates);
            var finalize = new SurgeryStepEvent(surgeon, patient, torso, new())
            { StepProto = "SurgeryStepFinalizeSexFemale", SurgeryProto = "SurgeryChangeSexFemale" };
            entities.EventBus.RaiseLocalEvent(finalStep, ref finalize);
            Assert.That(finalize.IsCancelled, Is.True, "Cannot change sex without installed donor organs.");
            Assert.That(appearance.Sex, Is.EqualTo(Sex.Male));

            // Real donor steps consume their items, preserving donated breast size.
            foreach (var organId in new[] { "Vagina", "Breasts" })
            {
                var donor = entities.SpawnEntity("AdultOrgan" + organId, coordinates);
                var implantStep = entities.SpawnEntity("SurgeryStepInsert" + organId, coordinates);
                Assert.That(surgery.GetSurgicalSite(operation, implantStep),
                    Is.EqualTo(organId == "Breasts" ? SurgicalSite.Ribcage : SurgicalSite.Groin));
                var insert = new SurgeryStepEvent(surgeon, patient, torso, new() { donor })
                { StepProto = "SurgeryStepInsert" + organId, SurgeryProto = "SurgeryChangeSexFemale" };
                entities.EventBus.RaiseLocalEvent(implantStep, ref insert);
                Assert.That(insert.IsCancelled, Is.False);
                Assert.That(entities.IsQueuedForDeletion(donor), Is.True);
            }
            var cautery = entities.SpawnEntity("Cautery", coordinates);
            var consent = entities.EnsureComponent<DetailExaminableComponent>(surgeon);
            consent.ERPStatus = EnumERPStatus.NO;
            finalize = new SurgeryStepEvent(surgeon, patient, torso, new() { cautery })
            { StepProto = "SurgeryStepFinalizeSexFemale", SurgeryProto = "SurgeryChangeSexFemale" };
            entities.EventBus.RaiseLocalEvent(finalStep, ref finalize);
            Assert.That(finalize.IsCancelled, Is.True);
            Assert.That(appearance.Sex, Is.EqualTo(Sex.Male));
            consent.ERPStatus = EnumERPStatus.FULL;
            finalize.IsCancelled = false;
            entities.EventBus.RaiseLocalEvent(finalStep, ref finalize);
            Assert.That(finalize.IsCancelled, Is.False);
            Assert.That(appearance.Sex, Is.EqualTo(Sex.Female));
            Assert.That(appearance.Gender, Is.EqualTo(Gender.Female));
            Assert.That(anatomy.HasVagina && anatomy.HasBreasts && !anatomy.HasPenis, Is.True);
            Assert.That(entities.HasComponent<SurgicalVoicePermissionComponent>(patient), Is.True);

            var voices = entities.System<SurgicalVoiceSystem>();
            foreach (var voice in server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<TTSVoicePrototype>())
                Assert.That(voices.IsVoiceAllowed(voice.ID, Sex.Female),
                    Is.EqualTo(voice.RoundStart && voice.Sex is Sex.Female or Sex.Unsexed));
            Assert.That(voices.IsVoiceAllowed("missing-voice", Sex.Female), Is.False);

            var availableVoices = server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<TTSVoicePrototype>().ToArray();
            var femaleVoice = availableVoices.First(v => v.RoundStart && v.Sex == Sex.Female);
            var maleVoice = availableVoices.First(v => v.RoundStart && v.Sex == Sex.Male);
            var beforeVoice = appearance.Voice;
            entities.EventBus.RaiseLocalEvent(patient, new SurgicalVoiceMessage(femaleVoice.ID) { Actor = patient });
            Assert.That(appearance.Voice, Is.EqualTo(beforeVoice), "Another actor cannot use the surgeon's permission.");
            entities.EventBus.RaiseLocalEvent(patient, new SurgicalVoiceMessage(maleVoice.ID) { Actor = surgeon });
            Assert.That(appearance.Voice, Is.EqualTo(beforeVoice), "The server rejects a voice of the wrong sex.");
            entities.EventBus.RaiseLocalEvent(patient, new SurgicalVoiceMessage(femaleVoice.ID) { Actor = surgeon });
            Assert.That(appearance.Voice.Id, Is.EqualTo(femaleVoice.ID));
            Assert.That(entities.GetComponent<TTSComponent>(patient).VoicePrototypeId, Is.EqualTo(femaleVoice.ID));
            Assert.That(entities.HasComponent<SurgicalVoicePermissionComponent>(patient), Is.False);

            // The reverse direction uses a donated organ too; extraction preserves existing tissue.
            foreach (var organId in new[] { "Vagina", "Breasts" })
            {
                var extractStep = entities.SpawnEntity("SurgeryStepExtract" + organId, coordinates);
                var extractionTool = entities.SpawnEntity("Scalpel", coordinates);
                var extract = new SurgeryStepEvent(surgeon, patient, torso, new() { extractionTool })
                { StepProto = "SurgeryStepExtract" + organId, SurgeryProto = "SurgeryExtract" + organId };
                entities.EventBus.RaiseLocalEvent(extractStep, ref extract);
                Assert.That(extract.IsCancelled, Is.False);
            }
            var penis = entities.SpawnEntity("AdultOrganPenis", coordinates);
            var insertPenisStep = entities.SpawnEntity("SurgeryStepInsertPenis", coordinates);
            var insertPenis = new SurgeryStepEvent(surgeon, patient, torso, new() { penis })
            { StepProto = "SurgeryStepInsertPenis", SurgeryProto = "SurgeryChangeSexMale" };
            entities.EventBus.RaiseLocalEvent(insertPenisStep, ref insertPenis);
            Assert.That(insertPenis.IsCancelled, Is.False);
            Assert.That(entities.IsQueuedForDeletion(penis), Is.True);
            var maleFinalStep = entities.SpawnEntity("SurgeryStepFinalizeSexMale", coordinates);
            var maleFinalize = new SurgeryStepEvent(surgeon, patient, torso, new() { cautery })
            { StepProto = "SurgeryStepFinalizeSexMale", SurgeryProto = "SurgeryChangeSexMale" };
            var patientConsent = entities.EnsureComponent<DetailExaminableComponent>(patient);
            patientConsent.ERPStatus = EnumERPStatus.NO;
            entities.EventBus.RaiseLocalEvent(maleFinalStep, ref maleFinalize);
            Assert.That(maleFinalize.IsCancelled, Is.True);
            Assert.That(appearance.Sex, Is.EqualTo(Sex.Female));
            patientConsent.ERPStatus = EnumERPStatus.FULL;
            maleFinalize.IsCancelled = false;
            entities.EventBus.RaiseLocalEvent(maleFinalStep, ref maleFinalize);
            Assert.That(maleFinalize.IsCancelled, Is.False);
            Assert.That(appearance.Sex, Is.EqualTo(Sex.Male));
            Assert.That(appearance.Gender, Is.EqualTo(Gender.Male));
            Assert.That(anatomy.HasPenis && !anatomy.HasVagina && !anatomy.HasBreasts, Is.True);

            var scalpelStep = entities.SpawnEntity("SurgeryStepChangeVoice", coordinates);
            foreach (var (id, chance) in new[] { ("Scalpel", 1f), ("ShardGlass", .7f), ("Wirecutter", .6f) })
            {
                var tool = entities.SpawnEntity(id, coordinates);
                Assert.That(surgery.GetStepSuccessRate(scalpelStep, new[] { tool }), Is.EqualTo(chance).Within(.001));
            }
            var clamp = entities.SpawnEntity("SurgeryStepAdultClamp", coordinates);
            var retract = entities.SpawnEntity("SurgeryStepRetractSkin", coordinates);
            var crowbar = entities.SpawnEntity("Crowbar", coordinates);
            Assert.That(surgery.GetStepSuccessRate(clamp, new[] { crowbar }), Is.EqualTo(.8f).Within(.001));
            Assert.That(surgery.GetStepSuccessRate(retract, new[] { crowbar }), Is.EqualTo(.9f).Within(.001));

            // Exercise the actual DoAfter completion path with a deterministic failed roll.
            var incisionPatient = entities.SpawnEntity("MobHuman", coordinates);
            entities.System<StandingStateSystem>().Down(incisionPatient);
            var head = body.GetBodyChildren(incisionPatient).Single(p => p.Component.PartType == BodyPartType.Head).Id;
            var scalpel = entities.SpawnEntity("Scalpel", coordinates);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(surgeon, scalpel), Is.True);
            void FinishIncision(float chance)
            {
                var finish = new SurgeryDoAfterEvent("SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", chance);
                finish.DoAfter = new Content.Shared.DoAfter.DoAfter(0,
                    new DoAfterArgs(entities, surgeon, 1, finish, incisionPatient, head) { Used = scalpel }, TimeSpan.Zero);
                entities.EventBus.RaiseLocalEvent(incisionPatient, finish);
            }
            FinishIncision(0);
            Assert.That(entities.GetComponent<SurgeryProgressComponent>(head).CompletedSteps, Is.Empty);
            Assert.That(entities.HasComponent<IncisionOpenComponent>(head), Is.False);
            Assert.That(entities.GetComponent<BloodstreamComponent>(incisionPatient).BleedAmount > 0
                || entities.GetComponent<DamageableComponent>(incisionPatient).TotalDamage > 0
                || entities.TryGetComponent<DamageableComponent>(head, out var headDamage) && headDamage.TotalDamage > 0, Is.True);
            FinishIncision(1);
            Assert.That(entities.GetComponent<SurgeryProgressComponent>(head).CompletedSteps,
                Does.Contain("SurgeryOpenIncision:SurgeryStepOpenIncisionScalpel"));
        });
    }
}
