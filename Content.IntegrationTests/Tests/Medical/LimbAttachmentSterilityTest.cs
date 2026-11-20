using System.Linq;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class LimbAttachmentSterilityTest
{
    [Test]
    public async Task AttachedLimbsLeaveTheirSocketWashableWithoutOpeningAnotherCavity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var body = em.System<SharedBodySystem>();
                var surgery = em.System<SurgerySystem>();
                var singletons = em.System<StarlightEntitySystem>();
                var containers = em.System<SharedContainerSystem>();
                var hands = em.System<SharedHandsSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();

                foreach (var (partType, symmetry, operation, attachmentStep) in new[]
                {
                    (BodyPartType.Arm, BodyPartSymmetry.Left, "SurgeryLimbAttachmentLeftArm", "SurgeryLimbAttachmentStepLeftArm"),
                    (BodyPartType.Arm, BodyPartSymmetry.Right, "SurgeryLimbAttachmentRightArm", "SurgeryLimbAttachmentStepRightArm"),
                    (BodyPartType.Hand, BodyPartSymmetry.Left, "SurgeryLimbAttachmentLeftHand", "SurgeryLimbAttachmentStep"),
                    (BodyPartType.Foot, BodyPartSymmetry.Left, "SurgeryLimbAttachmentLeftFoot", "SurgeryLimbAttachmentStep"),
                })
                {
                    var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                    var surgeon = em.SpawnEntity("MobHuman", map.GridCoords);
                    em.System<StandingStateSystem>().Down(patient);
                    var limb = body.GetBodyChildren(patient).Single(p => p.Component.PartType == partType && p.Component.Symmetry == symmetry).Id;
                    Assert.That(containers.TryGetContainingContainer(limb, out var socket), Is.True);
                    var parent = socket!.Owner;
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Surface), Is.False);
                    var wound = em.EnsureComponent<SurgicalSterilityComponent>(parent);
                    wound.Contamination[SurgicalSite.Surface] = 50;
                    wound.Infection[SurgicalSite.Surface] = 50;
                    Assert.That(surgery.IsSurgeryValid(patient, parent, "SurgeryLavageSurface", "SurgeryStepLavage", out _, out _, out _), Is.False,
                        "Closed unrelated tissue still requires an incision before irrigation.");

                    // Reject an occupied socket without opening a wound or granting completed incision progress.
                    var replacement = em.SpawnEntity(em.GetComponent<MetaDataComponent>(limb).EntityPrototype!.ID, map.GridCoords);
                    Assert.That(singletons.TryGetSingleton(attachmentStep, out var step), Is.True);
                    var rejected = new SurgeryStepEvent(surgeon, patient, parent, new() { replacement })
                        { SurgeryProto = operation, StepProto = attachmentStep };
                    em.EventBus.RaiseLocalEvent(step, ref rejected);
                    Assert.That(rejected.IsCancelled, Is.True);
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Surface), Is.False);
                    em.DeleteEntity(replacement);

                    Assert.That(containers.Remove(limb, socket), Is.True);
                    Assert.That(hands.TryPickupAnyHand(surgeon, limb), Is.True);
                    var attach = new SurgeryStepEvent(surgeon, patient, parent, new() { limb })
                        { SurgeryProto = operation, StepProto = attachmentStep };
                    em.EventBus.RaiseLocalEvent(step, ref attach);
                    Assert.That(attach.IsCancelled, Is.False, operation);
                    Assert.That(em.GetComponent<BodyPartComponent>(limb).Body, Is.EqualTo(patient));
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Surface), Is.True, operation);
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Ribcage), Is.False);
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Abdomen), Is.False);
                    Assert.That(surgery.IsSurgicalSiteOpen(parent, SurgicalSite.Groin), Is.False);
                    Assert.That(surgery.IsSurgeryValid(patient, parent, "SurgeryLavageSurface", "SurgeryStepLavage", out _, out _, out _), Is.True);
                    var progress = em.GetComponent<SurgeryProgressComponent>(parent);
                    Assert.That(surgery.IsSurgeryRequirementSatisfied(parent, progress, "SurgeryOpenIncision"), Is.True,
                        "The socket must also support normal incision closure.");

                    var bottle = em.SpawnEntity("SurgicalLavageBottle", map.GridCoords);
                    Assert.That(hands.TryPickupAnyHand(surgeon, bottle), Is.True);
                    Assert.That(solutions.TryGetDrainableSolution(bottle, out _, out var saline), Is.True);
                    var before = saline!.Volume;
                    var finish = new SurgeryDoAfterEvent("SurgeryLavageSurface", "SurgeryStepLavage", 1);
                    finish.DoAfter = new Content.Shared.DoAfter.DoAfter(0,
                        new DoAfterArgs(em, surgeon, 1, finish, patient, parent) { Used = bottle }, TimeSpan.Zero);
                    em.EventBus.RaiseLocalEvent(patient, finish);
                    Assert.That(wound.Contamination[SurgicalSite.Surface], Is.Zero, operation);
                    Assert.That(wound.Infection[SurgicalSite.Surface], Is.EqualTo(20), operation);
                    Assert.That(saline.Volume, Is.EqualTo(before - 10), "Irrigation must consume its normal saline dose.");
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
