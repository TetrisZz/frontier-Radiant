using System.Collections.Generic;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Interaction.Panel;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class InteractionClothingTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: InteractionClothingDummy
  components:
  - type: Inventory
  - type: ContainerContainer

- type: entity
  id: InteractionClothingItem
  components:
  - type: Item
    size: Tiny
";

    [Test]
    public async Task ParticipantSlotsAreIndependentAndOverrideLegacyRules()
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
                var inventory = entities.System<InventorySystem>();
                var clothing = entities.System<InteractionClothingSystem>();
                var user = entities.SpawnEntity("InteractionClothingDummy", map.GridCoords);
                var target = entities.SpawnEntity("InteractionClothingDummy", map.GridCoords);
                var action = prototypes.Index<InteractionPrototype>("InteractionLickBreasts");

                Assert.That(action.UserBlockedClothingSlots, Is.EquivalentTo(new[] { "mask", "head" }));
                Assert.That(action.TargetBlockedClothingSlots,
                    Is.EquivalentTo(new[] { "jumpsuit", "outerClothing", "underweart" }));
                Assert.That(clothing.IsBlocked(user, target, action, out _), Is.False);

                void CheckSlot(EntityUid participant, string slot, InteractionPrototype rule,
                    bool expectedBlocked, bool expectedTargetBlocked)
                {
                    var item = entities.SpawnEntity("InteractionClothingItem", map.GridCoords);
                    Assert.That(inventory.TryEquip(participant, item, slot, silent: true, force: true),
                        Is.True, $"Equip {slot}");
                    Assert.That(clothing.IsBlocked(user, target, rule, out var targetBlocked),
                        Is.EqualTo(expectedBlocked), $"Blocked: {participant}, {slot}");
                    Assert.That(targetBlocked, Is.EqualTo(expectedTargetBlocked), $"Participant: {slot}");
                    Assert.That(inventory.TryUnequip(participant, slot, silent: true, force: true), Is.True);
                    Assert.That(clothing.IsBlocked(user, target, rule, out _), Is.False,
                        $"Available after removing {slot}");
                    entities.DeleteEntity(item);
                }

                foreach (var slot in new[] { "mask", "head" })
                {
                    CheckSlot(user, slot, action, true, false);
                    CheckSlot(target, slot, action, false, false);
                }
                foreach (var slot in new[] { "jumpsuit", "outerClothing", "underweart" })
                {
                    CheckSlot(target, slot, action, true, true);
                    CheckSlot(user, slot, action, false, false);
                }
                CheckSlot(user, "underwearb", action, false, false);
                CheckSlot(target, "underwearb", action, false, false);

                var legacy = new InteractionPrototype
                {
                    ID = "InteractionClothingLegacyTest",
                    Name = "Clothing rule test",
                    RequiredClothingSlots = new() { "jumpsuit" },
                    OneRequiredClothingSlots = new() { "gloves" },
                };
                CheckSlot(user, "jumpsuit", legacy, true, false);
                CheckSlot(target, "jumpsuit", legacy, true, true);
                CheckSlot(target, "gloves", legacy, true, true);
                CheckSlot(user, "gloves", legacy, false, false);
                legacy.UserBlockedClothingSlots = new();
                legacy.TargetBlockedClothingSlots = new();
                CheckSlot(user, "jumpsuit", legacy, false, false);
                CheckSlot(target, "jumpsuit", legacy, false, false);
                CheckSlot(target, "gloves", legacy, false, false);

                var exported = new InteractionPanelManager().ToDataNode(legacy);
                var imported = server.ResolveDependency<ISerializationManager>()
                    .Read<List<InteractionPrototype>>(exported, notNullableOverride: true)[0];
                Assert.That(imported.UserBlockedClothingSlots, Is.Empty);
                Assert.That(imported.TargetBlockedClothingSlots, Is.Empty);
                CheckSlot(user, "jumpsuit", imported, false, false);
                CheckSlot(target, "jumpsuit", imported, false, false);

                foreach (var id in new[] { "InteractionUnbuttonShirt", "InteractionGropeBreastOver", "InteractionTouchAss" })
                {
                    var overClothing = prototypes.Index<InteractionPrototype>(id);
                    Assert.That(overClothing.UserBlockedClothingSlots, Is.Empty, id);
                    Assert.That(overClothing.TargetBlockedClothingSlots, Is.Empty, id);
                    CheckSlot(user, "jumpsuit", overClothing, false, false);
                    CheckSlot(target, "jumpsuit", overClothing, false, false);
                }

                var kiss = prototypes.Index<InteractionPrototype>("InteractionKissLips");
                CheckSlot(user, "mask", kiss, true, false);
                CheckSlot(target, "mask", kiss, true, true);
                CheckSlot(user, "head", kiss, true, false);
                CheckSlot(target, "head", kiss, true, true);

                var solo = prototypes.Index<InteractionPrototype>("InteractionSoloTouchChest");
                var top = entities.SpawnEntity("InteractionClothingItem", map.GridCoords);
                Assert.That(inventory.TryEquip(user, top, "underweart", silent: true, force: true), Is.True);
                Assert.That(clothing.IsBlocked(user, null, solo, out var soloTargetBlocked), Is.True);
                Assert.That(soloTargetBlocked, Is.False);
                Assert.That(inventory.TryUnequip(user, "underweart", silent: true, force: true), Is.True);
                Assert.That(clothing.IsBlocked(user, null, solo, out _), Is.False);

                var unrestricted = prototypes.Index<InteractionPrototype>("InteractionSoloBlush");
                CheckSlot(user, "jumpsuit", unrestricted, false, false);
                CheckSlot(user, "mask", unrestricted, false, false);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Interaction clothing regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
