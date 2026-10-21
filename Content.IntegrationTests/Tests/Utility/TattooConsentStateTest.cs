using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Content.Client._radiant.Social;
using Content.Client.Stylesheets;
using Content.Shared._radiant.Social;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class TattooConsentStateTest
{
    [Test]
    public async Task QueuedIdleStateCannotDisableANewConsentRequest()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/TattooConsentStateTestPrototypes"),
                MountOptions = new MountOptions { DirMounts = new() { resources } },
            },
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
            prototypes.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/fonts.yml")));
            prototypes.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/themes.yml")));
            prototypes.ResolveResults();
            var uiManager = IoCManager.Resolve<IUserInterfaceManager>();
            uiManager.SetDefaultTheme("SS14DefaultTheme");
            uiManager.Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet;
            var loc = IoCManager.Resolve<ILocalizationManager>();
            var culture = CultureInfo.GetCultureInfo("ru-RU");
            if (!loc.HasCulture(culture))
                loc.LoadCulture(culture);
            loc.DefaultCulture = culture;
            var em = IoCManager.Resolve<IEntityManager>();
            em.Startup();
            var owner = em.SpawnEntity(null, MapCoordinates.Nullspace);
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.SetUi(owner, TattooUiKey.Consent, new InterfaceData(nameof(TattooConsentBoundUserInterface), 0, false));
            var request = new TattooUiState("Patient", 20, new TextTattoo { Text = "Remember", Zone = 2, Color = 1 }, false);
            ui.SetUiState(owner, TattooUiKey.Consent, request);
            using var bui = new TattooConsentBoundUserInterface(owner, TattooUiKey.Consent);
            typeof(BoundUserInterface).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bui, null);
            // An already-known machine can queue its old idle state before applying the new request.
            typeof(BoundUserInterface).GetMethod("UpdateState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(bui, [new TattooUiState("Patient", 20, null, false)]);
            bui.Update();
            var window = uiManager.WindowRoot.Children.OfType<TattooConsentWindow>().Single();
            var column = window.ContentsContainer.Children.Single();
            var accept = column.Children.OfType<Button>().Single(button => button.Text == Loc.GetString("tattoo-accept"));
            Assert.That(accept.Disabled, Is.False, "The current request, not the queued idle state, must control consent.");
            Assert.That(column.Children.OfType<RichTextLabel>().Single().GetMessage(), Does.Contain("Remember"));
            ui.SetUiState(owner, TattooUiKey.Consent, new TattooUiState("Patient", 20, request.Tattoo, true));
            bui.Update();
            Assert.That(accept.Disabled, Is.True, "Accepting twice must remain blocked.");
            Assert.That(column.Children.OfType<Button>().Single(button => button.Text == Loc.GetString("tattoo-reject")).Disabled, Is.False);
            ui.SetUiState(owner, TattooUiKey.Consent, new TattooUiState("Patient", 20, null, false));
            bui.Update();
            Assert.That(accept.Disabled, Is.True, "A cancelled request must not be accepted.");
        });
    }
}
