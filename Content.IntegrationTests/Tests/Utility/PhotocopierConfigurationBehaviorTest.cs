using System;
using System.Linq;
using Content.Server._radiant.Paperwork;
using Content.Server.Power.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Paper;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PhotocopierConfigurationBehaviorTest
{
    [Test]
    public async Task CatalogControlsPrintingAndAnimationFollowsSuccessfulPoweredJobs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var system = em.System<PhotocopierSystem>();
            var appearance = em.System<SharedAppearanceSystem>();
            var ui = em.System<UserInterfaceSystem>();
            var copier = em.SpawnEntity("Photocopier", map.GridCoords);
            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var configuration = em.GetComponent<PhotocopierComponent>(copier);
            var power = em.GetComponent<ApcPowerReceiverComponent>(copier);

            bool Printing()
            {
                Assert.That(appearance.TryGetData<bool>(copier, PhotocopierVisuals.Printing, out var printing), Is.True);
                return printing;
            }
            PhotocopierUiState State()
            {
                Assert.That(ui.TryGetUiState<PhotocopierUiState>(copier, PhotocopierUiKey.Key, out var state), Is.True);
                return state!;
            }
            void Print(string? form, int copies = 1)
                => em.EventBus.RaiseLocalEvent(copier, new PhotocopierPrintMessage(form, copies)
                    { Actor = user, UiKey = PhotocopierUiKey.Key });
            void CompleteJob()
            {
                configuration.ReadyAt = timing.CurTime;
                system.Update(0);
                Assert.That(Printing(), Is.False);
            }

            Assert.That(Printing(), Is.False);
            Assert.That(State().Forms.Select(form => form.Id), Is.EquivalentTo(configuration.Forms.Select(id => id.Id)));
            Assert.That(State().Forms.Any(form => form.Id.StartsWith("Separatist", StringComparison.Ordinal)), Is.False);
            configuration.Forms = ["GeneralDocument"];
            power.Powered = true;
            Print("SeparatistSaleContract");
            Print("SeparatistLeaseContract");
            Assert.That(configuration.Paper, Is.EqualTo(20), "Hidden templates cannot be printed by forging a message.");
            Assert.That(configuration.Ink, Is.EqualTo(100));
            Assert.That(Printing(), Is.False);

            Print("GeneralDocument");
            Assert.That(Printing(), Is.True);
            Assert.That(configuration.ReadyAt - timing.CurTime, Is.EqualTo(configuration.PrintDuration));
            Assert.That(State().Ready, Is.False);
            var sheets = em.EntityQuery<PaperComponent>().Count();
            Print("GeneralDocument");
            Assert.That(em.EntityQuery<PaperComponent>().Count(), Is.EqualTo(sheets), "Printing cannot be restarted during an active job.");
            CompleteJob();
            Assert.That(State().Ready, Is.True);

            configuration.Forms = ["SalaryRequest"];
            Print("GeneralDocument");
            Assert.That(configuration.Paper, Is.EqualTo(19));
            Print("SalaryRequest");
            Assert.That(configuration.Paper, Is.EqualTo(18));
            Assert.That(State().Forms.Select(form => form.Id), Is.EqualTo(new[] { "SalaryRequest" }));
            CompleteJob();

            configuration.Forms.Clear();
            Print("SalaryRequest");
            Assert.That(configuration.Paper, Is.EqualTo(18));
            var original = em.SpawnEntity("Paper", map.GridCoords);
            em.System<PaperSystem>().SetContent(original, "Original content");
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(copier, "original", original, null), Is.True);
            Assert.That(State().Forms, Is.Empty);
            Print(null, 3);
            Assert.That(configuration.Paper, Is.EqualTo(15));
            Assert.That(configuration.Ink, Is.EqualTo(95));
            Assert.That(configuration.ReadyAt - timing.CurTime, Is.EqualTo(configuration.PrintDuration * 3));
            Assert.That(Printing(), Is.True);
            Assert.That(em.EntityQuery<PaperComponent>().Count(paper => paper.Content == "Original content"), Is.EqualTo(4));

            power.Powered = false;
            var off = new PowerChangedEvent(false, 0);
            em.EventBus.RaiseLocalEvent(copier, ref off);
            Assert.That(Printing(), Is.False, "An unpowered copier cannot display a running scanner.");
            Assert.That(State().Ready, Is.False);
            power.Powered = true;
            var on = new PowerChangedEvent(true, power.Load);
            em.EventBus.RaiseLocalEvent(copier, ref on);
            Assert.That(Printing(), Is.True);
            CompleteJob();
        });
        TestContext.Out.WriteLine("All photocopier catalog, copying, power, and animation assertions passed.");
        await pair.CleanReturnAsync();
    }
}
