using System.Linq;
using Content.Server._NF.Medical;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgicalPrintoutTest
{
    [Test]
    public async Task PrintoutIncludesScannerFindingsAndOmitsEmptySection()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var patient = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 0, 0));
            var scanner = entities.System<BodyScannerSystem>();
            var printer = entities.System<HealthAnalyzerPrinterSystem>();
            Assert.That(printer.ComposeSurgicalReport(patient), Is.Empty);

            var body = entities.System<SharedBodySystem>();
            var torso = body.GetBodyChildren(patient).First(p => p.Component.PartType == BodyPartType.Torso);
            entities.AddComponent<IncisionOpenComponent>(torso.Id);
            var state = entities.AddComponent<SurgicalSterilityComponent>(torso.Id);
            state.Contamination[SurgicalSite.Ribcage] = 25;
            state.Infection[SurgicalSite.Ribcage] = 50;
            state.NecrosisSeconds[SurgicalSite.Ribcage] = 120;
            var findings = scanner.BuildDiagnostics(patient);
            Assert.That(findings.Count, Is.GreaterThanOrEqualTo(4));
            var report = printer.ComposeSurgicalReport(patient);
            foreach (var finding in findings)
                Assert.That(Robust.Shared.Utility.FormattedMessage.RemoveMarkupPermissive(report),
                    Does.Contain(finding.Text));
        });
    }
}
