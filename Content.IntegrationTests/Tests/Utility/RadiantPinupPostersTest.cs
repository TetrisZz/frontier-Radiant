using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Server.Spawners.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class RadiantPinupPostersTest
{
    private static readonly Dictionary<string, (string State, string Rsi)> Posters = new()
    {
        ["PosterContrabandRabbitHoleRS"] = ("rabbit_hole", "pinup_posters.rsi"),
        ["PosterContrabandBridgeBunnyRS"] = ("nt-bunny", "pinup_posters.rsi"),
        ["PosterContrabandDoubleGunshowRS"] = ("double_gunshow", "pinup_posters.rsi"),
        ["PosterContrabandRabbitSwoleRS"] = ("rabbit_swole", "pinup_posters.rsi"),
        ["PosterRushPropagandaRS"] = ("rush_propaganda", "extra_posters.rsi"),
        ["PosterMidtownSliceRS"] = ("midtown_slice", "extra_posters.rsi"),
        ["PosterHolidayCakeRS"] = ("holiday_cake", "extra_posters.rsi"),
        ["PosterHolidayLoveRS"] = ("holiday_love", "extra_posters.rsi"),
    };

    [Test]
    public async Task PostersLoadAndAreReachableFromAllThreeSpawners()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var prototypes = pair.Server.ProtoMan;
                var factory = pair.Server.ResolveDependency<IComponentFactory>();
                foreach (var spawner in new[] { "RandomPosterAny", "RandomPosterContraband", "RandomPosterLegit" })
                {
                    var possible = new HashSet<string>();
                    CollectPosters(spawner, new HashSet<string>(), possible);
                    Assert.That(possible, Is.SupersetOf(Posters.Keys), spawner);
                }

                void CollectPosters(string id, HashSet<string> visited, HashSet<string> result)
                {
                    if (!visited.Add(id))
                        return;
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    if (!prototype.TryGetComponent<RandomSpawnerComponent>(out var spawner, factory))
                    {
                        result.Add(id);
                        return;
                    }
                    foreach (var poster in Posters.Keys)
                        Assert.That(spawner.Prototypes.Count(child => child.Id == poster), Is.LessThanOrEqualTo(1), id + ": " + poster);
                    foreach (var child in spawner.Prototypes)
                        CollectPosters(child.Id, visited, result);
                }
            });

            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var map = em.System<SharedMapSystem>().CreateMap(out _);
                try
                {
                    foreach (var (id, (stateName, rsiName)) in Posters)
                    {
                        var poster = em.SpawnEntity(id, new EntityCoordinates(map, 0, 0));
                        var sprite = em.GetComponent<SpriteComponent>(poster);
                        var rsi = sprite.BaseRSI;
                        Assert.That(rsi, Is.Not.Null, id);
                        Assert.That(rsi!.Path.ToString(), Does.EndWith("_radiant/Structures/Wallmounts/" + rsiName), id);
                        Assert.That(rsi.TryGetState(stateName, out var state), Is.True, id);
                        Assert.That(state!.IsAnimated, Is.False, id);
                        em.DeleteEntity(poster);
                    }
                    var localization = IoCManager.Resolve<ILocalizationManager>();
                    var original = localization.DefaultCulture;
                    try
                    {
                        foreach (var cultureName in new[] { "ru-RU", "en-US" })
                        {
                            var culture = CultureInfo.GetCultureInfo(cultureName);
                            if (!localization.HasCulture(culture))
                                localization.LoadCulture(culture);
                            localization.DefaultCulture = culture;
                            foreach (var id in Posters.Keys)
                                Assert.That(localization.TryGetString("ent-" + id, out var name) && !string.IsNullOrWhiteSpace(name),
                                    Is.True, cultureName + ": " + id);
                        }
                    }
                    finally
                    {
                        localization.DefaultCulture = original;
                    }
                }
                finally
                {
                    em.DeleteEntity(map);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
