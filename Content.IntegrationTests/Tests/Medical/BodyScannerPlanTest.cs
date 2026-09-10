using System.Linq;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class BodyScannerPlanTest
{
    [Test]
    public async Task ScannerPlanTracksPatientAndSeparateCavities()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IMapManager>().CreateMap();
            var coords = new MapCoordinates(0, 0, map);
            var patient = entities.SpawnEntity("MobHuman", coords);
            var other = entities.SpawnEntity("MobHuman", coords);
            var scanner = entities.System<BodyScannerSystem>();
            var body = entities.System<SharedBodySystem>();
            var torso = body.GetBodyChildren(patient).Single(p => p.Component.PartType == BodyPartType.Torso).Id;
            var foot = body.GetBodyChildren(patient).First(p => p.Component.PartType == BodyPartType.Foot).Id;
            Assert.That(scanner.BuildOperationPlan(patient), Is.Empty);

            void Cavity(string name, bool open)
            {
                var verb = open ? "Open" : "Close";
                var id = $"SurgeryStep{verb}{name}{(open ? "State" : "Incision")}";
                var step = entities.SpawnEntity(id, coords);
                var ev = new SurgeryStepEvent(patient, patient, torso, new())
                { StepProto = id, SurgeryProto = $"Surgery{verb}{name}" };
                entities.EventBus.RaiseLocalEvent(step, ref ev);
            }
            string Text(string action, string site) => loc.GetString($"body-scanner-plan-{action}",
                ("part", loc.GetString($"surgical-site-{site}")));
            bool Contains(string text) => scanner.BuildOperationPlan(patient).Any(entry => entry.Text == text);

            Cavity("Ribcage", true);
            Cavity("Groin", true);
            Assert.That(Contains(Text("close", "ribcage")), Is.True);
            Assert.That(Contains(Text("close", "groin")), Is.True);
            Assert.That(Contains(Text("close", "abdomen")), Is.False);
            Assert.That(scanner.BuildOperationPlan(patient).Count(entry => entry.Text == loc.GetString(
                "body-scanner-plan-close", ("part", loc.GetString("health-analyzer-part-torso")))), Is.Zero);
            Cavity("Groin", false);
            Assert.That(Contains(Text("close", "groin")), Is.False);
            Assert.That(Contains(Text("close", "ribcage")), Is.True);

            var state = entities.EnsureComponent<SurgicalSterilityComponent>(torso);
            state.Contamination[SurgicalSite.Ribcage] = 4;
            Assert.That(Contains(Text("clean", "ribcage")), Is.True);
            Cavity("Ribcage", false);
            Assert.That(Contains(Text("clean", "ribcage")), Is.False);
            Assert.That(Contains(Text("observe", "ribcage")), Is.True);
            state.Infection[SurgicalSite.Ribcage] = 10;
            Assert.That(Contains(Text("infection", "ribcage")), Is.True);
            state.NecrosisSeconds[SurgicalSite.Ribcage] = 60;
            Assert.That(Contains(Text("debride", "ribcage")), Is.True);
            state.Contamination.Clear();
            state.Infection.Clear();
            state.NecrosisSeconds.Clear();

            var glass = entities.EnsureComponent<EmbeddedGlassComponent>(foot);
            glass.Fragments.Add("ShardGlass");
            Assert.That(scanner.BuildOperationPlan(patient), Is.Not.Empty);
            Assert.That(scanner.BuildOperationPlan(other), Is.Empty, "Plans must not leak between patients.");
            glass.Fragments.Clear();
            Assert.That(scanner.BuildOperationPlan(patient), Is.Empty, "Resolved tasks should disappear.");
        });
    }
}
