using System;
using System.Linq;
using Content.Server._radiant.Fishing;
using Content.Server._radiant.Social;
using Content.Server._radiant.Dossiers;
using Content.Server._radiant.GameRule.Components;
using Content.Server.Power.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Mobs.Systems;
using Content.Shared.Damage;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared._radiant.Social;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class SocialWorkshopBehaviorTest
{
    [Test]
    public async Task PhotocopierUsesTheLoadedResearchBaseAndPreviewsThePrintedMedicalForms()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var baseMap = await pair.CreateTestMap();
        var shuttleMap = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var metadata = em.System<MetaDataSystem>();
            var marker = em.SpawnEntity(null, baseMap.GridCoords);
            em.AddComponent<AcolithComponent>(marker);
            var baseGrid = em.GetComponent<TransformComponent>(marker).GridUid!.Value;
            var user = em.SpawnEntity("MobHumanDummy", shuttleMap.GridCoords);
            var copier = em.SpawnEntity("Photocopier", shuttleMap.GridCoords);
            var component = em.GetComponent<PhotocopierComponent>(copier);
            component.Forms = ["AcorpDocument", "AcorpWorkReport", "MedicalConsent", "MedicalExamination",
                "MedicalReferral", "MedicalSurgeryReport", "MedicalDischarge", "MedicalPrescription", "MedicalDeathCertificate"];
            em.GetComponent<ApcPowerReceiverComponent>(copier).Powered = true;
            string Print(string form)
            {
                component.ReadyAt = TimeSpan.Zero;
                var before = em.EntityQuery<PaperComponent>().ToHashSet();
                em.EventBus.RaiseLocalEvent(copier,
                    new PhotocopierPrintMessage(form, 1) { Actor = user, UiKey = PhotocopierUiKey.Key });
                var printed = em.EntityQuery<PaperComponent>().Single(paper => !before.Contains(paper));
                Assert.That(em.System<UserInterfaceSystem>().TryGetUiState<PhotocopierUiState>(copier, PhotocopierUiKey.Key, out var state), Is.True);
                Assert.That(state!.Forms.Single(entry => entry.Id == form).Content, Is.EqualTo(printed.Content), "Preview and output must use the same layout and print metadata.");
                return printed.Content;
            }

            metadata.SetEntityName(baseGrid, "МНБ Горизонт");
            Assert.That(Print("AcorpDocument"), Does.Contain("МНБ Горизонт"));
            metadata.SetEntityName(baseGrid, "МНБ Икарус");
            Assert.That(Print("AcorpWorkReport"), Does.Contain("МНБ Икарус").And.Not.Contain("Горизонт"));
            foreach (var id in new[] { "MedicalConsent", "MedicalExamination", "MedicalReferral", "MedicalSurgeryReport", "MedicalDischarge", "MedicalPrescription", "MedicalDeathCertificate" })
                Assert.That(Print(id), Does.Contain("[color=#28716b]").And.Contain("[form]").And.Contain(PaperworkClock.Date(DateTimeOffset.UtcNow)));
            em.RemoveComponent<AcolithComponent>(marker);
            Assert.That(Print("AcorpDocument"), Does.Contain("[form]").And.Not.Contain("Горизонт").And.Not.Contain("Икарус"), "No base must not imply a hard-coded Horizon.");
        });
        TestContext.Out.WriteLine("All research-base, medical template, and print-preview assertions passed.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TattooRequiresRecipientConsentAndRevocationDoesNotSpendInk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        EntityUid machine = default;
        EntityUid patient = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
            air.AdjustMoles(Gas.Oxygen, 21);
            air.AdjustMoles(Gas.Nitrogen, 79);
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var artist = em.SpawnEntity("MobHuman", map.GridCoords);
            patient = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<DossierHolderComponent>(patient).Loaded = true;
            machine = em.SpawnEntity("TattooMachine", map.GridCoords);
            var hands = em.System<ServerHands>();
            hands.AddHand(artist, "tattoo-test", HandLocation.Right);
            hands.SetActiveHand(artist, "tattoo-test");
            Assert.That(hands.TryPickup(artist, machine, "tattoo-test", checkActionBlocker: false, animate: false), Is.True);
            var use = new AfterInteractEvent(artist, machine, patient, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(machine, use);
            Assert.That(use.Handled, Is.True);
            var tattoo = em.GetComponent<TattooMachineComponent>(machine);
            var forged = new TattooProposeMessage("Forged", 2, 1) { Actor = patient, UiKey = TattooUiKey.Editor };
            em.EventBus.RaiseLocalEvent(machine, forged);
            Assert.That(tattoo.Pending, Is.Null);
            var request = new TattooProposeMessage("Remember", 2, 1) { Actor = artist, UiKey = TattooUiKey.Editor };
            em.EventBus.RaiseLocalEvent(machine, request);
            Assert.That(tattoo.Pending?.Text, Is.EqualTo("Remember"));
            var forgedConsent = new TattooConsentMessage(true) { Actor = artist, UiKey = TattooUiKey.Consent };
            em.EventBus.RaiseLocalEvent(machine, forgedConsent);
            Assert.That(tattoo.Accepted, Is.False);
            var accepted = new TattooConsentMessage(true) { Actor = patient, UiKey = TattooUiKey.Consent };
            em.EventBus.RaiseLocalEvent(machine, accepted);
            Assert.That(tattoo.Accepted, Is.True);
            var revoked = new TattooConsentMessage(false) { Actor = patient, UiKey = TattooUiKey.Consent };
            em.EventBus.RaiseLocalEvent(machine, revoked);
            Assert.That(tattoo.Pending, Is.Null);
            Assert.That(tattoo.Accepted, Is.False);
        });
        await pair.RunTicksSync(300);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.GetComponent<TattooMachineComponent>(machine).Ink, Is.EqualTo(20));
            Assert.That(em.GetComponent<DossierHolderComponent>(patient).Record.Tattoos, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PhotocopierPrintsDatedFormsCopiesOriginalsAndConsumesSupplies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var copier = em.SpawnEntity("Photocopier", map.GridCoords);
            var printer = em.GetComponent<PhotocopierComponent>(copier);
            printer.Forms = ["GeneralDocument"];
            var power = em.GetComponent<ApcPowerReceiverComponent>(copier);
            power.Powered = true;
            void Print(string? form, int copies = 1)
            {
                var request = new PhotocopierPrintMessage(form, copies) { Actor = user, UiKey = PhotocopierUiKey.Key };
                em.EventBus.RaiseLocalEvent(copier, request);
            }
            Print("GeneralDocument");
            Assert.That(printer.Paper, Is.EqualTo(19));
            Assert.That(printer.Ink, Is.EqualTo(99));
            var printed = em.EntityQuery<PaperComponent>().Single(p => p.Content.Contains(PaperworkClock.Date(DateTimeOffset.UtcNow)));
            Assert.That(printed.Content, Does.Contain("00:00:"));
            Print("GeneralDocument");
            Assert.That(printer.Paper, Is.EqualTo(19), "Cooldown prevents repeated requests");
            printer.ReadyAt = TimeSpan.Zero;
            Print("GeneralDocument", 100);
            Assert.That(printer.Paper, Is.EqualTo(19));
            Print("InvalidForm");
            Assert.That(printer.Paper, Is.EqualTo(19));
            power.Powered = false;
            Print("GeneralDocument");
            Assert.That(printer.Paper, Is.EqualTo(19));
            power.Powered = true;
            var original = em.SpawnEntity("Paper", map.GridCoords);
            var paper = em.GetComponent<PaperComponent>(original);
            em.System<PaperSystem>().SetContent(original, "Original [bold]text[/bold]");
            paper.Language = "TestLanguage";
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(copier, "original", original, null), Is.True);
            Print(null);
            var copies = em.EntityQuery<PaperComponent>().Where(p => p.Content == paper.Content).ToArray();
            Assert.That(copies.Length, Is.EqualTo(2));
            Assert.That(copies.All(p => p.Language == "TestLanguage"), Is.True);
            Assert.That(printer.Paper, Is.EqualTo(18));
            printer.Paper = 0;
            var stack = em.SpawnEntity("SheetPaper", map.GridCoords);
            var count = em.System<SharedStackSystem>().GetCount(stack);
            var refill = new InteractUsingEvent(user, stack, copier, map.GridCoords);
            em.EventBus.RaiseLocalEvent(copier, refill);
            Assert.That(printer.Paper, Is.EqualTo(count));
            printer.Ink = 0;
            var toner = em.SpawnEntity("PrinterToner", map.GridCoords);
            var inkRefill = new InteractUsingEvent(user, toner, copier, map.GridCoords);
            em.EventBus.RaiseLocalEvent(copier, inkRefill);
            Assert.That(printer.Ink, Is.EqualTo(100));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HairRestoresOriginalAndBouquetDisplaysItsAttachedNote()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var appearance = em.GetComponent<HumanoidAppearanceComponent>(user);
            appearance.MarkingSet.RemoveCategory(MarkingCategories.Hair);
            em.System<SharedHumanoidAppearanceSystem>().AddMarking(user, "HumanHairLong", Color.Red, humanoid: appearance);
            var oldHair = appearance.MarkingSet.Markings[MarkingCategories.Hair].Select(m => new Marking(m)).ToArray();
            var tie = em.SpawnEntity("ClothingHeadHairTie", map.GridCoords);
            var clothing = em.GetComponent<ClothingComponent>(tie);
            var equip = new ClothingGotEquippedEvent(user, clothing);
            em.EventBus.RaiseLocalEvent(tie, ref equip);
            Assert.That(appearance.MarkingSet.Markings[MarkingCategories.Hair].Single().MarkingId, Is.EqualTo("HumanHairPonytail"));
            var unequip = new ClothingGotUnequippedEvent(user, clothing);
            em.EventBus.RaiseLocalEvent(tie, ref unequip);
            var restored = appearance.MarkingSet.Markings[MarkingCategories.Hair].Single();
            Assert.That(restored.MarkingId, Is.EqualTo(oldHair.Single().MarkingId));
            Assert.That(restored.MarkingColors, Is.EqualTo(oldHair.Single().MarkingColors));
            var bouquet = em.SpawnEntity("BlueBouquet", map.GridCoords);
            var note = em.SpawnEntity("Paper", map.GridCoords);
            em.System<PaperSystem>().SetContent(note, "For you, [bold]always[/bold].");
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(bouquet, "note", note, null), Is.True);
            var examine = new ExaminedEvent(new FormattedMessage(), bouquet, user, true, false);
            em.EventBus.RaiseLocalEvent(bouquet, examine);
            Assert.That(examine.GetTotalMessage().ToString(), Does.Contain("For you, always."));
            Assert.That(em.System<ItemSlotsSystem>().TryEject(bouquet, "note", null, out _), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DecksAreFiniteAndSaunaConsumesWood()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var deck = em.SpawnEntity("QuestionDeck", map.GridCoords);
            for (var i = 0; i < 11; i++)
            {
                var draw = new UseInHandEvent(user);
                em.EventBus.RaiseLocalEvent(deck, draw);
            }
            var cards = em.EntityQuery<PaperComponent>().Where(p => em.GetComponent<MetaDataComponent>(p.Owner).EntityPrototype?.ID == "QuestionCard").ToArray();
            Assert.That(cards.Length, Is.EqualTo(10));
            Assert.That(cards.Select(c => c.Content).Distinct().Count(), Is.EqualTo(10));
            Assert.That(em.GetComponent<QuestionDeckComponent>(deck).Remaining, Is.Empty);
            var heater = em.SpawnEntity("SaunaHeater", map.GridCoords);
            var machine = em.GetComponent<SaunaHeaterComponent>(heater);
            var emptyStart = new ActivateInWorldEvent(user, heater, true);
            em.EventBus.RaiseLocalEvent(heater, emptyStart);
            Assert.That(machine.Lit, Is.False);
            var wood = em.SpawnEntity("MaterialWoodPlank", map.GridCoords);
            var fuel = new InteractUsingEvent(user, wood, heater, map.GridCoords);
            em.EventBus.RaiseLocalEvent(heater, fuel);
            Assert.That(machine.FuelSeconds, Is.GreaterThan(0));
            var start = new ActivateInWorldEvent(user, heater, true);
            em.EventBus.RaiseLocalEvent(heater, start);
            Assert.That(machine.Lit, Is.True);
            var remaining = machine.FuelSeconds;
            em.System<SaunaHeaterSystem>().Update(1);
            Assert.That(machine.FuelSeconds, Is.LessThan(remaining));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FishHaveInheritedGenesGrowAndRespectTankCapacity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        EntityUid tank = default;
        EntityUid mother = default;
        EntityUid father = default;
        EntityUid child = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
            air.AdjustMoles(Gas.Oxygen, 21);
            air.AdjustMoles(Gas.Nitrogen, 79);
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            tank = em.SpawnEntity("RadiantAquariumFilled", map.GridCoords);
            var aquarium = em.GetComponent<AquariumComponent>(tank);
            aquarium.Food = aquarium.MaxFood;
            var storage = em.System<SharedContainerSystem>().GetContainer(tank, "storagebase");
            mother = em.SpawnEntity("FishBass", map.GridCoords);
            father = em.SpawnEntity("FishBass", map.GridCoords);
            var maternal = em.GetComponent<FishBreedingComponent>(mother);
            var paternal = em.GetComponent<FishBreedingComponent>(father);
            maternal.Female = true;
            paternal.Female = false;
            maternal.FirstAllele = maternal.SecondAllele = 1;
            paternal.FirstAllele = paternal.SecondAllele = 2;
            Assert.That(em.System<SharedContainerSystem>().Insert(mother, storage), Is.True);
            Assert.That(em.System<SharedContainerSystem>().Insert(father, storage), Is.True);
            em.GetComponent<AquariumBreedingComponent>(tank).NextCheck = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
        });
        await pair.RunSeconds(2);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var container = em.System<SharedContainerSystem>().GetContainer(tank, "storagebase");
            child = container.ContainedEntities.Single(e => e != mother && e != father && em.HasComponent<FishComponent>(e));
            var genes = em.GetComponent<FishBreedingComponent>(child);
            Assert.That(genes.Juvenile, Is.True);
            Assert.That(genes.FirstAllele, Is.EqualTo(1));
            Assert.That(genes.SecondAllele, Is.EqualTo(2));
            genes.GrowthSeconds = FishBreedingRules.GrowthDuration;
        });
        await pair.RunSeconds(2);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.System<MobStateSystem>().IsAlive(child), Is.True,
                "Fry must remain healthy in a fed aquarium. Damage: " + em.GetComponent<DamageableComponent>(child).TotalDamage);
            Assert.That(em.GetComponent<FishBreedingComponent>(child).Juvenile, Is.False,
                "Water=" + em.GetComponent<FishComponent>(child).InAquariumWater +
                "; food=" + em.GetComponent<AquariumComponent>(tank).Food +
                "; growth=" + em.GetComponent<FishBreedingComponent>(child).GrowthSeconds);
            var container = em.System<SharedContainerSystem>().GetContainer(tank, "storagebase");
            Assert.That(em.System<SharedContainerSystem>().Insert(em.SpawnEntity("FishBass", map.GridCoords), container), Is.True);
            em.GetComponent<FishBreedingComponent>(mother).ReadyAt = TimeSpan.Zero;
            em.GetComponent<FishBreedingComponent>(father).ReadyAt = TimeSpan.Zero;
            em.GetComponent<AquariumBreedingComponent>(tank).NextCheck = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
        });
        await pair.RunSeconds(2);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var container = em.System<SharedContainerSystem>().GetContainer(tank, "storagebase");
            Assert.That(container.ContainedEntities.Count(em.HasComponent<FishComponent>), Is.EqualTo(4));
        });
        await pair.CleanReturnAsync();
    }
}
