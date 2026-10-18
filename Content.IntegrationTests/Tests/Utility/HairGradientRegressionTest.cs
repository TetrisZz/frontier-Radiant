using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Humanoid;
using Content.Client.Humanoid;
using Content.Client.Lobby;
using Content.Shared._radiant.Humanoid;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using ServerAppearance = Content.Server.Humanoid.HumanoidAppearanceSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class HairGradientRegressionTest
{
    [Test]
    public async Task ProfilesCopiesHologramsAndDisplacementKeepGradients()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var appearance = entities.System<ServerAppearance>();
                var source = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
                var target = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
                var profile = Profile("Human");
                appearance.LoadProfile(source, profile);
                appearance.CloneAppearance(source, target);
                AssertGradient(entities.GetComponent<HumanoidAppearanceComponent>(target), profile.Appearance);
                entities.GetComponent<HumanoidAppearanceComponent>(target).HairColoringMode = HairColoringMode.Solid;
                var shared = (SharedHumanoidAppearanceSystem) appearance;
                shared.ApplyAppearanceSnapshot(target, entities.GetComponent<HumanoidAppearanceComponent>(source));
                AssertGradient(entities.GetComponent<HumanoidAppearanceComponent>(target), profile.Appearance);
                entities.DeleteEntity(source);
                entities.DeleteEntity(target);
                TestContext.Out.WriteLine("Hair gradient server assertions completed");
            });

            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.ResolveDependency<IEntityManager>();
                var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
                var appearance = entities.System<HumanoidAppearanceSystem>();
                var sprites = entities.System<SpriteSystem>();
                var gradient = entities.System<HairGradientSystem>();
                var marking = prototypes.Index<MarkingPrototype>("HumanHairLong");
                var rsi = (SpriteSpecifier.Rsi) marking.Sprites[0];
                var layerId = $"{marking.ID}-{rsi.RsiState}";
                var bounds = gradient.GetBounds(marking);
                Assert.That(bounds, Is.Not.EqualTo(new Vector4(0, 0, 1, 1)), "Hair bounds must exclude empty padding");
                Assert.That(bounds.Z, Is.GreaterThan(bounds.X));
                Assert.That(bounds.W, Is.GreaterThan(bounds.Y));

                var dummy = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
                var humanoid = entities.GetComponent<HumanoidAppearanceComponent>(dummy);
                var sprite = entities.GetComponent<SpriteComponent>(dummy);
                appearance.LoadProfile(dummy, Profile("Human"));
                AssertHairShader(dummy, sprite);
                humanoid.MarkingsDisplacement[HumanoidVisualLayers.Hair] = new DisplacementData
                {
                    SizeMaps = new Dictionary<int, PrototypeLayerData>
                    {
                        [32] = new() { RsiPath = "Mobs/Species/Vox/displacement.rsi", State = "hair" },
                    },
                };
                appearance.RefreshAppearance(dummy);
                AssertHairShader(dummy, sprite);
                Assert.That(sprites.LayerMapTryGet((dummy, sprite), $"{layerId}-displacement", out _, false), Is.True);
                humanoid.HairColoringMode = HairColoringMode.Solid;
                appearance.RefreshAppearance(dummy);
                Assert.That(sprites.LayerMapTryGet((dummy, sprite), layerId, out var solidIndex, false), Is.True);
                Assert.That(sprite.TryGetLayer(solidIndex, out var solidLayer), Is.True);
                Assert.That(solidLayer.ShaderPrototype, Is.EqualTo("DisplacedDraw"));
                humanoid.MarkingsDisplacement.Clear();
                appearance.LoadProfile(dummy, Profile("SlimePerson"));
                AssertHairShader(dummy, sprite);
                Assert.That(sprites.LayerMapTryGet((dummy, sprite), $"{layerId}-displacement", out _, false), Is.False);
                Assert.That(humanoid.MarkingSet.Markings[MarkingCategories.Hair][0].MarkingColors[0].A,
                    Is.EqualTo(0.75f));

                using var picker = new SingleMarkingPicker { Category = MarkingCategories.Hair };
                picker.UpdateData(new List<Marking> { marking.AsMarking() }, "Human", 1);
                picker.UpdateGradientFromAppearance(Profile("SlimePerson").Appearance, "SlimePerson");
                var panel = picker.FindControl<BoxContainer>("HairColoringContainer")
                    .Children.OfType<GradientHairColorPanel>().Single();
                Assert.That(panel.Visible, Is.True, "Slimes must have gradient controls");
                foreach (var width in new[] { 400f, 640f })
                {
                    picker.InvalidateMeasure();
                    picker.Measure(new Vector2(width, 1100));
                    picker.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(width, 1100)));
                    var colors = picker.FindControl<BoxContainer>("ColorSelectorContainer").Parent!;
                    Assert.That(colors.Size.Y, Is.GreaterThan(100f), "The primary color picker must remain visible");
                    var controls = picker.FindControl<BoxContainer>("HairColoringContainer");
                    Assert.That(controls.Position.Y - (colors.Position.Y + colors.Size.Y), Is.InRange(0f, 12f));
                }
                entities.DeleteEntity(dummy);

                var hologram = entities.SpawnEntity("InterviewHologramContractor", MapCoordinates.Nullspace);
                appearance.LoadProfile(hologram, Profile("Human"));
                var holoSprite = entities.GetComponent<SpriteComponent>(hologram);
                entities.EventBus.RaiseLocalEvent(hologram, new BeforePostShaderRenderEvent(holoSprite, null));
                AssertHairShader(hologram, holoSprite);
                entities.DeleteEntity(hologram);
                var lobby = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<LobbyUIController>();
                var preview = lobby.LoadProfileEntity(Profile("Human"), prototypes.Index<JobPrototype>("Contractor"), true);
                AssertGradient(entities.GetComponent<HumanoidAppearanceComponent>(preview), Profile("Human").Appearance);
                AssertHairShader(preview, entities.GetComponent<SpriteComponent>(preview));
                entities.DeleteEntity(preview);
                TestContext.Out.WriteLine("Hair gradient client assertions completed");

                void AssertHairShader(EntityUid uid, SpriteComponent component)
                {
                    Assert.That(sprites.LayerMapTryGet((uid, component), layerId, out var index, false), Is.True);
                    Assert.That(component.TryGetLayer(index, out var layer), Is.True);
                    Assert.That(layer.ShaderPrototype, Is.EqualTo(HairColoringHelper.GradientShader));
                    Assert.That(component[index].Color, Is.EqualTo(Color.White));
                }
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Hair gradient regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }

    private static HumanoidCharacterProfile Profile(string species)
    {
        var appearance = new HumanoidCharacterAppearance
        {
            HairStyleId = "HumanHairLong",
            HairColor = Color.White,
            SkinColor = Color.Cyan,
            HairColoringMode = HairColoringMode.Gradient,
            HairGradientColor = Color.Red,
            HairGradientDirection = HairGradientDirection.BottomToTop,
            FacialHairColoringMode = HairColoringMode.Gradient,
            FacialHairGradientColor = Color.Blue,
            FacialHairGradientDirection = HairGradientDirection.RightToLeft,
        };
        return HumanoidCharacterProfile.DefaultWithSpecies(species).WithCharacterAppearance(appearance);
    }

    private static void AssertGradient(HumanoidAppearanceComponent actual, HumanoidCharacterAppearance expected)
    {
        Assert.That(actual.HairColoringMode, Is.EqualTo(expected.HairColoringMode));
        Assert.That(actual.HairGradientColor, Is.EqualTo(expected.HairGradientColor));
        Assert.That(actual.HairGradientDirection, Is.EqualTo(expected.HairGradientDirection));
        Assert.That(actual.FacialHairColoringMode, Is.EqualTo(expected.FacialHairColoringMode));
        Assert.That(actual.FacialHairGradientColor, Is.EqualTo(expected.FacialHairGradientColor));
        Assert.That(actual.FacialHairGradientDirection, Is.EqualTo(expected.FacialHairGradientDirection));
    }
}
