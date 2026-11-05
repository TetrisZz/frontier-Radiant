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
    [TestCase(false)]
    [TestCase(true)]
    public void EquipmentCooldownRepairCoversCustomTankActionsAndKeepsUnrelatedAbilities(bool deposit)
    {
        const string yaml = """
            entities:
            - proto: CustomTank
              entities:
              - uid: 1
                components:
                - type: GasTank
                  toggleActionEntity: 2
                - type: UseDelay
                  delays:
                    gasTank:
                      length: 1
                      startTime: 28799
                      endTime: 28800
            - proto: CustomTankAction
              entities:
              - uid: 2
                components:
                - type: Action
                  container: 1
                  attachedEntity: invalid
                  useDelay: 1
                  cooldown:
                    start: 28799
                    end: 28800
            - proto: OtherAbility
              entities:
              - uid: 3
                components:
                - type: Action
                  cooldown:
                    start: 30
                    end: 40
            - proto: RPED
              entities:
              - uid: 4
                components:
                - type: Storage
                  storedItems:
                    5:
                      position: 0,0
            - proto: SuperMatterBinStockPart
              entities:
              - uid: 5
                components:
                - type: Stack
                  count: 7
            """;
        var repaired = deposit
            ? ShipSaveYamlSanitizer.ScrubStoredItemYaml(yaml, out _)
            : ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out _);
        Assert.That(repaired, Does.Not.Contain("28799"));
        Assert.That(repaired, Does.Not.Contain("28800"));
        Assert.That(repaired, Does.Contain("length: 1"));
        Assert.That(repaired, Does.Contain("useDelay: 1"));
        Assert.That(repaired, Does.Contain("container: 1"));
        Assert.That(repaired, Does.Contain("start: 30"));
        Assert.That(repaired, Does.Contain("end: 40"));
        Assert.That(repaired, Does.Contain("count: 7"));
        Assert.That(repaired, Does.Contain("storedItems:"));
        var repeated = deposit
            ? ShipSaveYamlSanitizer.ScrubStoredItemYaml(repaired, out var changes)
            : ShipSaveYamlSanitizer.ScrubShipLoadYaml(repaired, out changes);
        Assert.That(changes, Is.Zero);
        Assert.That(repeated, Is.EqualTo(repaired));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShipTransferClearsOnlySuitButtonCooldownAndPreviousWearer(bool exporting)
    {
        var suit = new MappingDataNode
        {
            { "uid", new ValueDataNode("1") },
            { "components", new SequenceDataNode { new MappingDataNode
                {
                    { "type", new ValueDataNode("ToggleableClothing") },
                    { "actionEntity", new ValueDataNode("2") },
                } } },
        };
        var button = new MappingDataNode
        {
            { "type", new ValueDataNode("Action") },
            { "useDelay", new ValueDataNode("1") },
            { "container", new ValueDataNode("1") },
            { "attachedEntity", new ValueDataNode("invalid") },
            { "cooldown", new MappingDataNode
                { { "start", new ValueDataNode("139.0665276") }, { "end", new ValueDataNode("140.0665276") } } },
        };
        var unrelated = new MappingDataNode
        {
            { "type", new ValueDataNode("Action") },
            { "cooldown", new MappingDataNode { { "start", new ValueDataNode("10") }, { "end", new ValueDataNode("20") } } },
        };
        var unrelatedBefore = unrelated.Copy();
        var root = new MappingDataNode
        {
            { "entities", new SequenceDataNode { new MappingDataNode
                {
                    { "entities", new SequenceDataNode
                        {
                            suit,
                            new MappingDataNode { { "uid", new ValueDataNode("2") }, { "components", new SequenceDataNode { button } } },
                            new MappingDataNode { { "uid", new ValueDataNode("3") }, { "components", new SequenceDataNode { unrelated } } },
                        } },
                } } },
        };
        if (exporting)
            ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!);
        else
            ShipSaveYamlSanitizer.ScrubShipLoadNode(root);
        Assert.That(button.Get<ValueDataNode>("cooldown").IsNull, Is.True);
        Assert.That(button.Get<ValueDataNode>("attachedEntity").IsNull, Is.True);
        Assert.That(button.Get<ValueDataNode>("container").Value, Is.EqualTo("1"));
        Assert.That(button.Get<ValueDataNode>("useDelay").Value, Is.EqualTo("1"));
        Assert.That(unrelated, Is.EqualTo(unrelatedBefore), "Other action cooldowns must retain their normal persistence.");
        Assert.That(ShipSaveYamlSanitizer.ScrubShipLoadNode(root), Is.Zero);
    }

    [Test]
    public void LoadClearsOldRoundUseDelayTimestampsButKeepsDurationsAndNamedTimers()
    {
        const string yaml = "entities:\n- proto: LockerEngineer\n  entities:\n  - uid: 1\n"
            + "    components:\n    - type: UseDelay\n      delay: 1\n      delays:\n"
            + "        default:\n          length: 1\n          startTime: 35999\n          endTime: 36000\n"
            + "        storage:\n          length: 0.5\n          startTime: 35999.5\n          endTime: 36000\n";
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out var scrubbed);
        Assert.That(scrubbed, Is.EqualTo(4));
        Assert.That(result, Does.Not.Contain("startTime"));
        Assert.That(result, Does.Not.Contain("endTime"));
        Assert.That(result, Does.Contain("length: 0.5"));
        Assert.That(result, Does.Contain("default:"));
        Assert.That(result, Does.Contain("storage:"));
        Assert.That(ShipSaveYamlSanitizer.ScrubShipLoadYaml(result, out var repeated), Is.EqualTo(result));
        Assert.That(repeated, Is.Zero);
    }

    [Test]
    public void ExportClearsUseDelayTimestampsWithoutChangingDuration()
    {
        var delay = new MappingDataNode
        {
            { "length", new ValueDataNode("0.5") },
            { "startTime", new ValueDataNode("35999.5") },
            { "endTime", new ValueDataNode("36000") },
        };
        var component = new MappingDataNode
        {
            { "type", new ValueDataNode("UseDelay") },
            { "delays", new MappingDataNode { { "default", delay } } },
        };
        var entity = new MappingDataNode
        {
            { "uid", new ValueDataNode("1") },
            { "components", new SequenceDataNode { component } },
        };
        var root = new MappingDataNode
        {
            { "entities", new SequenceDataNode
                { new MappingDataNode { { "entities", new SequenceDataNode { entity } } } } },
        };
        ShipSaveYamlSanitizer.SanitizeShipSaveNode(root, null!);
        Assert.That(delay.ContainsKey("startTime"), Is.False);
        Assert.That(delay.ContainsKey("endTime"), Is.False);
        Assert.That(delay.Get<ValueDataNode>("length").Value, Is.EqualTo("0.5"));
    }

    [Test]
    public void LoadDropsOldStationRecordKeysWithoutChangingDepartmentAccessOrLock()
    {
        const string yaml = "entities:\n- proto: LockerEngineer\n  entities:\n  - uid: 1\n"
            + "    components:\n    - type: AccessReader\n      access: [[Engineering]]\n"
            + "      accessKeys:\n      - id: 7\n        station: 999\n"
            + "    - type: Lock\n      locked: false\n";
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out var scrubbed);
        Assert.That(scrubbed, Is.EqualTo(1));
        Assert.That(result, Does.Not.Contain("999"));
        Assert.That(result, Does.Contain("Engineering"));
        Assert.That(result, Does.Contain("locked: false"));
        Assert.That(ShipSaveYamlSanitizer.ScrubShipLoadYaml(result, out var repeated), Is.EqualTo(result));
        Assert.That(repeated, Is.Zero);
    }

    [Test]
    public void LoadClearsMissingHelmetActionButKeepsSavedHelmetReference()
    {
        const string yaml = "entities:\n- proto: ClothingOuterHardsuitEngineering\n  entities:\n"
            + "  - uid: 1\n    components:\n    - type: ToggleableClothing\n"
            + "      actionEntity: invalid\n      clothingUid: 2\n"
            + "- proto: ClothingHeadHelmetHardsuitEngineering\n  entities:\n"
            + "  - uid: 2\n    components:\n    - type: AttachedClothing\n      attachedUid: 1\n";
        var result = ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out var scrubbed);
        Assert.That(scrubbed, Is.EqualTo(1));
        Assert.That(result, Does.Not.Contain("invalid"));
        Assert.That(result, Does.Contain("clothingUid: 2"));
        Assert.That(result, Does.Contain("attachedUid: 1"));
        Assert.That(ShipSaveYamlSanitizer.ScrubShipLoadYaml(result, out var repeated), Is.EqualTo(result));
        Assert.That(repeated, Is.Zero);
    }

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
