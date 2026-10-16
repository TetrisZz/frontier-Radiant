using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._radiant.Storage;
using Content.Server._radiant.Storage;
using Content.Shared._radiant.Storage;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Emag.Systems;
using Content.Shared.Lock;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wall;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class PasswordSafeTest
{
    [Test]
    public async Task SafeConstructionGeneratesGuideAndBuildsThroughFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index<ConstructionPrototype>("PasswordWallSafe");
            var graph = prototypes.Index(recipe.Graph);
            var initial = graph.Edge("start", "frame")!;
            Assert.That(initial.Steps, Has.Count.EqualTo(1));
            Assert.That(initial.Steps.All(step => step is EntityInsertConstructionGraphStep), Is.True,
                "Initial placement cannot use tools");
            var finishing = graph.Edge("frame", "safe")!;
            Assert.That(finishing.Steps, Has.Count.EqualTo(4));
            Assert.That(finishing.Steps.Last(), Is.TypeOf<ToolConstructionGraphStep>());
            var board = entities.SpawnEntity("PasswordWallSafeCircuitboard", map.GridCoords);
            Assert.That(finishing.Steps.OfType<TagConstructionGraphStep>().Single()
                .EntityValid(board, entities, pair.Server.ResolveDependency<IComponentFactory>()), Is.True);

            // Exercise the same guide generator used by construction menu network requests.
            var construction = entities.System<Content.Server.Construction.ConstructionSystem>();
            var getGuide = typeof(Content.Server.Construction.ConstructionSystem)
                .GetMethod("GetGuide", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var guide = (ConstructionGuide?) getGuide.Invoke(construction, new object[] { recipe });
            Assert.That(guide, Is.Not.Null, "The menu must receive a recipe instead of an empty panel");
            Assert.That(guide!.Entries.Count(entry => entry.EntryNumber != null), Is.EqualTo(5));
            Assert.That(guide.Entries.Count(entry => entry.Icon != null), Is.EqualTo(5));

            var frame = entities.SpawnEntity("PasswordWallSafeFrame", map.GridCoords);
            var component = entities.GetComponent<Content.Server.Construction.Components.ConstructionComponent>(frame);
            Assert.That(component.Node, Is.EqualTo("frame"));
            construction.SetPathfindingTarget(frame, "safe", component);
            Assert.That(component.TargetNode, Is.EqualTo("safe"));
            Assert.That(entities.HasComponent<PasswordSafeComponent>(frame), Is.False);
            TestContext.Out.WriteLine("Password safe construction guide and frame assertions completed");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SafeRequiresDigitsKeepsContentsAndCanBeEmagged()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var system = entities.System<PasswordSafeServerSystem>();
                var locks = entities.System<LockSystem>();
                var storage = entities.System<SharedStorageSystem>();
                var appearance = entities.System<SharedAppearanceSystem>();
                var safe = entities.SpawnEntity("PasswordWallSafe", map.GridCoords);
                var component = entities.GetComponent<PasswordSafeComponent>(safe);
                var secret = entities.GetComponent<PasswordSafeCodeComponent>(safe);
                var user = entities.SpawnEntity("MobHuman", map.GridCoords);
                Entity<PasswordSafeComponent> ent = (safe, component);

                Assert.That(entities.HasComponent<WallMountComponent>(safe), Is.True);
                Assert.That(prototypes.Index<ConstructionPrototype>("PasswordWallSafe").Graph.Id, Is.EqualTo("PasswordWallSafe"));
                Assert.That(locks.IsLocked(safe), Is.True);
                Assert.That(locks.TryUnlock(safe, user), Is.False, "Generic lock interaction must not bypass the code");
                var attempt = new BoundUserInterfaceMessageAttempt(user, safe, StorageComponent.StorageUiKey.Key, new OpenBoundInterfaceMessage());
                entities.EventBus.RaiseLocalEvent(safe, attempt);
                Assert.That(attempt.Cancelled, Is.True, "Direct storage UI requests must not bypass the lock");

                foreach (var code in new[] { "", "123", "123456789", "12a4", "１２３４", "12 4" })
                    Assert.That(system.SubmitCode(ent, user, code, code, true), Is.False, code);
                Assert.That(system.SubmitCode(ent, user, "0123", "0124", true), Is.False);
                Assert.That(secret.Code, Is.Null);
                Assert.That(system.SubmitCode(ent, user, "0123", "0123", true), Is.True);
                Assert.That(secret.Code, Is.EqualTo("0123"));
                Assert.That(locks.IsLocked(safe), Is.False);
                var emag = entities.SpawnEntity("Emag", map.GridCoords);
                Assert.That(entities.System<EmagSystem>().TryEmagEffect(emag, user, safe), Is.False,
                    "An open safe does not need hacking");
                var paper = entities.SpawnEntity("Paper", map.GridCoords);
                Assert.That(storage.Insert(safe, paper, out _, playSound: false), Is.True);
                Assert.That(system.CloseSafe(ent, user), Is.True);
                Assert.That(locks.IsLocked(safe), Is.True);
                Assert.That(appearance.TryGetData(safe, PasswordSafeVisuals.State, out PasswordSafeState state), Is.True);
                Assert.That(state, Is.EqualTo(PasswordSafeState.Locking));

                Assert.That(system.SubmitCode(ent, user, "9876", "9876", true), Is.False, "Stale setup window must not change the code");
                Assert.That(secret.Code, Is.EqualTo("0123"));
                secret.NextAttempt = TimeSpan.Zero;
                Assert.That(system.SubmitCode(ent, user, "9876", "", false), Is.False);
                Assert.That(locks.IsLocked(safe), Is.True);
                secret.NextAttempt = TimeSpan.Zero;
                Assert.That(system.SubmitCode(ent, user, "0123", "", false), Is.True);
                Assert.That(entities.GetComponent<StorageComponent>(safe).StoredItems.ContainsKey(paper), Is.True);
                Assert.That(system.CloseSafe(ent, user), Is.True);

                Assert.That(entities.System<EmagSystem>().TryEmagEffect(emag, user, safe), Is.True);
                Assert.That(component.Broken, Is.True);
                Assert.That(locks.IsLocked(safe), Is.False);
                Assert.That(appearance.TryGetData(safe, PasswordSafeVisuals.State, out state), Is.True);
                Assert.That(state, Is.EqualTo(PasswordSafeState.Sparking));
                Assert.That(system.CloseSafe(ent, user), Is.False);
                Assert.That(entities.System<EmagSystem>().TryEmagEffect(emag, user, safe), Is.False);
                Assert.That(entities.GetComponent<StorageComponent>(safe).StoredItems.ContainsKey(paper), Is.True);
                component.AnimationUntil = TimeSpan.FromTicks(1);
                system.Update(0);
                Assert.That(appearance.TryGetData(safe, PasswordSafeVisuals.State, out state), Is.True);
                Assert.That(state, Is.EqualTo(PasswordSafeState.Open));
                TestContext.Out.WriteLine("Password safe server assertions completed");
            });
            await pair.Client.WaitAssertion(() =>
            {
                using var window = new PasswordSafeWindow();
                window.Update(new PasswordSafeUiState(false, false, false));
                var inputs = Descendants(window).OfType<LineEdit>().ToArray();
                Assert.That(inputs, Has.Length.EqualTo(2));
                Assert.That(inputs[0].IsValid!("12a4"), Is.False);
                Assert.That(inputs[0].IsValid!("0123"), Is.True);
                inputs[0].Text = inputs[1].Text = "0123";
                window.Update(new PasswordSafeUiState(false, false, false));
                Assert.That(inputs[0].Text, Is.EqualTo("0123"), "Unchanged state must not erase typed codes");
                PasswordSafeSubmitMessage? submitted = null;
                window.Submit += message => submitted = message;
                inputs[0].ForceSubmitText();
                Assert.That(submitted, Is.Not.Null);
                Assert.That(submitted!.Code, Is.EqualTo("0123"));
                Assert.That(submitted.Configure, Is.True);
                window.Measure(new Vector2(800, 600));
                window.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(Vector2.Zero, new Vector2(300, 450)));
                window.Update(new PasswordSafeUiState(true, false, false));
                window.Update(new PasswordSafeUiState(true, true, false));
                window.Update(new PasswordSafeUiState(true, true, true));
                TestContext.Out.WriteLine("Password safe UI assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Password safe regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
