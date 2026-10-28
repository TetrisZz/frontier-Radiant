using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._radiant.Medical.Genetics;
using Content.Client._radiant.Medical.Virology;
using Content.Client.Stylesheets;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Medical.Virology;
using Robust.Client.ResourceManagement;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class MedicalLaboratoryLayoutTest
{
    [Test]
    public async Task LaboratoryWindowsFitTheirFramesAndPreserveSelectionsWhileUpdating()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var theme = new StyleNano(pair.Client.ResolveDependency<IResourceCache>()).Stylesheet;
                using var genetics = new GeneticResearchWindow { Stylesheet = theme };
                genetics.Update(new GeneticBodyState
                {
                    Sample = new string('W', 120), Profile = "100 / 100", CanSelect = true, CanAnalyze = true,
                    Genes = { ["RadiantRegeneration"] = "Известный комплекс восстановления тканей" },
                    SelectedGene = "RadiantRegeneration", SequencePattern = "AT??GC",
                    Products = { GeneticProduct.Regeneration }, Selected = GeneticProduct.Regeneration,
                    TherapyDetails = Loc.GetString("genetics-variant-help-standard"),
                });
                var analyze = Descendants(genetics).OfType<Button>().Single(b => b.Text == Loc.GetString("genetics-ui-analyze"));
                var eject = Descendants(genetics).OfType<Button>().Single(b => b.Text == Loc.GetString("genetics-eject"));
                Assert.That(analyze.Disabled, Is.False);
                Assert.That(eject.Disabled, Is.True);
                Assert.That(analyze.ModulateSelfOverride, Is.EqualTo(Color.White), "The global theme must not darken the custom palette.");
                static float Brightness(Color color) => color.R * .2126f + color.G * .7152f + color.B * .0722f;
                var availableColor = ((StyleBoxFlat) analyze.StyleBoxOverride!).BackgroundColor;
                var disabled = ((StyleBoxFlat) eject.StyleBoxOverride!).BackgroundColor;
                Assert.That(Brightness(availableColor) - Brightness(disabled), Is.GreaterThan(.2f));
                analyze.ToggleMode = true;
                analyze.Pressed = true;
                var pressed = ((StyleBoxFlat) analyze.StyleBoxOverride!).BackgroundColor;
                Assert.That(Brightness(pressed), Is.LessThan(Brightness(availableColor) * .65f), "Pressing must darken an available button.");
                analyze.Pressed = false;
                using var virology = new VirologyWindow { Stylesheet = theme };
                var state = new VirologyState
                {
                    Producer = true, CanDesign = true, CanCulture = true, CanEject = true,
                    Symptoms = { ["Fatigue"] = "Утомляемость · 1\n" + new string('W', 160) },
                    Diseases = { ["Hematic~Fatigue"] = "Гематическая инфекция с дополнительными проявлениями" },
                    Draft = { "Fatigue" }, Selected = "Hematic~Fatigue",
                    Design = Loc.GetString("virology-design-treatment"),
                };
                virology.Update(state);
                var checkbox = Descendants(virology).OfType<CheckBox>().Single();
                virology.Update(state);
                Assert.That(Descendants(virology).OfType<CheckBox>().Single(), Is.SameAs(checkbox));
                Assert.That(checkbox.Pressed, Is.True);
                using var vault = new DnaVaultWindow { Stylesheet = theme };
                vault.Update(new DnaVaultState
                {
                    Sample = new string('W', 160), HumanGoal = 3, PlantGoal = 3, AnimalGoal = 3,
                    Rewards = { ["GeneticRegenerationInjector"] = "Восстановление тканей\nНагрузка: 55 / 100" },
                });
                foreach (var window in new DefaultWindow[] { genetics, virology, vault })
                foreach (var size in new[] { new Vector2(640, 600), new Vector2(900, 780) })
                {
                    window.SetSize = size;
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X), window.GetType().Name);
                    Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(size.Y), window.GetType().Name);
                }
                var rsi = pair.Client.ResolveDependency<IResourceCache>().GetResource<RSIResource>(
                    new ResPath("/Textures/_radiant/Structures/Machines/dna_vault.rsi")).RSI;
                Assert.That(rsi.TryGetState("vault", out var active), Is.True);
                Assert.That(active!.IsAnimated, Is.True);
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
