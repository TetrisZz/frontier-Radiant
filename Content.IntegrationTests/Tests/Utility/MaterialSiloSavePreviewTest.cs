using System.Globalization;
using System.Numerics;
using Content.Client.Materials.UI;
using Content.Client.Stylesheets;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class MaterialSiloSavePreviewTest
{
    [Test]
    public async Task SiloShowsRetainedAmountsAndFitsAtMinimumWidth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.EntMan;
                var silo = entities.SpawnEntity("MachineMaterialSilo", MapCoordinates.Nullspace);
                var lathe = entities.SpawnEntity("Autolathe", MapCoordinates.Nullspace);
                var loc = IoCManager.Resolve<ILocalizationManager>();
                foreach (var locale in new[] { "ru-RU", "en-US" })
                {
                    var culture = CultureInfo.GetCultureInfo(locale);
                    if (!loc.HasCulture(culture))
                        loc.LoadCulture(culture);
                    loc.DefaultCulture = culture;
                    using var window = new OreSiloMenu
                    {
                        Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet,
                    };
                    window.SetEntity(silo);
                    Assert.That(window.FindControl<BoxContainer>("SaveLegend").Visible, Is.True);
                    var materialList = window.FindControl<MaterialStorageControl>("Materials")
                        .FindControl<BoxContainer>("MaterialList");
                    materialList.RemoveAllChildren();
                    var glass = new MaterialDisplay(silo, "Glass", 27600, true);
                    var ore = new MaterialDisplay(silo, "RawDiamond", 1200, true);
                    materialList.AddChild(glass);
                    materialList.AddChild(ore);
                    Assert.That(glass.SavedAmount.Text, Is.EqualTo(Loc.GetString("ship-save-materials-saved-amount", ("amount", 276))));
                    Assert.That(glass.SavedAmount.FontColorOverride, Is.EqualTo(Color.FromHex("#7ED6AD")));
                    glass.UpdateVolume(50200);
                    Assert.That(glass.SavedAmount.Text, Is.EqualTo(Loc.GetString("ship-save-materials-saved-amount", ("amount", 300))));
                    Assert.That(glass.SavedAmount.FontColorOverride, Is.EqualTo(Color.FromHex("#EF8D92")));
                    Assert.That(ore.SavedAmount.Text, Is.EqualTo(Loc.GetString("ship-save-materials-saved-amount", ("amount", 10))));

                    glass.UpdateVolume(28095);
                    Assert.That(glass.SavedAmount.Text, Is.EqualTo(locale == "ru-RU" ? "280,95" : "280.95"));
                    ore.UpdateVolume(604);
                    Assert.That(ore.SavedAmount.Text, Is.EqualTo(locale == "ru-RU" ? "6,04" : "6.04"));
                    glass.UpdateVolume(50205);
                    Assert.That(glass.SavedAmount.Text, Is.EqualTo("300"), "Whole counts should not acquire trailing zeros.");
                    Assert.That(glass.SavedAmount.ToolTip, Does.Contain(locale == "ru-RU" ? "202,05" : "202.05"));
                    Assert.That(glass.SavedAmount.ToolTip, Does.Not.Contain("202.050"));

                    foreach (var size in new[] { new Vector2(560, 420), new Vector2(620, 520) })
                    {
                        window.SetSize = size;
                        window.Measure(size);
                        window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                        Assert.That(glass.ProductName.Width, Is.GreaterThan(100));
                        Assert.That(glass.SavedAmount.Width, Is.GreaterThanOrEqualTo(56));
                        Assert.That(glass.SavedAmount.GlobalPosition.X + glass.SavedAmount.Width, Is.LessThanOrEqualTo(size.X));
                    }

                    glass.UpdateVolume(30000);
                    Assert.That(glass.SavedAmount.FontColorOverride, Is.EqualTo(Color.FromHex("#7ED6AD")),
                        "Exactly reaching the limit must not show a loss warning.");
                    using var ordinary = new MaterialDisplay(lathe, "Steel", 50200, true);
                    Assert.That(ordinary.SavedAmount.Visible, Is.False, "Ordinary lathes must not promise silo persistence.");
                }
                entities.DeleteEntity(silo);
                entities.DeleteEntity(lathe);
            });
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine($"Silo preview failure: {exception}");
            throw;
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
