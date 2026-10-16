using System.Linq;
using Content.Client.Interaction.Panel.Ui;
using Content.Shared._radiant.ERP;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class InteractionAnatomyPanelTest
{
    [Test]
    public async Task BreastSizeIsHiddenUntilSurgicallyChanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.ResolveDependency<IEntityManager>();
                var dummy = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
                var anatomy = entities.EnsureComponent<AdultAnatomyComponent>(dummy);
                anatomy.AnatomyInitialized = true;
                anatomy.HasBreasts = true;
                anatomy.BreastSize = AdultBreastSize.Large;
                using var panel = new InteractionPanelMenu();
                panel.UpdateUser(dummy);
                var label = panel.UserModel.Children.OfType<Label>().Single();
                Assert.That(label.Text, Does.Contain(Loc.GetString("adult-anatomy-breasts-unsized-player")));
                Assert.That(label.Text, Does.Not.Contain(Loc.GetString("adult-anatomy-size-large")));
                anatomy.BreastSizeSurgicallyChanged = true;
                panel.UpdateUser(dummy);
                Assert.That(label.Text, Does.Contain(Loc.GetString("adult-anatomy-size-large")));
                anatomy.HasBreasts = false;
                panel.UpdateUser(dummy);
                Assert.That(label.Text, Does.Not.Contain(Loc.GetString("adult-anatomy-breasts-unsized-player")));
                entities.DeleteEntity(dummy);
                TestContext.Out.WriteLine("Interaction anatomy panel assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Interaction anatomy panel regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
