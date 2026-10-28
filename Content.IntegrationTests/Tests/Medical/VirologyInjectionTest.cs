using Content.Server.Chemistry.EntitySystems;
using Content.Server.Hands.Systems;
using Content.Server._radiant.Medical.Virology;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class VirologyInjectionTest
{
    [TestCase("Hematic")]
    [TestCase("Hematic~Fatigue")]
    public async Task DrawnInfectiousBloodTransmitsThroughSyringesAndHypospraysButVaccinesProtect(string strain)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid doctor = default;
            EntityUid donor = default;
            EntityUid recipient = default;
            EntityUid syringe = default;
            EntityUid device = default;
            await pair.Server.WaitAssertion(() =>
            {
                doctor = Patient();
                var skills = em.AddComponent<ProfessionalSkillsComponent>(doctor);
                skills.Levels[(int) ProfessionalSkill.Medicine] = 2;
                donor = Patient();
                Assert.That(em.System<VirologySystem>().Expose(donor, strain, 100), Is.True);
                em.System<VirologySystem>().Advance(donor, 180);
            });

            foreach (var prototype in new[] { "Syringe", "Hypospray", "HypoMini", "HyposprayAcorp", "Vaccinated", "CleanBlood" })
            {
                await pair.Server.WaitAssertion(() =>
                {
                    recipient = Patient();
                    if (prototype == "Vaccinated")
                        Assert.That(em.System<VirologySystem>().ApplyDose(recipient, "Hematic", true), Is.True);
                    syringe = em.SpawnEntity("Syringe", map.GridCoords);
                    Assert.That(em.System<HandsSystem>().TryPickupAnyHand(doctor, syringe), Is.True);
                    em.System<InjectorSystem>().SetMode((syringe, em.GetComponent<InjectorComponent>(syringe)), InjectorToggleMode.Draw);
                    var source = prototype == "CleanBlood" ? Patient() : donor;
                    Interact(syringe, source);
                });
                await pair.Server.WaitRunTicks(360);
                await pair.Server.WaitAssertion(() =>
                {
                    var contents = Contents(syringe, em.GetComponent<InjectorComponent>(syringe).SolutionName);
                    Assert.That(contents.Volume, Is.EqualTo(em.GetComponent<InjectorComponent>(syringe).TransferAmount), prototype);
                    Assert.That(contents.GetTotalPrototypeQuantity("CultureHematic") > FixedPoint2.Zero,
                        Is.EqualTo(prototype != "CleanBlood"), prototype);
                    em.System<InjectorSystem>().SetMode((syringe, em.GetComponent<InjectorComponent>(syringe)), InjectorToggleMode.Inject);
                    if (prototype is "Syringe" or "Vaccinated" or "CleanBlood")
                    {
                        device = syringe;
                        Interact(device, recipient);
                        return;
                    }

                    device = em.SpawnEntity(prototype, map.GridCoords);
                    var hypo = em.GetComponent<HyposprayComponent>(device);
                    Interact(syringe, device);
                    Assert.That(Contents(device, hypo.SolutionName).GetTotalPrototypeQuantity("CultureHematic"),
                        Is.GreaterThan(FixedPoint2.Zero), "Contaminated blood must survive refilling the hypospray.");
                    em.DeleteEntity(syringe);
                });
                await pair.Server.WaitRunTicks(1);
                if (prototype is not ("Syringe" or "Vaccinated" or "CleanBlood"))
                {
                    await pair.Server.WaitAssertion(() =>
                    {
                        Assert.That(em.System<HandsSystem>().TryPickupAnyHand(doctor, device), Is.True);
                        Assert.That(em.System<HypospraySystem>().TryDoInject(
                            (device, em.GetComponent<HyposprayComponent>(device)), recipient, doctor), Is.True);
                    });
                }
                await pair.Server.WaitRunTicks(360);
                await pair.Server.WaitAssertion(() =>
                {
                    var infected = em.TryGetComponent<VirologyCarrierComponent>(recipient, out var carrier)
                        && carrier.Infections.ContainsKey(strain);
                    Assert.That(infected, Is.EqualTo(prototype is not ("Vaccinated" or "CleanBlood")), prototype);
                    em.DeleteEntity(device);
                });
            }

            void Interact(EntityUid used, EntityUid target)
            {
                var interaction = new AfterInteractEvent(doctor, used, target, map.GridCoords, true);
                em.EventBus.RaiseLocalEvent(used, interaction);
                Assert.That(interaction.Handled, Is.True);
            }

            EntityUid Patient()
            {
                var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                // The test grid is in vacuum. Breathing is outside the injection scenario.
                em.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(patient);
                em.AddComponent<Content.Shared.Damage.Components.GodmodeComponent>(patient);
                return patient;
            }

            Solution Contents(EntityUid item, string name)
            {
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(item, name, out _, out var solution), Is.True);
                return solution!;
            }
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
