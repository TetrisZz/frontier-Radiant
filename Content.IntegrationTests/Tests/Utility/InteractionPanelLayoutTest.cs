using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client.Interaction.Panel.Ui;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Interaction.Panel;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
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
            IoCManager.Resolve<IPrototypeManager>().RegisterKind(typeof(InteractionPrototype));
            using var panel = new InteractionPanelMenu();
            panel.Measure(new Vector2(640, 740));
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(640, 740)));
            Assert.That(panel.FindControl<BoxContainer>("ManagementPanel").Visible, Is.False);
            Assert.That(panel.FindControl<Button>("FavoritesOnlyButton").ToggleMode, Is.True);
            Assert.That(panel.FindControl<BoxContainer>("InteractionContainer").Children
                .OfType<Collapsible>().All(group => !group.BodyVisible), Is.True);
            var groups = panel.FindControl<BoxContainer>("InteractionContainer").Children.OfType<Collapsible>().ToArray();
            foreach (var group in groups)
                group.BodyVisible = true;
            // This bare client has no window theme; invoke the opening lifecycle hook directly.
            var opened = typeof(InteractionPanelMenu).GetMethod("Opened",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            opened.Invoke(panel, null);
            Assert.That(groups.All(group => !group.BodyVisible), Is.True, "Opening must collapse every category");
            panel.FindControl<CheckBox>("AvailableOnlyCheckBox").Pressed = true;
            panel.FindControl<Button>("FavoritesOnlyButton").Pressed = true;
            panel.FindControl<LineEdit>("SearchBar").Text = "test";
            Assert.That(groups.All(group => !group.BodyVisible), Is.True, "Filters must not expand categories");
            foreach (var group in groups)
                group.BodyVisible = true;
            opened.Invoke(panel, null);
            Assert.That(groups.All(group => !group.BodyVisible), Is.True, "Reopening must also collapse categories");
            panel.UpdateAppendageHeading("SlimePerson");
            Assert.That(panel.TailHeading.Label.Text,
                Is.EqualTo(Robust.Shared.Localization.Loc.GetString("interaction-panel-slime-collapsible")));
            panel.UpdateAppendageHeading("Human");
            Assert.That(panel.TailHeading.Label.Text,
                Is.EqualTo(Robust.Shared.Localization.Loc.GetString("interaction-panel-tail-collapsible")));
            Assert.That(panel.FindControl<PanelContainer>("TargetCard").Visible, Is.False);
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
            foreach (var name in new[] { "Lips", "Hands", "Head", "Chest", "Body" })
            {
                var heading = panel.FindControl<CollapsibleHeading>(name);
                var icon = heading.Label.Parent!.Children.OfType<TextureRect>().Single(c => c.Name == "BodyPartIcon");
                Assert.That(icon.Texture, Is.Not.Null);
                Assert.That(icon.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                Assert.That(icon.Modulate, Is.EqualTo(Color.FromHex("#E85A8A")));
                Assert.That(((Collapsible) heading.Parent!).BodyVisible, Is.False);
            }
        });
    }
}
