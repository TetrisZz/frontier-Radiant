using Content.Shared.Hands.Components;
using Content.Shared.Paper;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PaperWritingPermissionTest
{
    [Test]
    public async Task SavingRequiresAHeldWritingToolAndHonorsPaperProtection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var paper = em.SpawnEntity("Paper", map.GridCoords);
            var pen = em.SpawnEntity("Pen", map.GridCoords);
            var system = em.System<PaperSystem>();
            var hands = em.System<ServerHands>();
            hands.AddHand(user, "paper-test", HandLocation.Right);
            hands.AddHand(user, "paper-other", HandLocation.Left);
            hands.SetActiveHand(user, "paper-test");
            var component = em.GetComponent<PaperComponent>(paper);
            system.SetContent(paper, "Name: [form]");
            void Write(string text)
            {
                var message = new PaperComponent.PaperInputTextMessage(text)
                    { Actor = user, UiKey = PaperComponent.PaperUiKey.Key };
                em.EventBus.RaiseLocalEvent(paper, message);
            }

            Assert.That(system.CanWrite(paper, user), Is.False, "A pen on the floor is not a held tool.");
            Write("Forged without pen");
            Assert.That(component.Content, Is.EqualTo("Name: [form]"));
            Assert.That(hands.TryPickup(user, pen, "paper-test", checkActionBlocker: false, animate: false), Is.True);
            Assert.That(system.CanWrite(paper, user), Is.True);
            Write("Name: Test");
            Assert.That(component.Content, Is.EqualTo("Name: Test"));
            // Keeping the pen elsewhere in the inventory must not count as holding it.
            Assert.That(hands.TryDrop(user, pen), Is.True);
            Write("Changed after dropping pen");
            Assert.That(component.Content, Is.EqualTo("Name: Test"));
            Assert.That(hands.TryPickup(user, pen, "paper-test", checkActionBlocker: false, animate: false), Is.True);
            component.EditingDisabled = true;
            Write("Tampered protected document");
            Assert.That(component.Content, Is.EqualTo("Name: Test"));
            component.EditingDisabled = false;
            component.StampedBy.Add(new StampDisplayInfo { StampedName = "Test" });
            Write("Tampered stamped document");
            Assert.That(component.Content, Is.EqualTo("Name: Test"));
            em.System<TagSystem>().AddTag(pen, "WriteIgnoreStamps");
            Write("Authorized stamped edit");
            Assert.That(component.Content, Is.EqualTo("Authorized stamped edit"));
            em.System<TagSystem>().RemoveTag(pen, "WriteIgnoreStamps");
            component.StampedBy.Clear();
            // The pen can be in the other hand, not necessarily the selected one.
            hands.SetActiveHand(user, "paper-other");
            Assert.That(system.CanWrite(paper, user), Is.True);

            paper = em.SpawnEntity("RadiantPrintedDossier", map.GridCoords);
            component = em.GetComponent<PaperComponent>(paper);
            system.SetContent(paper, "Immutable template: [form]");
            Assert.That(system.CanWrite(paper, user), Is.False);
            Assert.That(system.CanWrite(paper, user, fillingForm: true), Is.True);
            Write("Immutable template: Filled");
            Assert.That(component.Content, Is.EqualTo("Immutable template: Filled"));
            Write("Changed the dossier template");
            Assert.That(component.Content, Is.EqualTo("Immutable template: Filled"));
        });
        TestContext.Out.WriteLine("All paper writing permission assertions passed.");
        await pair.CleanReturnAsync();
    }
}
