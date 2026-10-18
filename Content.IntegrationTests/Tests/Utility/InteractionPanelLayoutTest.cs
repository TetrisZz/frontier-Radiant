using System.IO;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Interaction.Panel.Ui;
using Content.Client.Hands;
using Content.Client.UserInterface.Systems.Interaction;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Interaction.Panel;
using Robust.Client;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class InteractionPanelLayoutTest
{
    [Test]
    public async Task PanelLoadsIconsAndKeepsManagementCollapsed()
    {
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/PanelLayoutTestPrototypes"),
                MountOptions = new MountOptions
                {
                    DirMounts = new()
                    {
                        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"))
                    }
                }
            },
        };
        options.BeforeStart += () =>
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.DoAutoRegistrations();
            factory.GenerateNetIds();
            IoCManager.Register<InteractionPanelManager>();
            IoCManager.BuildGraph();
        };
        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await client.WaitAssertion(() =>
        {
            // The bare client skips content loading; include window settings in its serializer.
            var serialization = IoCManager.Resolve<ISerializationManager>();
            serialization.Shutdown();
            IoCManager.Resolve<IReflectionManager>().LoadAssemblies(typeof(InteractionPanelPreferences).Assembly);
            serialization.Initialize();
            IoCManager.Resolve<IPrototypeManager>().RegisterKind(typeof(InteractionPrototype));
            using var panel = new InteractionPanelMenu();
            panel.Measure(new Vector2(640, 740));
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(640, 740)));
            Assert.That(panel.FindControl<BoxContainer>("ManagementPanel").Visible, Is.False);
            Assert.That(panel.FindControl<Button>("FavoritesOnlyButton").ToggleMode, Is.True);
            Assert.That(panel.FindControl<BoxContainer>("InteractionContainer").Children
                .OfType<Collapsible>().All(group => !group.BodyVisible), Is.True);
            var groups = panel.FindControl<BoxContainer>("InteractionContainer").Children.OfType<Collapsible>().ToArray();
            var expanded = groups.Select((_, index) => index % 2 == 0).ToArray();
            for (var index = 0; index < groups.Length; index++)
                groups[index].BodyVisible = expanded[index];
            // This bare client has no window theme; invoke the opening lifecycle hook directly.
            var opened = typeof(InteractionPanelMenu).GetMethod("Opened",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            opened.Invoke(panel, null);
            Assert.That(groups.Select(group => group.BodyVisible), Is.EqualTo(expanded), "Opening must preserve category state");
            panel.FindControl<Button>("FavoritesOnlyButton").Pressed = true;
            panel.FindControl<LineEdit>("SearchBar").Text = "test";
            var filter = typeof(InteractionPanelMenu).GetMethod("UpdateButtonsVisibility",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            filter.Invoke(panel, null);
            Assert.That(groups.Select(group => group.BodyVisible), Is.EqualTo(expanded), "Filters must preserve category state");
            groups[0].BodyVisible = false;
            expanded[0] = false;
            opened.Invoke(panel, null);
            Assert.That(groups.Select(group => group.BodyVisible), Is.EqualTo(expanded), "Reopening must preserve category state");
            panel.UpdateAppendageHeading("SlimePerson");
            Assert.That(panel.TailHeading.Label.Text,
                Is.EqualTo(Robust.Shared.Localization.Loc.GetString("interaction-panel-slime-collapsible") + " (0)"));
            panel.UpdateAppendageHeading("Human");
            Assert.That(panel.TailHeading.Label.Text,
                Is.EqualTo(Robust.Shared.Localization.Loc.GetString("interaction-panel-tail-collapsible") + " (0)"));
            Assert.That(panel.FindControl<PanelContainer>("TargetCard").Visible, Is.False);
            var pick = panel.FindControl<Button>("PickTargetButton");
            Assert.That(pick.ToggleMode, Is.True);
            Assert.That(pick.Disabled, Is.True, "World picking needs an attached character");
            var resources = IoCManager.Resolve<IResourceCache>();
            Assert.That(resources.TryGetResource<RSIResource>(
                new ResPath("/Textures/Interface/Misc/crosshair_pointers.rsi"), out var pickRsi), Is.True);
            Assert.That(pickRsi!.RSI.TryGetState("gun_sight", out var pickState), Is.True);
            var pickTexture = pickState!.GetFrame(RsiDirection.South, 0);
            Assert.That(pick.Children.OfType<TextureRect>().Single().Texture, Is.SameAs(pickTexture));
            Assert.That(panel.TargetPickTexture, Is.SameAs(pickTexture), "The world cursor must use the same RSI frame");
            Assert.That(pickTexture.Size, Is.EqualTo(new Vector2i(64, 64)));
            var overlays = IoCManager.Resolve<IOverlayManager>();
            var cursor = new ShowHandItemOverlay();
            overlays.AddOverlay(cursor);
            try
            {
                var previousTexture = pickRsi.RSI["melee_sight"].GetFrame(RsiDirection.South, 0);
                cursor.IconOverride = previousTexture;
                cursor.IconOverrideScale = 0.75f;
                var controller = new InteractionUIController();
                IoCManager.InjectDependencies(controller);
                typeof(InteractionUIController).GetField("_interactionWindow",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(controller, panel);
                var updateCursor = typeof(InteractionUIController).GetMethod("UpdatePickCursor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                updateCursor.Invoke(controller, new object[] { true });
                Assert.That(cursor.IconOverride, Is.SameAs(pickTexture));
                Assert.That((Vector2) cursor.IconOverride!.Size * cursor.IconOverrideScale, Is.EqualTo(new Vector2(32, 32)));
                Assert.That(panel.FindControl<TextureRect>("PickTargetIcon").SetSize, Is.EqualTo(new Vector2(24, 24)));
                updateCursor.Invoke(controller, new object[] { false });
                Assert.That(cursor.IconOverride, Is.SameAs(previousTexture));
                Assert.That(cursor.IconOverrideScale, Is.EqualTo(0.75f));
                cursor.IconOverride = pickTexture;
                Assert.That(cursor.IconOverrideScale, Is.EqualTo(1f), "Replacing a cursor must reset its scale");
            }
            finally
            {
                overlays.RemoveOverlay<ShowHandItemOverlay>();
            }
            Assert.That(panel.TryPickTarget(EntityUid.Invalid), Is.False);
            Assert.That(panel.FindControl<BoxContainer>("UserSpriteView").Orientation,
                Is.EqualTo(BoxContainer.LayoutOrientation.Horizontal));
            Assert.That(panel.FindControl<BoxContainer>("UserNameRow").Parent,
                Is.EqualTo(panel.FindControl<BoxContainer>("UserSpriteView").Parent));
            Assert.That(panel.FindControl<BoxContainer>("TargetSpriteView").Orientation,
                Is.EqualTo(BoxContainer.LayoutOrientation.Horizontal));
            var userCard = panel.FindControl<PanelContainer>("UserCard");
            var targetCard = panel.FindControl<PanelContainer>("TargetCard");
            targetCard.Visible = true;
            foreach (var model in new[] { "UserSpriteView", "TargetSpriteView" })
                panel.FindControl<BoxContainer>(model).AddChild(new Label { Text = "Character\nStatus" });
            // The detached test window has no UI frame to process queued child layout updates.
            var cards = panel.FindControl<BoxContainer>("UpperBlock");
            cards.InvalidateMeasure();
            cards.Measure(new Vector2(592, 150));
            cards.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(592, 150)));
            Assert.That(userCard.Position.X + userCard.Size.X, Is.LessThanOrEqualTo(targetCard.Position.X));
            Assert.That(targetCard.Size.X, Is.GreaterThanOrEqualTo(200));
            var matches = typeof(InteractionPanelMenu).GetMethod("MatchesFilters",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var action = new Button { Text = "Action" };
            Assert.That(matches.Invoke(panel, new object[] { action, string.Empty }), Is.EqualTo(true));
            action.Disabled = true;
            Assert.That(matches.Invoke(panel, new object[] { action, string.Empty }), Is.EqualTo(false));
            action.Disabled = false;
            Assert.That(matches.Invoke(panel, new object[] { action, "missing" }), Is.EqualTo(false));
            Assert.That(matches.Invoke(panel, new object[] { action, "action" }), Is.EqualTo(true));
            foreach (var width in new[] { 540f, 640f })
            {
                panel.InvalidateMeasure();
                panel.Measure(new Vector2(width, 740));
                panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(width, 740)));
                var search = panel.FindControl<LineEdit>("SearchBar");
                var clear = panel.FindControl<Button>("ClearSearchButton");
                var favorites = panel.FindControl<Button>("FavoritesOnlyButton");
                var management = panel.FindControl<Button>("ManagementButton");
                Assert.That(search.Size.X, Is.GreaterThan(200));
                Assert.That(search.Position.X + search.Size.X, Is.LessThanOrEqualTo(clear.Position.X));
                Assert.That(clear.Position.X + clear.Size.X, Is.LessThanOrEqualTo(favorites.Position.X));
                Assert.That(favorites.Position.X + favorites.Size.X, Is.LessThanOrEqualTo(management.Position.X));
            }
            foreach (var name in new[] { "Lips", "Hands", "Head", "Chest", "Body" })
            {
                var heading = panel.FindControl<CollapsibleHeading>(name);
                var icon = heading.Label.Parent!.Children.OfType<TextureRect>().Single(c => c.Name == "BodyPartIcon");
                Assert.That(icon.Texture, Is.Not.Null);
                Assert.That(icon.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                Assert.That(icon.Modulate, Is.EqualTo(Color.FromHex("#E85A8A")));
                var group = (Collapsible) heading.Parent!;
                Assert.That(group.BodyVisible, Is.EqualTo(expanded[Array.IndexOf(groups, group)]));
                Assert.That(heading.Label.Text, Does.EndWith(" (0)"));
            }
            var save = typeof(InteractionPanelMenu).GetMethod("SavePanelPreferences",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var savedSize = new Vector2(760, 800);
            panel.SetSize = savedSize;
            panel.Measure(savedSize);
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, savedSize));
            save.Invoke(panel, null);
            using var restored = new InteractionPanelMenu();
            opened.Invoke(restored, null);
            Assert.That(restored.SetSize, Is.EqualTo(savedSize), "A new window must reload its saved size");
            var restoredGroups = restored.FindControl<BoxContainer>("InteractionContainer").Children.OfType<Collapsible>();
            Assert.That(restoredGroups.Select(group => group.BodyVisible), Is.EqualTo(expanded),
                "A new window must reload saved category state");
            var localization = IoCManager.Resolve<ILocalizationManager>();
            foreach (var language in new[] { "ru-RU", "en-US" })
            {
                localization.SetCulture(CultureInfo.GetCultureInfo(language));
                var distance = localization.GetString("interaction-panel-partner-distance", ("distance", Math.Round(1.56f, 1)));
                Assert.That(distance, Does.Match(@"^1[.,]6 (m|\u043c)$"), "Distance must show one decimal digit and its unit");
                Assert.That(distance, Does.Not.Contain("{"), "Distance must not show an unresolved Fluent expression");
                Assert.That(distance, Does.EndWith(language == "ru-RU" ? " \u043c" : " m"));
            }
        });
    }
}
