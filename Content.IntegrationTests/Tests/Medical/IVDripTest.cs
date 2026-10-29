using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Body.Components;
using Content.Server._radiant.Medical.Virology;
using Content.Shared._radiant.Medical.IV;
using Content.Shared._radiant.Skills;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.DragDrop;
using Content.Shared.Lathe.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using ServerIV = Content.Server._radiant.Medical.IV.IVDripSystem;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class IVDripTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: IVTestPatient
  parent: BaseItem
  components:
  - type: Bloodstream
    bloodMaxVolume: 100
    chemicalMaxVolume: 100
    bloodRefreshAmount: 0
    bloodlossDamage: {}
    bloodlossHealDamage: {}
  - type: Damageable
    damageContainer: Biological
";

    [TestCase("RadiantIVDrip")]
    [TestCase("RadiantIVDripTelescopic")]
    public async Task DosesAreTimedConservativeAndRespectSkillsAndDetachment(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var iv = em.System<ServerIV>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var slots = em.System<ItemSlotsSystem>();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var patient = em.SpawnEntity("IVTestPatient", map.GridCoords);
                var blood = em.GetComponent<BloodstreamComponent>(patient);
                var bloodSolution = Contents(patient, blood.BloodSolutionName);
                var chemicalSolution = Contents(patient, blood.ChemicalSolutionName);
                var stand = em.SpawnEntity(prototype, map.GridCoords);
                var comp = em.GetComponent<IVDripComponent>(stand);
                var bag = em.SpawnEntity("RadiantIVBag", map.GridCoords);
                var contents = Contents(bag, "iv_bag");
                Assert.That(comp.TransferAmount, Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(comp.TransferInterval, Is.EqualTo(TimeSpan.FromSeconds(3)));
                Assert.That(iv.SetTransferAmount((stand, comp), user, 1), Is.True);
                comp.TransferInterval = TimeSpan.FromSeconds(0.5); // A fast prototype must not bypass the pump cadence.
                Assert.That(slots.TryInsert(stand, "iv_bag", bag, user), Is.True);
                Assert.That(solutions.TryGetSolution(bag, "iv_bag", out var bagSolution, out _), Is.True);
                solutions.TryAddReagent(bagSolution!.Value, "Water", 10);
                Assert.That(comp.FillPercentage, Is.EqualTo(5), "An inserted bag must refresh even before a patient is connected.");
                Assert.That(iv.TryAttach((stand, comp), user, patient), Is.True);
                iv.Update(1000);
                Assert.That(chemicalSolution.Volume, Is.EqualTo(FixedPoint2.Zero), "Connecting must not deliver an immediate bolus.");
                Dose();
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(9)));
                Assert.That(chemicalSolution.Volume, Is.EqualTo(FixedPoint2.New(1)));
                Assert.That(bloodSolution.Volume, Is.EqualTo(FixedPoint2.New(100)));
                iv.Update(1000);
                Assert.That(chemicalSolution.Volume, Is.EqualTo(FixedPoint2.New(1)), "Several updates at the same time cannot duplicate a dose.");
                Assert.That(comp.NextTransfer - pair.Server.ResolveDependency<IGameTiming>().CurTime,
                    Is.EqualTo(TimeSpan.FromSeconds(3)));
                Dose();
                Assert.That(chemicalSolution.Volume, Is.EqualTo(FixedPoint2.New(2)), "An overdue timer still delivers one dose only.");
                Assert.That(iv.SetTransferAmount((stand, comp), user, FixedPoint2.New(0.5)), Is.False);
                Assert.That(iv.SetTransferAmount((stand, comp), user, 999), Is.False);
                Assert.That(iv.SetTransferAmount((stand, comp), user, 5), Is.True);
                Dose();
                Assert.That(chemicalSolution.Volume, Is.EqualTo(FixedPoint2.New(7)));
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(3)));

                Assert.That(iv.SetInjecting((stand, comp), user, false), Is.True);
                var beforeBlood = bloodSolution.Volume;
                Dose();
                Assert.That(bloodSolution.Volume, Is.EqualTo(beforeBlood - 5));
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(8)));
                Assert.That(iv.SetInjecting((stand, comp), user, true), Is.True);
                solutions.RemoveAllSolution(bagSolution.Value);
                solutions.TryAddReagent(bagSolution.Value, "Blood", 5);
                Dose();
                Assert.That(bloodSolution.Volume, Is.EqualTo(beforeBlood), "Matching blood must refill blood, not become medicine.");
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.Zero));
                solutions.TryAddReagent(bagSolution.Value, "Blood", 5);
                Dose();
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(5)), "A full patient must not destroy unused blood.");

                // Mixed doses preserve proportions, including a partially available destination.
                solutions.RemoveAllSolution(bagSolution.Value);
                solutions.TryAddReagent(bagSolution.Value, "Water", 1);
                solutions.TryAddReagent(bagSolution.Value, "Blood", 1);
                Assert.That(solutions.TryGetSolution(patient, blood.BloodSolutionName, out var bloodEntity, out _), Is.True);
                solutions.SplitSolution(bloodEntity!.Value, FixedPoint2.New(0.25));
                var beforeChemistry = chemicalSolution.Volume;
                Dose();
                Assert.That(bloodSolution.Volume, Is.EqualTo(beforeBlood));
                Assert.That(chemicalSolution.Volume, Is.EqualTo(beforeChemistry + FixedPoint2.New(0.25)));
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(1.5)));
                var other = em.SpawnEntity(prototype, map.GridCoords);
                var otherComp = em.GetComponent<IVDripComponent>(other);
                Assert.That(iv.TryAttach((other, otherComp), user, patient), Is.True);
                Assert.That(comp.AttachedTo, Is.Null, "Only one stand may be connected to a patient.");
                var hand = new InteractHandEvent(user, other);
                em.EventBus.RaiseLocalEvent(other, hand);
                Assert.That(hand.Handled, Is.True);
                Assert.That(otherComp.AttachedTo, Is.Null);

                var skills = em.EnsureComponent<ProfessionalSkillsComponent>(user);
                skills.Levels[(int)ProfessionalSkill.Medicine] = 0;
                Assert.That(iv.TryAttach((stand, comp), user, patient), Is.False);
                skills.Levels[(int)ProfessionalSkill.Medicine] = 1;
                var drag = new DragDropDraggedEvent(user, patient);
                em.EventBus.RaiseLocalEvent(stand, ref drag);
                Assert.That(drag.Handled, Is.True);
                Assert.That(comp.AttachedTo, Is.EqualTo(patient));
                var transforms = em.System<SharedTransformSystem>();
                transforms.SetCoordinates(patient, map.GridCoords.Offset(new Vector2(4, 0)));
                iv.Update(0);
                Assert.That(comp.AttachedTo, Is.Null);
                Assert.That(em.GetComponent<DamageableComponent>(patient).TotalDamage, Is.EqualTo(FixedPoint2.New(3)));

                Assert.That(pair.Server.ProtoMan.Index<LatheRecipePackPrototype>("NFMedicalStatic").Recipes
                    .Any(recipe => recipe.Id == "RadiantIVBag"), Is.True);

                void Dose() { comp.NextTransfer = TimeSpan.Zero; iv.Update(0); }
                Solution Contents(EntityUid uid, string name)
                {
                    Assert.That(solutions.TryGetSolution(uid, name, out _, out var solution), Is.True);
                    return solution!;
                }
            });

            Solution scheduledMedicine = default!;
            var earlyTicks = 0;
            var laterTicks = 0;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var timing = pair.Server.ResolveDependency<IGameTiming>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var stand = em.SpawnEntity(prototype, map.GridCoords);
                var bag = em.SpawnEntity("RadiantIVBag", map.GridCoords);
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var patient = em.SpawnEntity("IVTestPatient", map.GridCoords);
                Assert.That(solutions.TryGetSolution(bag, "iv_bag", out var source, out _), Is.True);
                solutions.TryAddReagent(source!.Value, "Water", 10);
                Assert.That(solutions.TryGetSolution(patient, "chemicals", out _, out var target), Is.True);
                scheduledMedicine = target!;
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(stand, "iv_bag", bag, user), Is.True);
                Assert.That(em.System<ServerIV>().TryAttach((stand, em.GetComponent<IVDripComponent>(stand)), user, patient), Is.True);
                earlyTicks = Math.Max(1, (int)(2.9 / timing.TickPeriod.TotalSeconds));
                laterTicks = (int)Math.Ceiling(3.1 / timing.TickPeriod.TotalSeconds) - earlyTicks;
            });
            await pair.RunTicksSync(earlyTicks);
            await pair.Server.WaitAssertion(() => Assert.That(scheduledMedicine.Volume, Is.EqualTo(FixedPoint2.Zero)));
            await pair.RunTicksSync(laterTicks);
            await pair.Server.WaitAssertion(() => Assert.That(scheduledMedicine.Volume, Is.EqualTo(FixedPoint2.New(5)),
                "The live scheduler must deliver the default five-unit dose after three seconds."));

            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var maps = em.System<SharedMapSystem>();
                var mapUid = maps.CreateMap(out var mapId);
                var stand = em.SpawnEntity(prototype, new MapCoordinates(Vector2.Zero, mapId));
                var iv = em.GetComponent<IVDripComponent>(stand);
                var sprite = em.GetComponent<SpriteComponent>(stand);
                iv.HasBag = true;
                iv.FillPercentage = 50;
                foreach (var flowing in new[] { false, true })
                foreach (var injecting in new[] { false, true })
                {
                    iv.Flowing = flowing;
                    iv.Injecting = injecting;
                    var changed = new AfterAutoHandleStateEvent();
                    em.EventBus.RaiseComponentEvent(stand, iv, ref changed);
                    var state = (injecting ? flowing ? "injecting" : "injectidle" : flowing ? "donating" : "donateidle")
                        + (iv.Telescopic ? "_tele" : "");
                    Assert.That(sprite.LayerGetState(sprite.LayerMapGet(IVDripVisuals.Stand)).ToString(), Is.EqualTo(state));
                    Assert.That(sprite.BaseRSI!.TryGetState(state, out _), Is.True);
                }
                em.DeleteEntity(mapUid);
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task LowFlowDoesNotAccumulateStandardMedicinesAndNormalOverdoseEffectsStillApply()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var patients = new List<(EntityUid Patient, string Reagent, int Dose, Solution Contents, string Damage)>();
            var ticks = 0;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var solutions = em.System<SharedSolutionContainerSystem>();
                var iv = em.System<ServerIV>();
                foreach (var (reagent, damage) in new[] { ("Bicaridine", "Poison"), ("Dermaline", "Blunt") })
                foreach (var amount in new[] { 1, 5 })
                {
                    // Separate bodies and wheeled stands so collision damage cannot masquerade as an overdose.
                    var coordinates = map.GridCoords.Offset(new Vector2(patients.Count * 4, 0));
                    var doctor = em.SpawnEntity("MobHumanDummy", coordinates.Offset(new Vector2(0, 1)));
                    var patient = em.SpawnEntity("MobHuman", coordinates);
                    // Exclude both suffocation and low-pressure damage on the vacuum test map.
                    em.RemoveComponent<RespiratorComponent>(patient);
                    em.EnsureComponent<Content.Server.Atmos.Components.PressureImmunityComponent>(patient);
                    var bloodstream = em.GetComponent<BloodstreamComponent>(patient);
                    Assert.That(solutions.TryGetSolution(patient, bloodstream.ChemicalSolutionName, out var target, out var contents), Is.True);
                    solutions.RemoveAllSolution(target!.Value);
                    var bag = em.SpawnEntity("RadiantIVBag", coordinates);
                    Assert.That(solutions.TryGetSolution(bag, "iv_bag", out var source, out _), Is.True);
                    solutions.TryAddReagent(source!.Value, reagent, 100);
                    var stand = em.SpawnEntity("RadiantIVDrip", coordinates.Offset(new Vector2(0.75f, 0)));
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(stand, "iv_bag", bag, doctor), Is.True);
                    Assert.That(iv.SetTransferAmount((stand, em.GetComponent<IVDripComponent>(stand)), doctor, amount), Is.True);
                    Assert.That(iv.TryAttach((stand, em.GetComponent<IVDripComponent>(stand)), doctor, patient), Is.True);
                    patients.Add((patient, reagent, amount, contents!, damage));
                }
                ticks = (int)Math.Ceiling(16.2 / pair.Server.ResolveDependency<IGameTiming>().TickPeriod.TotalSeconds);
            });
            await pair.RunTicksSync(ticks);
            await pair.Server.WaitAssertion(() =>
            {
                foreach (var (patient, reagent, amount, contents, damageType) in patients)
                {
                    var quantity = contents.GetTotalPrototypeQuantity(reagent);
                    var damage = pair.Server.EntMan.GetComponent<DamageableComponent>(patient).Damage.DamageDict.GetValueOrDefault(damageType);
                    if (amount == 1)
                    {
                        Assert.That(quantity, Is.LessThanOrEqualTo(FixedPoint2.New(1)), reagent + ": low flow must not accumulate at normal human metabolism");
                        Assert.That(damage, Is.EqualTo(FixedPoint2.Zero), reagent + ": low flow must not trigger overdose damage");
                    }
                    else
                    {
                        Assert.That(quantity, Is.GreaterThanOrEqualTo(FixedPoint2.New(reagent == "Bicaridine" ? 15 : 10)), reagent);
                        Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), reagent + ": IV administration must not bypass normal overdose effects");
                    }
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [TestCase("Hematic")]
    [TestCase("Hematic~Fatigue")]
    public async Task DonatedBloodKeepsInfectionAndVaccinationStillProtects(string strain)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var solutions = em.System<SharedSolutionContainerSystem>();
                var iv = em.System<ServerIV>();
                var virology = em.System<VirologySystem>();
                var doctor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var donor = Patient();
                Assert.That(virology.Expose(donor, strain, 100), Is.True);
                virology.Advance(donor, 180);
                foreach (var vaccinated in new[] { false, true })
                {
                    var stand = em.SpawnEntity("RadiantIVDrip", map.GridCoords);
                    var comp = em.GetComponent<IVDripComponent>(stand);
                    var bag = em.SpawnEntity("RadiantIVBag", map.GridCoords);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(stand, "iv_bag", bag, doctor), Is.True);
                    Assert.That(iv.SetTransferAmount((stand, comp), doctor, 15), Is.True);
                    Assert.That(iv.SetInjecting((stand, comp), doctor, false), Is.True);
                    Assert.That(iv.TryAttach((stand, comp), doctor, donor), Is.True);
                    for (var i = 0; i < 5; i++) { comp.NextTransfer = TimeSpan.Zero; iv.Update(0); }
                    Assert.That(solutions.TryGetSolution(bag, "iv_bag", out _, out var contents), Is.True);
                    Assert.That(contents!.Volume, Is.EqualTo(FixedPoint2.New(75)));
                    Assert.That(contents.GetTotalPrototypeQuantity("CultureHematic"), Is.GreaterThan(FixedPoint2.Zero));
                    var recipient = Patient();
                    var blood = em.GetComponent<BloodstreamComponent>(recipient);
                    Assert.That(solutions.TryGetSolution(recipient, blood.BloodSolutionName, out var bloodSolution, out _), Is.True);
                    solutions.SplitSolution(bloodSolution!.Value, 100);
                    if (vaccinated)
                        Assert.That(virology.ApplyDose(recipient, "Hematic", true), Is.True);
                    Assert.That(iv.SetInjecting((stand, comp), doctor, true), Is.True);
                    Assert.That(iv.TryAttach((stand, comp), doctor, recipient), Is.True);
                    for (var i = 0; i < 5; i++) { comp.NextTransfer = TimeSpan.Zero; iv.Update(0); }
                    Assert.That(em.TryGetComponent<VirologyCarrierComponent>(recipient, out var carrier)
                        && carrier.Infections.ContainsKey(strain), Is.EqualTo(!vaccinated));
                    iv.Detach((stand, comp));
                }
                EntityUid Patient()
                {
                    var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                    em.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(patient);
                    em.AddComponent<Content.Shared.Damage.Components.GodmodeComponent>(patient);
                    return patient;
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
