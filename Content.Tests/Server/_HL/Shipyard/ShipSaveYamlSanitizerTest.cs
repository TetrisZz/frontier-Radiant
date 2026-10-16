using Content.Server._HL.Shipyard;
using System.Collections.Generic;
using NUnit.Framework;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Tests.Server._HL.Shipyard;

/// <summary>
/// Covers the load-side scrub, whose risk is the YAML parse/emit round-trip rather than the pruning
/// itself: a ship file the owner cannot load is far worse than the log noise the scrub removes.
/// </summary>
[TestFixture]
[TestOf(typeof(ShipSaveYamlSanitizer))]
public sealed class ShipSaveYamlSanitizerTest
{
    [Test]
    public void ExportPreservesSafeContentsWithoutRelaxingOtherStorageFilters()
    {
        MappingDataNode Card(string uid) => new()
        {
            { "uid", new ValueDataNode(uid) },
            { "components", new SequenceDataNode
                {
                    new MappingDataNode { { "type", new ValueDataNode("IdCard") } },
                    new MappingDataNode
                    {
                        { "type", new ValueDataNode("Battery") },
                        { "currentCharge", new ValueDataNode("123") },
                    },
                }
            },
        };
        var savedCard = Card("1");
        var ordinaryCard = Card("2");
        var entities = new SequenceDataNode { savedCard, ordinaryCard };
        var root = new MappingDataNode
        {
            { "entities", new SequenceDataNode
                { new MappingDataNode { { "entities", entities } } } },
        };

        ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!, new HashSet<string> { "1" });

        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0], Is.SameAs(savedCard));
        var battery = (MappingDataNode) savedCard.Get<SequenceDataNode>("components")[1];
        Assert.That(battery.Get<ValueDataNode>("currentCharge").Value, Is.EqualTo("123"));
    }

    [Test]
    public void ExportClearsStationBindingWithoutClearingMaterials()
    {
        var binding = new MappingDataNode
        {
            { "type", new ValueDataNode("StationBoundObject") },
            { "boundStation", new ValueDataNode("99") },
            { "enabled", new ValueDataNode("true") },
        };
        var materials = new MappingDataNode
        {
            { "type", new ValueDataNode("MaterialStorage") },
            { "storage", new MappingDataNode { { "Steel", new ValueDataNode("1234") } } },
        };
        var expected = materials.Copy();
        var entity = new MappingDataNode
        {
            { "uid", new ValueDataNode("1") },
            { "components", new SequenceDataNode { binding, materials } },
        };
        var root = new MappingDataNode
        {
            { "entities", new SequenceDataNode
                { new MappingDataNode { { "entities", new SequenceDataNode { entity } } } } },
        };

        ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!);

        Assert.That(binding.Get<ValueDataNode>("boundStation").IsNull, Is.True);
        Assert.That(binding.Get<ValueDataNode>("enabled").Value, Is.EqualTo("false"));
        Assert.That(materials, Is.EqualTo(expected));
    }

    [Test]
    public void LoadClearsLegacyStationBindingEvenWhenTargetExists()
    {
        const string yaml = "entities:\n- proto: ''\n  entities:\n  - uid: 1\n    components:\n"
            + "    - type: StationBoundObject\n      boundStation: 1\n      enabled: true\n";
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out var scrubbed);
        Assert.That(scrubbed, Is.EqualTo(1));
        Assert.That(result, Does.Contain("enabled: false"));
        Assert.That(ShipSaveYamlSanitizer.ScrubShipLoadYaml(result, out var repeated), Is.EqualTo(result));
        Assert.That(repeated, Is.Zero);
    }

    [Test]
    public void ExportKeepsRadarSettings()
    {
        var iff = new MappingDataNode
        {
            { "type", new ValueDataNode("IFF") },
            { "color", new ValueDataNode("#3ca6fa") },
            { "flags", new ValueDataNode("IsPlayerShuttle, HideLabel") },
            { "serviceFlags", new ValueDataNode("Trade") },
            { "readOnly", new ValueDataNode("true") },
        };
        var expected = iff.Copy();
        var entity = new MappingDataNode
        {
            { "uid", new ValueDataNode("1") },
            { "components", new SequenceDataNode { iff } },
        };
        var group = new MappingDataNode { { "entities", new SequenceDataNode { entity } } };
        var root = new MappingDataNode { { "entities", new SequenceDataNode { group } } };

        ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!);

        Assert.That(entity.Get<SequenceDataNode>("components"), Does.Contain(iff));
        Assert.That(iff, Is.EqualTo(expected));
    }

    [Test]
    public void ExportKeepsConfiguredAccessButClearsRoundScopedRecords()
    {
        var access = new MappingDataNode
        {
            { "type", new ValueDataNode("AccessReader") },
            { "enabled", new ValueDataNode("false") },
            { "access", new SequenceDataNode() },
            { "accessKeys", new SequenceDataNode { new ValueDataNode("old-record") } },
            { "accessLog", new SequenceDataNode { new ValueDataNode("old-log") } },
        };
        var entity = new MappingDataNode
        {
            { "uid", new ValueDataNode("1") },
            { "components", new SequenceDataNode { access } },
        };
        var group = new MappingDataNode { { "entities", new SequenceDataNode { entity } } };
        var root = new MappingDataNode { { "entities", new SequenceDataNode { group } } };

        // No prototype group: the prototype manager is deliberately not needed for this case.
        ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!);

        Assert.That(entity.Get<SequenceDataNode>("components"), Does.Contain(access));
        Assert.That(access.Get<ValueDataNode>("enabled").Value, Is.EqualTo("false"));
        Assert.That(access.Get<SequenceDataNode>("access"), Is.Empty);
        Assert.That(access.Get<SequenceDataNode>("accessKeys"), Is.Empty);
        Assert.That(access.Get<SequenceDataNode>("accessLog"), Is.Empty);
    }

    // uid 1 is present. 'invalid' is what the writer emits for a reference it could not resolve, and
    // 99 is a uid the file never defines. Both are dangling; only 1 should survive.
    private const string ShipWithDanglingRefs = @"meta:
  format: 7
entities:
- proto: """"
  entities:
  - uid: 1
    components:
    - type: Transform
- proto: WallSolid
  entities:
  - uid: 2
    components:
    - type: Transform
    - type: EmbeddedContainer
      embeddedObjects:
      - 1
      - invalid
      - 99
    - type: CombatMode
      combatToggleActionEntity: invalid
    - type: VendingMachinePurchase
      purchaseGrid: invalid
";

    private const string CleanShip = @"meta:
  format: 7
entities:
- proto: WallSolid
  entities:
  - uid: 1
    components:
    - type: Transform
  - uid: 2
    components:
    - type: EmbeddedContainer
      embeddedObjects:
      - 1
";

    [Test]
    public void ScrubDropsDanglingSetEntriesAndKeepsLiveOnes()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithDanglingRefs, out var scrubbed);

        Assert.That(scrubbed, Is.EqualTo(4), "two set entries, one scalar, one required-ref component");
        Assert.That(result, Does.Not.Contain("invalid"), "no dangling marker should survive the scrub");
        Assert.That(result, Does.Not.Contain("99"), "a uid the file never defines is dangling too");
        Assert.That(result, Does.Contain("embeddedObjects"), "the live entry keeps the field alive");
    }

    [Test]
    public void ScrubNullsScalarRefAndDropsRequiredRefComponent()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithDanglingRefs, out _);

        // Nullable scalar is nulled in place; the component stays so the rest of its state loads.
        Assert.That(result, Does.Contain("CombatMode"));
        // purchaseGrid is non-nullable, so nulling would just trade one load error for another.
        Assert.That(result, Does.Not.Contain("VendingMachinePurchase"));
    }

    [Test]
    public void ScrubLeavesACleanFileByteIdentical()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(CleanShip, out var scrubbed);

        Assert.That(scrubbed, Is.EqualTo(0));
        Assert.That(result, Is.EqualTo(CleanShip), "a file with nothing to remove must not be re-emitted");
    }

    [Test]
    public void ScrubReturnsInputUnchangedWhenItCannotParse()
    {
        const string garbage = "this: is: not: a: ship: file:\n\t- [unclosed";

        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(garbage, out var scrubbed);

        Assert.That(scrubbed, Is.EqualTo(0));
        Assert.That(result, Is.EqualTo(garbage), "a parse failure must hand the loader the original text");
    }

    // The shape a pre-rework ship save carries: a legacy artifact whose grant action sits in its
    // 'actions' container, with the legacy component nodes real map files serialized (Artifact with
    // isSuppressed, BiasedArtifact). uid 2 is the artifact, uid 3 the action.
    private const string ShipWithLegacyArtifact = @"meta:
  format: 7
entities:
- proto: WallSolid
  entities:
  - uid: 1
    components:
    - type: Transform
- proto: VariedXenoArtifactItem
  entities:
  - uid: 2
    components:
    - type: Transform
    - type: Artifact
      isSuppressed: True
    - type: BiasedArtifact
    - type: ContainerContainer
      containers:
        actions: !type:Container
          ents:
          - 3
- proto: ActionArtifactActivate
  entities:
  - uid: 3
    components:
    - type: Transform
      parent: 2
";

    [Test]
    public void ScrubDropsLegacyActionEntityAndItsContainerSlot()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithLegacyArtifact, out var scrubbed);

        // One action entity, two legacy component nodes, one container slot.
        Assert.That(scrubbed, Is.EqualTo(4));
        Assert.That(result, Does.Not.Contain("ActionArtifactActivate"),
            "the legacy grant action has no replacement and must not reach the loader");
        Assert.That(result, Does.Not.Contain("- 3"),
            "the dropped action's container slot must be pruned with it");
    }

    [Test]
    public void ScrubStripsLegacyComponentNodesButLeavesTheRenameToTheMigration()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithLegacyArtifact, out _);

        Assert.That(result, Does.Not.Contain("BiasedArtifact"));
        Assert.That(result, Does.Not.Contain("isSuppressed"),
            "the legacy Artifact node goes whole, its fields with it");
        // Renaming the body is triad_migration.yml's job, applied inside the engine's deserializer.
        // The scrub touching prototype ids would hide a broken migration entry instead of failing on it.
        Assert.That(result, Does.Contain("VariedXenoArtifactItem"));
    }

    // An anchored artifact as the pre-fix save purge wrote it: the nodes gone, the graph still naming
    // them, and an unlock session in flight. Field shapes are from a real save.
    private const string ShipWithNodelessArtifact = @"meta:
  format: 7
entities:
- proto: """"
  entities:
  - uid: 1
    components:
    - type: Transform
- proto: ComplexXenoArtifact
  entities:
  - uid: 2
    components:
    - type: Transform
      parent: 1
      pos: 0.5,0.5
      anchored: True
    - type: XenoArtifact
      nodeVertices:
      - invalid
      - invalid
      nodeAdjacencyMatrix:
      - - False
        - True
      - - False
        - False
      nextUnlockTime: -0.8333325
      isGenerationRequired: False
      cachedActiveNodes:
      - invalid
      cachedSegments:
      - - invalid
        - invalid
    - type: XenoArtifactUnlocking
      triggeredNodeIndexes:
      - 0
      endTime: 0
";

    // The same artifact saved whole: both nodes are in the file.
    private const string ShipWithWholeArtifact = @"meta:
  format: 7
entities:
- proto: """"
  entities:
  - uid: 1
    components:
    - type: Transform
- proto: ComplexXenoArtifact
  entities:
  - uid: 2
    components:
    - type: Transform
      parent: 1
      pos: 0.5,0.5
      anchored: True
    - type: XenoArtifact
      nodeVertices:
      - 3
      - 4
      nodeAdjacencyMatrix:
      - - False
        - True
      - - False
        - False
      isGenerationRequired: False
      cachedSegments:
      - - 3
        - 4
    - type: XenoArtifactUnlocking
      triggeredNodeIndexes:
      - 0
      endTime: 0
- proto: XenoArtifactEmp
  entities:
  - uid: 3
    components:
    - type: Transform
      parent: 2
  - uid: 4
    components:
    - type: Transform
      parent: 2
";

    [Test]
    public void ScrubResetsTheGraphOfAnArtifactWhoseNodesAreMissing()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithNodelessArtifact, out var scrubbed);

        // Four graph fields and the unlock session.
        Assert.That(scrubbed, Is.EqualTo(5));
        Assert.That(result, Does.Not.Contain("invalid"), "a graph field naming a missing node survived");
        Assert.That(result, Does.Not.Contain("XenoArtifactUnlocking"), "the unlock session indexes the old graph");
        Assert.That(result, Does.Contain("isGenerationRequired: True"), "the artifact must load owing a graph");
        Assert.That(result, Does.Contain("nextUnlockTime"), "a field outside the graph was touched");
    }

    [Test]
    public void ScrubLeavesAnArtifactWhoseNodesArePresentByteIdentical()
    {
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ShipWithWholeArtifact, out var scrubbed);

        Assert.That(scrubbed, Is.EqualTo(0));
        Assert.That(result, Is.EqualTo(ShipWithWholeArtifact), "a whole artifact and its unlock session must load as saved");
    }

    [Test]
    public void ScrubKeepsLiveComponentsWhoseNamesTheReworkReused()
    {
        // ArtifactAnalyzer was deleted by the rework and re-registered with a new data shape; a save
        // written by the current build legitimately carries it, so the scrub must not eat it.
        const string ship = @"meta:
  format: 7
entities:
- proto: MachineArtifactAnalyzer
  entities:
  - uid: 1
    components:
    - type: Transform
    - type: ArtifactAnalyzer
";

        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(ship, out var scrubbed);

        Assert.That(scrubbed, Is.EqualTo(0));
        Assert.That(result, Is.EqualTo(ship));
    }
}
