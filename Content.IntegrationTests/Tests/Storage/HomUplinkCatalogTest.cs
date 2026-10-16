using System;
using System.Globalization;
using System.Linq;
using Content.Shared.Store;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class HomUplinkCatalogTest
{
    [Test]
    public async Task DonorCatalogDescriptionsResolveAndStayCompact()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var loc = pair.Server.ResolveDependency<ILocalizationManager>();
                var previousCulture = loc.DefaultCulture;
                try
                {
                    var culture = CultureInfo.GetCultureInfo("ru-RU");
                    loc.DefaultCulture = culture;
                    var listings = prototypes.EnumeratePrototypes<ListingPrototype>()
                        .Where(listing => listing.Categories.Any(category =>
                            category.Id.StartsWith("UplinkHom", StringComparison.Ordinal))).ToList();
                    Assert.That(listings.Count, Is.GreaterThanOrEqualTo(60));
                    foreach (var listing in listings)
                    {
                        Assert.That(listing.Description, Does.StartWith("uplink-hom-"), listing.ID);
                        Assert.That(loc.TryGetString(listing.Description!, out var text), Is.True, listing.ID);
                        Assert.That(text, Is.Not.Empty, listing.ID);
                        Assert.That(text!.Length, Is.LessThanOrEqualTo(260), listing.ID);
                    }
                    Assert.That(loc.GetString("uplink-hom-backpack-desc"), Does.Contain("извлеките всё содержимое"));
                    Assert.That(loc.GetString("uplink-hom-gut-desc"), Does.Contain("извлеките всё содержимое"));
                    Assert.That(prototypes.Index<ListingPrototype>("UplinkCrateFunMobFenGhost").Description,
                        Is.EqualTo("uplink-hom-fennec-desc"));
                    Assert.That(prototypes.Index<ListingPrototype>("UplinkCrateFunMobPigRSGhost").Description,
                        Is.EqualTo("uplink-hom-pig-desc"));
                    TestContext.Out.WriteLine($"Hom uplink catalog assertions completed: {listings.Count} listings");
                }
                finally
                {
                    loc.DefaultCulture = previousCulture;
                }
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Hom uplink catalog regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
