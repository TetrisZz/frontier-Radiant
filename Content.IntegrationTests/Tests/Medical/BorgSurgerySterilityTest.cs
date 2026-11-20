using System.Linq;
using Content.Server._Starlight.Medical.Surgery;
using Content.Server.Silicons.Borgs;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class BorgSurgerySterilityTest
{
    [Test]
    public async Task ModuleSterilizesItsToolsButKeepsWoundAndPuddleRisk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var surgery = em.System<SurgerySystem>();
                var hands = em.System<SharedHandsSystem>();
                var surgeon = em.SpawnEntity("BorgChassisMedical", map.GridCoords);
                var chassis = em.GetComponent<BorgChassisComponent>(surgeon);
                var module = chassis.ModuleContainer.ContainedEntities.Single(uid =>
                    em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID == "BorgModuleSurgery");
                // A bare standard chassis has no battery; install this module directly for the operation test.
                em.System<BorgSystem>().InstallModule(surgeon, module, chassis);
                EntityUid scalpel = default;
                var toolCount = 0;
                foreach (var hand in em.GetComponent<HandsComponent>(surgeon).Hands.Keys)
                {
                    hands.SetActiveHand(surgeon, hand);
                    if (!hands.TryGetActiveItem(surgeon, out var tool))
                        continue;
                    toolCount++;
                    var state = em.GetComponent<SurgicalItemSterilityComponent>(tool.Value);
                    Assert.That(state.SelfSterilizing, Is.True);
                    Assert.That(surgery.CanDisinfectItem(tool.Value), Is.False);
                    var id = em.GetComponent<MetaDataComponent>(tool.Value).EntityPrototype!.ID;
                    var ordinary = em.SpawnEntity(id.Replace("BorgSurgery", "Cyber"), map.GridCoords);
                    Assert.That(em.GetComponent<SurgicalItemSterilityComponent>(ordinary).SelfSterilizing, Is.False,
                        "Human cyberarm instruments must retain their usual disinfection rules.");
                    em.DeleteEntity(ordinary);
                    if (id == "ScalpelBorgSurgery")
                        scalpel = tool.Value;
                }
                Assert.That(toolCount, Is.EqualTo(8));
                Assert.That(scalpel, Is.Not.EqualTo(default(EntityUid)));
                foreach (var hand in em.GetComponent<HandsComponent>(surgeon).Hands.Keys)
                {
                    hands.SetActiveHand(surgeon, hand);
                    if (hands.TryGetActiveItem(surgeon, out var tool) && tool == scalpel)
                        break;
                }
                var instrument = em.GetComponent<SurgicalItemSterilityComponent>(scalpel);
                var table = em.SpawnEntity("OperatingTable", map.GridCoords);
                EntityUid? puddle = null;
                foreach (var withPuddle in new[] { false, true })
                {
                    if (withPuddle)
                        puddle = em.SpawnEntity("PuddleBlood", map.GridCoords);
                    var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                    em.System<StandingStateSystem>().Down(patient);
                    Assert.That(em.System<SharedBuckleSystem>().TryBuckle(patient, surgeon, table), Is.True);
                    var head = em.System<SharedBodySystem>().GetBodyChildren(patient)
                        .Single(part => part.Component.PartType == BodyPartType.Head).Id;
                    var wound = em.EnsureComponent<SurgicalSterilityComponent>(head);
                    wound.Contamination[SurgicalSite.Surface] = 30;
                    instrument.Dirty = true;
                    instrument.Used = true;
                    instrument.LastPatient = surgeon;
                    instrument.ContainerContamination = 15;
                    var finish = new SurgeryDoAfterEvent("SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", 1);
                    finish.DoAfter = new Content.Shared.DoAfter.DoAfter(0,
                        new DoAfterArgs(em, surgeon, 1, finish, patient, head) { Used = scalpel }, TimeSpan.Zero);
                    em.EventBus.RaiseLocalEvent(patient, finish);
                    Assert.That(em.HasComponent<IncisionOpenComponent>(head), Is.True, "The surgery step must actually run.");
                    Assert.That(wound.Contamination[SurgicalSite.Surface], Is.EqualTo(withPuddle ? 33 : 30),
                        "Existing wound dirt stays; only environmental puddle risk is added.");
                    Assert.That(instrument.Dirty, Is.False);
                    Assert.That(instrument.Used, Is.False);
                    em.DeleteEntity(patient);
                }
                if (puddle != null)
                    em.DeleteEntity(puddle.Value);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
