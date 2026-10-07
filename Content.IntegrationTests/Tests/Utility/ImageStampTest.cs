using System.Linq;
using System.IO;
using System.Numerics;
using Content.Client.Paper.UI;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Client;
using Robust.Shared;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ImageStampTest
{
    [Test]
    public async Task IllustratedStampsApplyCopyAndDisplayWithoutReplacingOrdinaryStamps()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        // A freshly generated server starts at time zero, equal to a new stamp's initial delay end.
        await server.WaitRunTicks(1);
        var entities = server.ResolveDependency<IEntityManager>();
        var impressions = new System.Collections.Generic.List<StampDisplayInfo>();
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var user = entities.SpawnEntity("MobHuman", coordinates);
            var system = entities.System<PaperSystem>();
            var appearance = entities.System<SharedAppearanceSystem>();
            foreach (var suffix in new[] { "Feline", "Horny", "CentCom", "Clown", "Denied", "Mime", "Granted", "Pepe" })
            {
                var stamp = entities.SpawnEntity("RadiantRubberStamp" + suffix, coordinates);
                var stampComp = entities.GetComponent<StampComponent>(stamp);
                var paper = entities.SpawnEntity("Paper", coordinates);
                entities.EventBus.RaiseLocalEvent(paper, new InteractUsingEvent(user, stamp, paper, coordinates));
                var paperComp = entities.GetComponent<PaperComponent>(paper);
                var impression = paperComp.StampedBy.Single();
                Assert.That(impression.StampSprite, Is.EqualTo(stampComp.StampSprite).And.Not.Null);
                Assert.That(paperComp.StampState, Is.EqualTo(stampComp.StampState));
                Assert.That(paperComp.StampRsiPath, Is.EqualTo(stampComp.StampRsiPath));
                Assert.That(appearance.TryGetData(paper, PaperComponent.PaperVisuals.StampRsi, out string rsi), Is.True);
                Assert.That(rsi, Is.EqualTo(stampComp.StampRsiPath));
                var copy = entities.SpawnEntity("Paper", coordinates);
                system.CopyStamps(paper, copy);
                var copyComp = entities.GetComponent<PaperComponent>(copy);
                Assert.That(copyComp.StampedBy.Single().StampSprite, Is.EqualTo(impression.StampSprite));
                Assert.That(copyComp.StampRsiPath, Is.EqualTo(stampComp.StampRsiPath));
                impressions.Add(impression);
            }

            var regular = entities.SpawnEntity("RubberStampApproved", coordinates);
            var regularPaper = entities.SpawnEntity("Paper", coordinates);
            entities.EventBus.RaiseLocalEvent(regularPaper, new InteractUsingEvent(user, regular, regularPaper, coordinates));
            var regularComp = entities.GetComponent<PaperComponent>(regularPaper);
            Assert.That(regularComp.StampedBy.Single().StampSprite, Is.Null);
            Assert.That(regularComp.StampRsiPath, Is.Null);
            impressions.Add(regularComp.StampedBy.Single());
            entities.DeleteEntity(map);
        });
        // Isolate this UI check from unrelated missing translations in the full lobby.
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/ImageStampTestPrototypes"),
                MountOptions = new MountOptions
                {
                    DirMounts = new() { Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources")) }
                }
            }
        };
        options.BeforeStart += () =>
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.DoAutoRegistrations();
            factory.GenerateNetIds();
        };
        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await client.WaitAssertion(() =>
        {
            var prototypes = IoCManager.Resolve<IPrototypeManager>();
            prototypes.LoadString("- type: shader\n  id: PaperStamp\n  kind: source\n  path: /Textures/Shaders/paperstamp.swsl\n");
            prototypes.ResolveResults();
            using var widget = new StampWidget();
            foreach (var impression in impressions)
            {
                widget.StampInfo = impression;
                widget.Measure(new Vector2(400, 200));
                var illustrated = impression.StampSprite != null;
                Assert.That(widget.FindControl<StampLabel>("StampedByLabel").Visible, Is.EqualTo(!illustrated));
                Assert.That(widget.PanelOverride == null, Is.EqualTo(illustrated));
                if (illustrated)
                {
                    Assert.That(widget.DesiredSize.X, Is.InRange(242f, 270f));
                    Assert.That(widget.DesiredSize.Y, Is.InRange(108f, 135f));
                }
            }
            using var collection = new StampCollection();
            foreach (var impression in impressions)
                collection.AddStamp(new StampWidget { StampInfo = impression });
            foreach (var size in new[] { new Vector2(480, 150), new Vector2(200, 150) })
            {
                collection.Measure(size);
                collection.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                foreach (var stamp in collection.Children.Cast<StampWidget>())
                {
                    var centre = stamp.GlobalPosition - collection.GlobalPosition + stamp.Size * 0.5f;
                    var half = stamp.Size * 0.5f;
                    var cos = MathF.Abs(MathF.Cos(stamp.Orientation));
                    var sin = MathF.Abs(MathF.Sin(stamp.Orientation));
                    var rotatedHalf = new Vector2(half.X * cos + half.Y * sin, half.X * sin + half.Y * cos);
                    Assert.That(centre.X - rotatedHalf.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(centre.Y - rotatedHalf.Y, Is.GreaterThanOrEqualTo(0));
                    Assert.That(centre.X + rotatedHalf.X, Is.LessThanOrEqualTo(size.X));
                    Assert.That(centre.Y + rotatedHalf.Y, Is.LessThanOrEqualTo(size.Y));
                }
            }
            collection.RemoveStamps();
            Assert.That(collection.ChildCount, Is.Zero, "Updating a sheet must remove its old impressions");
        });
    }
}
