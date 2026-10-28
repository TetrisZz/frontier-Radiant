using System;
using System.Linq;
using Content.Server._radiant.Paperwork;
using Content.Server.Power.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Paper;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PhotocopierBodyPrintTest
{
    [Test]
    public async Task BodyCopiesConsumeSuppliesAndKeepTheirImageWhenCopied()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var system = em.System<PhotocopierSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var copier = em.SpawnEntity("Photocopier", map.GridCoords);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var configuration = em.GetComponent<PhotocopierComponent>(copier);
            var scanner = em.GetComponent<PhotocopierBodyScannerComponent>(copier);
            var power = em.GetComponent<ApcPowerReceiverComponent>(copier);
            scanner.Delay = TimeSpan.Zero;
            int Prints() => em.EntityQuery<BodyPhotocopyComponent>().Count();
            void CompleteJob()
            {
                configuration.ReadyAt = timing.CurTime;
                system.Update(0);
            }

            Assert.That(system.TryStartBodyPrint(copier, user), Is.False, "An unpowered copier cannot scan.");
            power.Powered = true;
            configuration.Paper = 0;
            Assert.That(system.TryStartBodyPrint(copier, user), Is.False);
            configuration.Paper = 20;
            configuration.Ink = 0;
            Assert.That(system.TryStartBodyPrint(copier, user), Is.False);
            configuration.Ink = 100;
            Assert.That(Prints(), Is.Zero);

            var appearance = em.GetComponent<HumanoidAppearanceComponent>(user);
            foreach (var (species, sex, expected) in new[]
            {
                ("Human", Sex.Male, "PaperBodyPrintMale"),
                ("Human", Sex.Female, "PaperBodyPrintFemale"),
                ("Reptilian", Sex.Female, "PaperBodyPrintLizard"),
                ("SlimePerson", Sex.Male, "PaperBodyPrintSlime"),
                ("Felinid", Sex.Male, "PaperBodyPrintCat"),
                ("Vulpkanin", Sex.Female, "PaperBodyPrintFluffy"),
            })
            {
                appearance.Species = species;
                appearance.Sex = sex;
                var before = Prints();
                Assert.That(system.TryStartBodyPrint(copier, user), Is.True);
                Assert.That(Prints(), Is.EqualTo(before + 1));
                Assert.That(em.EntityQuery<BodyPhotocopyComponent>().Count(print =>
                    em.GetComponent<MetaDataComponent>(print.Owner).EntityPrototype?.ID == expected), Is.EqualTo(1));
                Assert.That(system.TryStartBodyPrint(copier, user), Is.False, "An active print job blocks scans.");
                CompleteJob();
            }
            Assert.That(configuration.Paper, Is.EqualTo(14));
            Assert.That(configuration.Ink, Is.EqualTo(94));

            var original = em.EntityQuery<BodyPhotocopyComponent>().First().Owner;
            var paper = em.GetComponent<PaperComponent>(original);
            Assert.That(paper.Content, Is.Not.Empty, "The sheet must not be presented as blank paper.");
            paper.Language = "Общегалактический";
            paper.EditingDisabled = true;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(copier, "original", original, null), Is.True);
            Assert.That(system.TryStartBodyPrint(copier, user), Is.False, "An inserted original covers the glass.");
            var sourcePrototype = em.GetComponent<MetaDataComponent>(original).EntityPrototype!.ID;
            var beforeCopies = Prints();
            em.EventBus.RaiseLocalEvent(copier, new PhotocopierPrintMessage(null, 2)
                { Actor = user, UiKey = PhotocopierUiKey.Key });
            Assert.That(Prints(), Is.EqualTo(beforeCopies + 2));
            var copies = em.EntityQuery<BodyPhotocopyComponent>().Where(print => print.Owner != original &&
                em.GetComponent<MetaDataComponent>(print.Owner).EntityPrototype?.ID == sourcePrototype).ToArray();
            Assert.That(copies, Has.Length.EqualTo(2), "The prototype retains the image for copies, faxing, and serialization.");
            foreach (var copy in copies)
            {
                var copyPaper = em.GetComponent<PaperComponent>(copy.Owner);
                Assert.That(copyPaper.Content, Is.EqualTo(paper.Content));
                Assert.That(copyPaper.Language, Is.EqualTo(paper.Language));
                Assert.That(copyPaper.EditingDisabled, Is.True);
            }
            Assert.That(configuration.Paper, Is.EqualTo(12));
            Assert.That(configuration.Ink, Is.EqualTo(92));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelledScansDoNotConsumePaperOrToner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var copier = em.SpawnEntity("Photocopier", map.GridCoords);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(copier).Powered = true;
            Assert.That(em.System<PhotocopierSystem>().TryStartBodyPrint(copier, user), Is.True);
            var action = em.GetComponent<DoAfterComponent>(user).DoAfters.Values.Single();
            Assert.That(action.Args.BreakOnMove, Is.True);
            Assert.That(action.Args.BreakOnDamage, Is.True);
            Assert.That(action.Args.Target, Is.EqualTo(copier));
            em.System<SharedDoAfterSystem>().Cancel(action.Id);
            Assert.That(action.Cancelled, Is.True);
            Assert.That(em.GetComponent<PhotocopierComponent>(copier).Paper, Is.EqualTo(20));
            Assert.That(em.GetComponent<PhotocopierComponent>(copier).Ink, Is.EqualTo(100));
            Assert.That(em.EntityQuery<BodyPhotocopyComponent>(), Is.Empty);

            Assert.That(em.System<PhotocopierSystem>().TryStartBodyPrint(copier, user), Is.True);
            var pending = em.GetComponent<DoAfterComponent>(user).DoAfters.Values.Single(scan => !scan.Cancelled);
            var finished = (PhotocopierBodyScanDoAfterEvent) pending.Args.Event;
            finished.DoAfter = pending;
            em.GetComponent<ApcPowerReceiverComponent>(copier).Powered = false;
            em.EventBus.RaiseLocalEvent(copier, finished);
            Assert.That(em.EntityQuery<BodyPhotocopyComponent>(), Is.Empty, "Power must be rechecked at completion.");
            Assert.That(em.GetComponent<PhotocopierComponent>(copier).Paper, Is.EqualTo(20));
            Assert.That(em.GetComponent<PhotocopierComponent>(copier).Ink, Is.EqualTo(100));
            em.System<SharedDoAfterSystem>().Cancel(pending.Id);

            em.GetComponent<ApcPowerReceiverComponent>(copier).Powered = true;
            var holder = em.SpawnEntity(null, map.GridCoords);
            var containers = em.System<SharedContainerSystem>();
            var container = containers.EnsureContainer<Container>(holder, "body-scan-test");
            Assert.That(containers.Insert(user, container), Is.True);
            Assert.That(em.System<PhotocopierSystem>().TryStartBodyPrint(copier, user), Is.False,
                "Being at the same coordinates inside a closed container does not grant access to the glass.");
        });
        await pair.CleanReturnAsync();
    }
}
