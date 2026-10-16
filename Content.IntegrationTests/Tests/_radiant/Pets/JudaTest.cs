using System;
using Content.Server.Speech.Components;
using Content.Shared.Speech;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._radiant.Pets;

[TestFixture]
public sealed class JudaTest
{
    [Test]
    public async Task JudaBubblesAndSpawnsOnlyAfterMapInitialization()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var entities = pair.Server.EntMan;
            MapId mapId = default;
            EntityUid map = default;
            EntityUid marker = default;
            await pair.Server.WaitAssertion(() =>
            {
                map = entities.System<SharedMapSystem>().CreateMap(out mapId, runMapInit: false);
                marker = entities.SpawnEntity("SpawnMobJudaEmotionalSupport",
                    new MapCoordinates(System.Numerics.Vector2.Zero, mapId));
                Assert.That(CountJudas(), Is.Zero, "An uninitialized map must retain its spawner.");
                entities.System<SharedMapSystem>().InitializeMap(mapId);
                Assert.That(CountJudas(), Is.EqualTo(1));
                var query = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var metadata, out var transform))
                {
                    if (metadata.EntityPrototype?.ID != "MobJudaEmotionalSupport" || transform.MapID != mapId)
                        continue;
                    Assert.That(entities.GetComponent<ReplacementAccentComponent>(uid).Accent, Is.EqualTo("Juda"));
                    var speech = entities.GetComponent<SpeechComponent>(uid);
                    Assert.That(speech.SpeechVerb.ToString(), Is.EqualTo("Slime"));
                    Assert.That(speech.SpeechSounds!.Value.ToString(), Is.EqualTo("Slime"));
                    for (var i = 0; i < 12; i++)
                    {
                        var accent = new AccentGetEvent(uid, "Hello!");
                        entities.EventBus.RaiseLocalEvent(uid, accent);
                        Assert.That(accent.Message, Does.Contain("буль").IgnoreCase
                            .Or.Contain("blub").IgnoreCase);
                    }
                }
            });
            await pair.Server.WaitRunTicks(2);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(entities.Deleted(marker), Is.True, "The single-use marker must disappear.");
                Assert.That(CountJudas(), Is.EqualTo(1), "The marker must not spawn duplicates.");
                entities.DeleteEntity(map);
            });
            TestContext.Out.WriteLine("Juda accent and uninitialized-map spawn assertions completed");

            int CountJudas()
            {
                var count = 0;
                var query = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var metadata, out var transform))
                {
                    if (metadata.EntityPrototype?.ID == "MobJudaEmotionalSupport" && transform.MapID == mapId)
                        count++;
                }
                return count;
            }
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Juda regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
