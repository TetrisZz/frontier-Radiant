using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._radiant.Media;
using Content.Server._CorvaxGoob.Photo;
using Content.Server._radiant.Artisan;
using Content.Server._radiant.Dossiers;
using Content.Server._radiant.Media;
using Content.Server._radiant.Medical.Autopsy;
using Content.Shared._radiant.Artisan;
using Content.Shared._radiant.Media;
using Content.Shared._radiant.Medical.Autopsy;
using Content.Shared._radiant.Skills;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.MassMedia.Components;
using Content.Shared.Materials;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Content.Shared.Paper;
using Robust.Client.UserInterface;
using Robust.Client.ResourceManagement;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CivicMechanicsTest
{
    [Test]
    public async Task SculpturesConsumeMaterialsRequireAChiselAndPreserveMakerAndMaterial()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var system = em.System<ArtisanSystem>();
                var containers = em.System<SharedContainerSystem>();
                var hands = em.System<SharedHandsSystem>();
                var actor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var chisel = em.SpawnEntity("RadiantSculptingChisel", map.GridCoords);
                foreach (var (material, result, recipe, amount) in new[]
                {
                    ("SpaceQuartz1", "RadiantSculptureStoneBust", "ArtisanSculptStoneBust", 10),
                    ("MaterialWoodPlank1", "RadiantSculptureWoodCorgi", "ArtisanSculptWoodCorgi", 8),
                    ("SheetSteel1", "RadiantSculptureSteelMedic", "ArtisanSculptSteelMedic", 8),
                    ("IngotGold1", "RadiantSculptureGoldScholar", "ArtisanSculptGoldScholar", 8),
                })
                {
                    var station = em.SpawnEntity("RadiantSculptingBench", map.GridCoords);
                    var data = em.GetComponent<ArtisanStationComponent>(station);
                    var input = containers.GetContainer(station, ArtisanStationComponent.Container);
                    var raw = em.SpawnEntity(material, map.GridCoords);
                    em.System<SharedStackSystem>().SetCount(raw, amount);
                    Assert.That(containers.Insert(raw, input), Is.True);
                    foreach (var held in hands.EnumerateHeld(actor).ToArray()) hands.TryDrop(actor, held);
                    Send(new(ArtisanAction.Recipe, recipe));
                    Assert.That(data.Busy, Is.False, "A missing tool must not start a job.");
                    Assert.That(em.GetComponent<StackComponent>(raw).Count, Is.EqualTo(amount));
                    Assert.That(hands.TryPickupAnyHand(actor, chisel, animate: false), Is.True);
                    Send(new(ArtisanAction.Recipe, recipe));
                    Assert.That(data.Busy, Is.True);
                    hands.TryDrop(actor, chisel);
                    system.Update(1);
                    Assert.That(data.Busy, Is.False, "Removing the chisel must interrupt manual work.");
                    Assert.That(em.GetComponent<StackComponent>(raw).Count, Is.EqualTo(amount));
                    Assert.That(hands.TryPickupAnyHand(actor, chisel, animate: false), Is.True);
                    Send(new(ArtisanAction.Recipe, recipe)); system.Update(data.Remaining + .1f);
                    var blank = input.ContainedEntities.Single(e => em.HasComponent<ArtisanWorkpieceComponent>(e));
                    Assert.That(!em.EntityExists(raw) || em.GetComponent<StackComponent>(raw).Count == 0, Is.True,
                        "The rough-carving stage consumes the input once; empty stacks may await deferred deletion.");
                    Send(new(ArtisanAction.Process, item: em.GetNetEntity(blank))); system.Update(data.Remaining + .1f);
                    var sculpture = input.ContainedEntities.Single(e => em.HasComponent<ArtisanProductComponent>(e));
                    Assert.That(em.GetComponent<MetaDataComponent>(sculpture).EntityPrototype!.ID, Is.EqualTo(result));
                    Assert.That(em.GetComponent<ArtisanProductComponent>(sculpture).Maker,
                        Is.EqualTo(em.GetComponent<MetaDataComponent>(actor).EntityName));
                    Assert.That(em.HasComponent<ArtisanWorkpieceComponent>(sculpture), Is.False);
                    void Send(ArtisanActionMessage message)
                    { message.Actor = actor; message.UiKey = ArtisanUiKey.Key; em.EventBus.RaiseLocalEvent(station, message); }
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task NewspapersUseTheSectorFeedConsumePaperThrottlePrintingAndKeepPhotoSnapshots()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            NewsroomUiState? preview = null;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                SectorNewsComponent.Articles.Clear();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var terminal = em.SpawnEntity("RadiantNewsroomConsole", map.GridCoords);
                var component = em.GetComponent<NewsroomComponent>(terminal);
                component.NeedsPower = false;
                var system = em.System<NewsroomSystem>();
                using var stream = pair.Server.ResolveDependency<IResourceManager>().ContentFileRead(new ResPath("/Textures/_radiant/Objects/Medical/autopsy.rsi/autopsy_scanner.png"));
                using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                Assert.That(NewsPhotoRules.Valid(bytes), Is.True);
                Assert.That(NewsPhotoRules.Valid(new byte[100]), Is.False);
                var oversized = (byte[]) bytes.Clone();
                oversized[16] = 0x7f;
                Assert.That(NewsPhotoRules.Valid(oversized), Is.False);
                using (var image = Image.Load(bytes))
                using (var jpeg = new MemoryStream())
                {
                    image.SaveAsJpeg(jpeg);
                    bytes = jpeg.ToArray();
                }
                Assert.That(NewsPhotoRules.Valid(bytes), Is.True, "The camera's JPEG fallback must be accepted too.");
                Assert.That(NewsPhotoRules.Valid(bytes.Take(24).ToArray()), Is.False);
                var photo = em.SpawnEntity("PhotoCard", map.GridCoords);
                em.GetComponent<PhotoCardComponent>(photo).ImageData = bytes;
                var insert = new InteractUsingEvent(user, photo, terminal, map.GridCoords);
                em.EventBus.RaiseLocalEvent(terminal, insert);
                Assert.That(insert.Handled, Is.True); Assert.That(component.AttachedPhoto, Is.EqualTo(bytes));
                Assert.That(system.TryPublish((terminal, component), user, "Артель", "Мастерская открыта", "Принимаем заказы на скульптуру."), Is.True);
                Assert.That(system.TryPublish((terminal, component), user, "Артель", "Повтор", "Повтор"), Is.False);
                Assert.That(SectorNewsComponent.Articles, Has.Count.EqualTo(1));
                Assert.That(SectorNewsComponent.Articles[0].Channel, Is.EqualTo("Артель"));
                var stock = em.System<SharedMaterialStorageSystem>().GetMaterialAmount(terminal, "Paper");
                var printed = system.TryPrint((terminal, component), user);
                Assert.That(printed, Is.Not.Null);
                Assert.That(system.TryPrint((terminal, component), user), Is.Null, "Rapid presses must not print extra copies.");
                Assert.That(em.System<SharedMaterialStorageSystem>().GetMaterialAmount(terminal, "Paper"), Is.EqualTo(stock - 100));
                var edition = em.GetComponent<NewsEditionComponent>(printed!.Value);
                Assert.That(edition.Articles, Has.Count.EqualTo(1));
                Assert.That(edition.Articles[0].Photo, Is.EqualTo(bytes));
                var snapshot = edition.Articles[0];
                preview = new NewsroomUiState(new() { snapshot }, snapshot, "Артель", false, 9, true, false, false, "");
                bytes[0] = 0;
                SectorNewsComponent.Articles.Clear();
                Assert.That(NewsPhotoRules.Valid(edition.Articles[0].Photo), Is.True,
                    "A printed issue must retain its independent photograph even after the source and feed change.");
                Assert.That(em.GetComponent<PaperComponent>(printed.Value).Content, Does.Contain("Мастерская открыта"));
            });
            await pair.Client.WaitAssertion(() =>
            {
                var clientEm = pair.Client.EntMan;
                var cache = pair.Client.ResolveDependency<IResourceCache>();
                foreach (var prototype in new[] { "RadiantNewspaper", "RadiantAutopsyScanner", "RadiantSculptingChisel", "RadiantSculptureBlank" })
                {
                    var item = clientEm.SpawnEntity(prototype, Robust.Shared.Map.MapCoordinates.Nullspace);
                    foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
                    {
                        var held = new GetInhandVisualsEvent(item, hand);
                        clientEm.EventBus.RaiseLocalEvent(item, held);
                        Assert.That(held.Layers, Has.Count.EqualTo(1), prototype);
                        var layer = held.Layers.Single().Item2;
                        Assert.That(layer.RsiPath, Is.Not.Null, prototype);
                        var rsi = cache.GetResource<RSIResource>(new ResPath("/Textures") / layer.RsiPath!).RSI;
                        Assert.That(rsi.TryGetState(layer.State!, out _), Is.True, $"{prototype}: {hand}");
                    }
                    clientEm.DeleteEntity(item);
                }
                using var window = new NewsroomWindow(); window.OpenCentered(); window.UpdateState(preview!);
                var controls = Descendants(window).ToArray();
                var title = controls.OfType<LineEdit>().Single(c => c.Name == "NewsTitle");
                var body = controls.OfType<TextEdit>().Single(c => c.Name == "NewsBody");
                title.Text = "Следующая статья"; body.TextRope = new Rope.Leaf("Черновик");
                window.UpdateState(preview!);
                Assert.That(title.Text, Is.EqualTo("Следующая статья"));
                Assert.That(Rope.Collapse(body.TextRope), Is.EqualTo("Черновик"), "Feed refreshes must not erase a draft.");
                window.Measure(new Vector2(900, 740)); window.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(900, 740)));
                Assert.That(body.Size.X, Is.GreaterThan(300));
                Assert.That(Descendants(window).OfType<TextureRect>().Any(c => c.Visible && c.Texture != null), Is.True,
                    "The selected article must show the actual photograph.");
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task AutopsyRequiresTrainingAndSurgeryPreservesChemistryAndRecordsOnlyItsExaminedPatient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            EntityUid doctor = default, patient = default, scanner = default;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                doctor = em.SpawnEntity("MobHuman", map.GridCoords);
                patient = em.SpawnEntity("MobHuman", map.GridCoords);
                scanner = em.SpawnEntity("RadiantAutopsyScanner", map.GridCoords);
                Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(doctor, scanner, animate: false), Is.True);
                var skills = em.EnsureComponent<ProfessionalSkillsComponent>(doctor);
                skills.Levels[(int) ProfessionalSkill.Medicine] = 1;
                var chemicals = em.System<SharedSolutionContainerSystem>();
                Assert.That(chemicals.TryGetSolution(patient, BloodstreamComponent.DefaultChemicalsSolutionName, out var solution, out _), Is.True);
                chemicals.TryAddReagent(solution!.Value, "Toxin", 5);
                em.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 300), true);
                em.System<MobStateSystem>().ChangeMobState(patient, MobState.Dead);
                var system = em.System<AutopsySystem>();
                var data = em.GetComponent<AutopsyScannerComponent>(scanner); data.ScanSeconds = .05f;
                Assert.That(system.StartScan((scanner, data), doctor, patient, false), Is.False);
                skills.Levels[(int) ProfessionalSkill.Medicine] = 2;
                Assert.That(system.StartScan((scanner, data), doctor, patient, false), Is.True);
            });
            await pair.RunTicksSync(20);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var data = em.GetComponent<AutopsyScannerComponent>(scanner);
                Assert.That(data.Busy, Is.False); Assert.That(data.Report, Is.Not.Empty);
                Assert.That(data.Target, Is.EqualTo(patient));
                var system = em.System<AutopsySystem>();
                em.GetComponent<ProfessionalSkillsComponent>(doctor).Levels[(int) ProfessionalSkill.Medicine] = 3;
                Assert.That(system.StartScan((scanner, data), doctor, patient, true), Is.False, "A scanner must not replace surgical access.");
                var torso = em.System<SharedBodySystem>().GetBodyChildren(patient).First(p => p.Component.PartType == BodyPartType.Torso).Id;
                var cavity = em.EnsureComponent<SurgicalCavityStateComponent>(torso);
                typeof(SurgicalCavityStateComponent).GetField(nameof(SurgicalCavityStateComponent.RibcageOpen))!.SetValue(cavity, true);
                Assert.That(system.HasSurgicalAccess(patient), Is.True);
                var partial = system.BuildReport(patient, doctor, true);
                Assert.That(partial, Does.Contain(Loc.GetString("autopsy-unidentified-trace")));
                em.GetComponent<ProfessionalSkillsComponent>(doctor).Levels[(int) ProfessionalSkill.Medicine] = 4;
                var detailed = system.BuildReport(patient, doctor, true);
                Assert.That(detailed, Does.Contain(pair.Server.ProtoMan.Index<ReagentPrototype>("Toxin").LocalizedName));
                Assert.That(detailed, Does.Not.Contain(Loc.GetString("autopsy-unidentified-trace")));
                Assert.That(system.StartScan((scanner, data), doctor, patient, true), Is.True);
            });
            await pair.RunTicksSync(20);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var data = em.GetComponent<AutopsyScannerComponent>(scanner);
                Assert.That(data.Report, Does.Contain(Loc.GetString("autopsy-detailed")));
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var holder = em.EnsureComponent<DossierHolderComponent>(patient);
                holder.UserId = session.UserId; holder.Slot = 0; holder.Loaded = true;
                em.EnsureComponent<Content.Shared.Access.Components.AccessComponent>(doctor);
                em.System<SharedAccessSystem>().TrySetTags(doctor, new Robust.Shared.Prototypes.ProtoId<AccessLevelPrototype>[] { "MedicalRecords" });
                var action = new AutopsyActionMessage(AutopsyAction.Record) { Actor = doctor, UiKey = AutopsyUiKey.Key };
                em.EventBus.RaiseLocalEvent(scanner, action);
                var notes = holder.Record.MedicalNotes;
                Assert.That(notes, Does.Contain(data.Summary));
                em.EventBus.RaiseLocalEvent(scanner, new AutopsyActionMessage(AutopsyAction.Record) { Actor = doctor, UiKey = AutopsyUiKey.Key });
                Assert.That(holder.Record.MedicalNotes, Is.EqualTo(notes), "Rapid repeated presses must not duplicate a record.");
                em.EnsureComponent<RottingComponent>(patient);
                Assert.That(em.System<AutopsySystem>().BuildReport(patient, doctor, true), Does.Contain(Loc.GetString("autopsy-traces-lost")));
                var stock = em.System<SharedMaterialStorageSystem>().GetMaterialAmount(scanner, "Paper");
                em.EventBus.RaiseLocalEvent(scanner, new AutopsyActionMessage(AutopsyAction.Print) { Actor = doctor, UiKey = AutopsyUiKey.Key });
                Assert.That(em.System<SharedMaterialStorageSystem>().GetMaterialAmount(scanner, "Paper"), Is.EqualTo(stock - 100));
            });
            await pair.RunTicksSync(20);
        }
        finally { await pair.CleanReturnAsync(); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
