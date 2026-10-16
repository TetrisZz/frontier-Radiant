using System.Collections.Generic;
using System.Linq;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction.Panel;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class InteractionBodyFeatureTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: InteractionFeatureDummy
  components:
  - type: HumanoidAppearance
    species: Human

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeatureNative
  components:
  - type: HumanoidAppearance
    species: Vulpkanin

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeatureSlime
  components:
  - type: HumanoidAppearance
    species: SlimePerson

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeatureMoth
  components:
  - type: HumanoidAppearance
    species: Moth

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeatureHarpy
  components:
  - type: HumanoidAppearance
    species: Harpy

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeatureResomi
  components:
  - type: HumanoidAppearance
    species: Resomi

- type: entity
  parent: InteractionFeatureDummy
  id: InteractionFeaturePhysical
  components:
  - type: Body
    prototype: InteractionFeatureBody

- type: body
  id: InteractionFeatureBody
  name: Interaction feature body
  root: tail
  slots:
    tail:
      part: InteractionFeatureTail

- type: entity
  id: InteractionFeatureTail
  components:
  - type: BodyPart
    partType: Tail
";

    [Test]
    public async Task SelectedInteractionsRespectTheirRolesAndFeatures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        try
        {
            await server.WaitAssertion(() =>
            {
                var entities = server.ResolveDependency<IEntityManager>();
                var prototypes = server.ResolveDependency<IPrototypeManager>();
                var localization = server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();
                var features = entities.System<InteractionBodyFeatureSystem>();
                var ids = new[]
                {
                    "InteractionHugShoulders",
                    "InteractionRuffleHair",
                    "InteractionBrushStrand",
                    "InteractionSoloSlimeBubble",
                    "InteractionSoloSlimeShape",
                    "InteractionSoloSlimeWave",
                    "InteractionTouchHornsTogether",
                    "InteractionSoloPreenFeather",
                    "InteractionSoloStretchNeck",
                    "InteractionSoloRollShoulders",
                    "InteractionSoloRubNoseBridge",
                    "InteractionSoloSmoothHair",
                    "InteractionStrokeEars",
                    "InteractionSoloTailAroundLegs",
                    "InteractionSoloHideTail",
                    "InteractionTailOnLap",
                    "InteractionSoloAlertAntennae",
                    "InteractionSoloShakeFoliage",
                    "InteractionHugWaist",
                    "InteractionHeadOnChest",
                    "InteractionSoloShyEarsSmile",
                    "InteractionTailAroundHand",
                    "InteractionWingHug",
                    "InteractionSoloSlimeHeart",
                    "InteractionBoopNose",
                    "InteractionKissForehead",
                    "InteractionKissLips",
                    "InteractionKissHand",
                    "InteractionNuzzle",
                    "InteractionSoloCrossArms",
                    "InteractionTajaranTailEntwine",
                };
                Assert.That(ids.Distinct().Count(), Is.EqualTo(31));
                foreach (var id in ids)
                {
                    var action = prototypes.Index<InteractionPrototype>(id);
                    Assert.That(action.ERP, Is.False, id);
                    Assert.That(localization.HasString(action.Name), Is.True, action.Name);
                    Assert.That(action.UserMessages, Is.Not.Empty, id);
                    if (!action.Solo)
                        Assert.That(action.TargetMessages, Is.Not.Empty, id);
                    foreach (var key in action.UserMessages.Concat(action.TargetMessages).Concat(action.OtherMessages))
                        Assert.That(localization.HasString(key), Is.True, key);
                }

                var human = entities.SpawnEntity("InteractionFeatureDummy", map.GridCoords);
                var partner = entities.SpawnEntity("InteractionFeatureDummy", map.GridCoords);
                var slime = entities.SpawnEntity("InteractionFeatureSlime", map.GridCoords);
                var moth = entities.SpawnEntity("InteractionFeatureMoth", map.GridCoords);
                var harpy = entities.SpawnEntity("InteractionFeatureHarpy", map.GridCoords);
                var resomi = entities.SpawnEntity("InteractionFeatureResomi", map.GridCoords);
                var appearance = entities.GetComponent<HumanoidAppearanceComponent>(human);
                var partnerAppearance = entities.GetComponent<HumanoidAppearanceComponent>(partner);
                appearance.MarkingSet.Clear();
                partnerAppearance.MarkingSet.Clear();

                void AddMarking(HumanoidAppearanceComponent targetAppearance, string id)
                {
                    var marking = prototypes.Index<MarkingPrototype>(id);
                    if (!targetAppearance.MarkingSet.Markings.TryGetValue(marking.MarkingCategory, out var markings))
                        targetAppearance.MarkingSet.Markings[marking.MarkingCategory] = markings = new();
                    markings.Add(marking.AsMarking());
                }

                foreach (var id in new[]
                         {
                             "InteractionSoloSlimeBubble", "InteractionSoloSlimeShape",
                             "InteractionSoloSlimeWave", "InteractionSoloSlimeHeart",
                         })
                {
                    var action = prototypes.Index<InteractionPrototype>(id);
                    Assert.That(action.Solo, Is.True, id);
                    Assert.That(features.IsAllowed(slime, null, action), Is.True, id);
                    Assert.That(features.IsAllowed(human, null, action), Is.False, id);
                }

                var feathers = prototypes.Index<InteractionPrototype>("InteractionSoloPreenFeather");
                Assert.That(feathers.Solo, Is.True);
                Assert.That(features.IsAllowed(harpy, null, feathers), Is.True);
                Assert.That(features.IsAllowed(resomi, null, feathers), Is.True);
                Assert.That(features.IsAllowed(moth, null, feathers), Is.False);
                Assert.That(features.IsAllowed(human, null, feathers), Is.False);
                AddMarking(appearance, "HarpyChestDefault");
                Assert.That(features.IsAllowed(human, null, feathers), Is.True);

                var wingHug = prototypes.Index<InteractionPrototype>("InteractionWingHug");
                Assert.That(features.IsAllowed(human, partner, wingHug), Is.False);
                Assert.That(features.IsAllowed(harpy, partner, wingHug), Is.True);
                Assert.That(features.IsAllowed(moth, partner, wingHug), Is.True);
                AddMarking(appearance, "MothWingsDefault");
                Assert.That(features.IsAllowed(human, partner, wingHug), Is.True);
                appearance.PermanentlyHidden.Add(HumanoidVisualLayers.Tail);
                Assert.That(features.IsAllowed(human, partner, wingHug), Is.False);
                appearance.PermanentlyHidden.Clear();

                var antennae = prototypes.Index<InteractionPrototype>("InteractionSoloAlertAntennae");
                Assert.That(antennae.Solo, Is.True);
                Assert.That(features.IsAllowed(moth, null, antennae), Is.True);
                Assert.That(features.IsAllowed(human, null, antennae), Is.False);
                AddMarking(appearance, "MothAntennasDefault");
                Assert.That(features.IsAllowed(human, null, antennae), Is.True);
                appearance.MarkingSet.Clear();
                AddMarking(appearance, "MothAntennasNone");
                Assert.That(features.IsAllowed(human, null, antennae), Is.False);

                var foliage = prototypes.Index<InteractionPrototype>("InteractionSoloShakeFoliage");
                Assert.That(foliage.Solo, Is.True);
                Assert.That(features.IsAllowed(human, null, foliage), Is.False);
                AddMarking(appearance, "DionaLeafyHeadTop");
                Assert.That(features.IsAllowed(human, null, foliage), Is.True);
                appearance.MarkingSet.Clear();

                var horns = prototypes.Index<InteractionPrototype>("InteractionTouchHornsTogether");
                Assert.That(features.IsAllowed(human, partner, horns), Is.False);
                AddMarking(appearance, "DemonHornsSmall");
                Assert.That(features.IsAllowed(human, partner, horns), Is.False);
                AddMarking(partnerAppearance, "DemonHornsSmall");
                Assert.That(features.IsAllowed(human, partner, horns), Is.True);
                appearance.MarkingSet.Clear();

                foreach (var id in new[] { "InteractionTailOnLap", "InteractionTailAroundHand" })
                {
                    var action = prototypes.Index<InteractionPrototype>(id);
                    Assert.That(action.Solo, Is.False, id);
                    Assert.That(features.IsAllowed(human, partner, action), Is.False, id);
                }
                AddMarking(appearance, "FelinidTailBasic");
                foreach (var id in new[] { "InteractionTailOnLap", "InteractionTailAroundHand" })
                    Assert.That(features.IsAllowed(human, partner,
                        prototypes.Index<InteractionPrototype>(id)), Is.True, id);

                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                TestContext.Out.WriteLine("Interaction selected assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Interaction selected regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AppendagesFollowCurrentAppearanceAndAttachedParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        try
        {
            await server.WaitAssertion(() =>
            {
                var entities = server.ResolveDependency<IEntityManager>();
                var prototypes = server.ResolveDependency<IPrototypeManager>();
                var features = entities.System<InteractionBodyFeatureSystem>();
                var user = entities.SpawnEntity("InteractionFeatureDummy", map.GridCoords);
                var target = entities.SpawnEntity("InteractionFeatureDummy", map.GridCoords);
                var userAppearance = entities.GetComponent<HumanoidAppearanceComponent>(user);
                var targetAppearance = entities.GetComponent<HumanoidAppearanceComponent>(target);
                userAppearance.MarkingSet.Clear();
                targetAppearance.MarkingSet.Clear();

                var tailAction = prototypes.Index<InteractionPrototype>("InteractionTouchTail");
                var earAction = prototypes.Index<InteractionPrototype>("InteractionScratchBehindEars");
                var hornAction = prototypes.Index<InteractionPrototype>("InteractionTouchHorns");
                foreach (var action in new[] { tailAction, earAction, hornAction })
                {
                    Assert.That(action.NearestAllowedSpecies, Does.Contain("all"), action.ID);
                    Assert.That(features.IsAllowed(user, target, action), Is.False, action.ID);
                }

                void AddMarking(HumanoidAppearanceComponent appearance, string id)
                {
                    var marking = prototypes.Index<MarkingPrototype>(id);
                    appearance.MarkingSet.Markings[marking.MarkingCategory] = new() { marking.AsMarking() };
                }

                AddMarking(targetAppearance, "FelinidTailBasic");
                Assert.That(features.IsAllowed(user, target, tailAction), Is.True);
                Assert.That(features.IsAllowed(user, target, earAction), Is.False);
                AddMarking(targetAppearance, "FelinidEarsBasic");
                Assert.That(features.IsAllowed(user, target, earAction), Is.True);
                Assert.That(features.IsAllowed(user, target, hornAction), Is.False);
                AddMarking(targetAppearance, "DemonHornsSmall");
                Assert.That(features.IsAllowed(user, target, hornAction), Is.True);
                Assert.That(features.IsAllowed(user, target, earAction), Is.False,
                    "A horn marking on HeadTop is not an ear marking");
                targetAppearance.MarkingSet.Markings[MarkingCategories.HeadTop]
                    .Add(prototypes.Index<MarkingPrototype>("FelinidEarsBasic").AsMarking());
                Assert.That(features.HasRequiredFeatures(target,
                    new() { HumanoidBodyFeature.Ears, HumanoidBodyFeature.Horns }), Is.True);

                targetAppearance.MarkingSet.Clear();
                AddMarking(targetAppearance, "MothWingsDefault");
                Assert.That(features.IsAllowed(user, target, tailAction), Is.False,
                    "Wings on the Tail layer are not a tail");
                targetAppearance.MarkingSet.Clear();
                targetAppearance.CustomBaseLayers[HumanoidVisualLayers.Tail] = new("MobDemonTail");
                Assert.That(features.HasFeature(target, HumanoidBodyFeature.Tail), Is.True);
                targetAppearance.CustomBaseLayers.Clear();
                AddMarking(targetAppearance, "FelinidTailBasic");
                targetAppearance.HiddenLayers[HumanoidVisualLayers.Tail] = SlotFlags.OUTERCLOTHING;
                Assert.That(features.HasFeature(target, HumanoidBodyFeature.Tail), Is.True,
                    "Equipment conceals rather than removes appendages");
                targetAppearance.PermanentlyHidden.Add(HumanoidVisualLayers.Tail);
                Assert.That(features.HasFeature(target, HumanoidBodyFeature.Tail), Is.False);
                targetAppearance.PermanentlyHidden.Clear();
                targetAppearance.CustomBaseLayers[HumanoidVisualLayers.Tail] = new(null);
                Assert.That(features.HasFeature(target, HumanoidBodyFeature.Tail), Is.False);
                targetAppearance.CustomBaseLayers.Clear();

                var solo = prototypes.Index<InteractionPrototype>("InteractionSoloTailLift");
                Assert.That(features.IsAllowed(user, null, solo), Is.False);
                AddMarking(userAppearance, "FelinidTailBasic");
                Assert.That(features.IsAllowed(user, null, solo), Is.True);
                Assert.That(features.IsAllowed(user, null, tailAction), Is.False);

                var both = prototypes.Index<InteractionPrototype>("InteractionTajaranTailEntwine");
                Assert.That(both.AllowedSpecies, Does.Contain("all"));
                Assert.That(both.NearestAllowedSpecies, Does.Contain("all"));
                Assert.That(features.IsAllowed(user, target, both), Is.True);
                userAppearance.MarkingSet.Clear();
                Assert.That(features.IsAllowed(user, target, both), Is.False);
                AddMarking(userAppearance, "FelinidTailBasic");
                targetAppearance.MarkingSet.Clear();
                Assert.That(features.IsAllowed(user, target, both), Is.False);

                var native = entities.SpawnEntity("InteractionFeatureNative", map.GridCoords);
                Assert.That(features.HasFeature(native, HumanoidBodyFeature.Tail), Is.True);
                Assert.That(features.HasFeature(native, HumanoidBodyFeature.Ears), Is.True);
                entities.GetComponent<HumanoidAppearanceComponent>(native).MarkingSet.Clear();
                Assert.That(features.HasFeature(native, HumanoidBodyFeature.Tail), Is.False);
                Assert.That(features.HasFeature(native, HumanoidBodyFeature.Ears), Is.False);

                var physical = entities.SpawnEntity("InteractionFeaturePhysical", map.GridCoords);
                entities.GetComponent<HumanoidAppearanceComponent>(physical).MarkingSet.Clear();
                Assert.That(features.HasFeature(physical, HumanoidBodyFeature.Tail), Is.True);
                var part = entities.System<SharedBodySystem>()
                    .GetBodyChildrenOfType(physical, BodyPartType.Tail).Single().Id;
                AddMarking(entities.GetComponent<HumanoidAppearanceComponent>(physical), "FelinidTailBasic");
                entities.DeleteEntity(part);
                Assert.That(features.HasFeature(physical, HumanoidBodyFeature.Tail), Is.False);

                var custom = new InteractionPrototype
                {
                    ID = "InteractionAppendageTest",
                    Name = "Appendage rule test",
                    UserRequiredBodyFeatures = new() { HumanoidBodyFeature.Tail },
                    TargetRequiredBodyFeatures = new() { HumanoidBodyFeature.Ears, HumanoidBodyFeature.Horns },
                };
                var data = new InteractionPanelManager().ToDataNode(custom);
                var imported = server.ResolveDependency<ISerializationManager>()
                    .Read<List<InteractionPrototype>>(data, notNullableOverride: true)[0];
                Assert.That(imported.UserRequiredBodyFeatures, Is.EqualTo(custom.UserRequiredBodyFeatures));
                Assert.That(imported.TargetRequiredBodyFeatures, Is.EqualTo(custom.TargetRequiredBodyFeatures));
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                TestContext.Out.WriteLine("Interaction appendage assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Interaction appendage regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
