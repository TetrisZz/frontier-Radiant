using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server._HL.Shipyard;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class OfficeChairMigrationTest
{
    [Test]
    public async Task LegacyOfficeChairIdsLoadAsStandardChairsAndSaveWithoutTheOldIds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypes.HasIndex<EntityPrototype>("ChairOfficeWhiteRadiant"), Is.False);
            Assert.That(prototypes.HasIndex<EntityPrototype>("ChairOfficeDarkRadiant"), Is.False);
            foreach (var id in new[] { "ChairOfficeLight", "ChairOfficeDark", "ChairOfficeGrey" })
            {
                var chair = em.SpawnEntity(id, map.GridCoords);
                em.System<SharedTransformSystem>().Unanchor(chair);
            }

            var saves = em.System<ShipyardGridSaveSystem>();
            Assert.That(saves.TryBuildShipSaveYaml(map.Grid.Owner, out var saved, out _), Is.True);
            var yaml = new YamlStream();
            using (var reader = new StringReader(saved))
                yaml.Load(reader);
            var groups = (YamlSequenceNode) ((YamlMappingNode) yaml.Documents[0].RootNode).Children[new YamlScalarNode("entities")];
            var renamed = 0;
            foreach (var group in groups.Children.Cast<YamlMappingNode>())
            {
                if (group.Children[new YamlScalarNode("proto")] is not YamlScalarNode proto)
                    continue;
                var legacy = proto.Value switch
                {
                    "ChairOfficeLight" => "ChairOfficeWhiteRadiant",
                    "ChairOfficeDark" => "ChairOfficeDarkRadiant",
                    _ => null,
                };
                if (legacy == null)
                    continue;
                proto.Value = legacy;
                renamed++;
            }
            Assert.That(renamed, Is.EqualTo(2));
            using var writer = new StringWriter();
            yaml.Save(writer, false);
            var legacySave = ShipSaveYamlSanitizer.ScrubShipLoadYaml(writer.ToString(), out _);
            Assert.That(legacySave, Does.Contain("ChairOfficeWhiteRadiant").And.Contain("ChairOfficeDarkRadiant"),
                "The ship sanitizer must leave legacy IDs for the map migration to resolve.");
            using var legacyReader = new StringReader(legacySave);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, legacyReader, "legacy office chairs",
                out var loaded, offset: new Vector2(20, 20)), Is.True);
            var ids = new List<string>();
            var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var metadata, out var transform))
            {
                if (transform.GridUid != loaded.Value.Owner || metadata.EntityPrototype is not { } prototype)
                    continue;
                ids.Add(prototype.ID);
                if (prototype.ID is "ChairOfficeLight" or "ChairOfficeDark" or "ChairOfficeGrey")
                    Assert.That(transform.Anchored, Is.False, "Migrating a loose chair must not anchor it.");
            }
            foreach (var id in new[] { "ChairOfficeLight", "ChairOfficeDark", "ChairOfficeGrey" })
                Assert.That(ids.Count(value => value == id), Is.EqualTo(1), id);
            Assert.That(saves.TryBuildShipSaveYaml(loaded.Value.Owner, out var resaved, out _), Is.True);
            Assert.That(resaved, Does.Not.Contain("ChairOfficeWhiteRadiant").And.Not.Contain("ChairOfficeDarkRadiant"));
            em.DeleteEntity(loaded.Value.Owner);
        });
        TestContext.Out.WriteLine("All legacy office chair migration and ship save assertions passed.");
        await pair.CleanReturnAsync();
    }
}
