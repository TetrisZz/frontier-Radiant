using Robust.Client.GameObjects;
using Content.Shared.VendingMachines;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Sprite;

[TestFixture]
public sealed class RadiantClothingImportTest
{
    [Test]
    public async Task ImportedClothingAndVapeStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client;
            var sprites = client.System<SpriteSystem>();
            foreach (var id in new[]
            {
                "ClothingUniformRadiantReallyBlackSuit", "ClothingUniformRadiantBlackSuitFem",
                "ClothingUniformRadiantReallyBlackSuitSkirt", "ClothingUniformRadiantBlackSuitFemSkirt",
                "ClothingUniformRadiantParamedicDark", "ClothingUniformRadiantParamedicDarkSkirt",
                "ClothingOuterCoatNavyGoldLong2", "ClothingOuterCoatNavyGoldLong",
                "ClothingOuterCloakNavyKomandor", "ClothingOuterCloakNavyCaptain",
                "ClothingUniformRadiantWhiteTshirt", "ClothingUniformRadiantGrayTshirt",
                "ClothingUniformRadiantSlimBodyShorts", "ClothingOuterCoatOccult",
                "ClothingUnderwearBottomRadiantStriped", "ClothingUnderwearBottomRadiantBee",
                "DisposableVapeBlack", "DisposableVapeBlue", "DisposableVapeGreen", "DisposableVapeOrange",
                "DisposableVapePurple", "DisposableVapeRed", "DisposableVapeWhite", "DisposableVapeYellow",
            })
            {
                var uid = client.EntMan.Spawn(id);
                var sprite = client.EntMan.GetComponent<SpriteComponent>(uid);
                Assert.That(sprite.Icon, Is.Not.Null, id);
                Assert.That(sprites.TryGetLayer((uid, sprite), 0, out var layer, false), Is.True, id);
                var rsi = layer.ActualRsi;
                Assert.That(rsi, Is.Not.Null, id);
                var slot = id.StartsWith("DisposableVape") ? "MASK"
                    : id.StartsWith("ClothingNeck") ? "NECK"
                    : id.StartsWith("ClothingOuter") ? "OUTERCLOTHING"
                    : id.StartsWith("ClothingHead") ? "HEAD"
                    : id.StartsWith("ClothingUnderwear") ? "UNDERWEARB" : "INNERCLOTHING";
                Assert.That(rsi!.TryGetState("equipped-" + slot, out var worn), Is.True, id);
                if (id is "ClothingUniformRadiantWhiteTshirt" or "ClothingUniformRadiantGrayTshirt")
                    foreach (var hand in new[] { "left", "right" })
                        Assert.That(rsi.TryGetState("inhand-" + hand, out _), Is.True, id);
                if (id.Contains("ParamedicDark"))
                    Assert.That(rsi.TryGetState("folded-equipped-INNERCLOTHING", out _), Is.True, id);
                if (slot == "UNDERWEARB")
                    foreach (var species in new[] { "feroxi", "vulpkanin", "tajaran", "reptilian" })
                        Assert.That(rsi.TryGetState("equipped-UNDERWEARB-" + species, out _), Is.True, id + species);
                client.EntMan.DeleteEntity(uid);
            }
            TestContext.Out.WriteLine("Imported clothing sprites and in-hand states assertions completed");
        });
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = pair.Server.ResolveDependency<IComponentFactory>();
            foreach (var (role, oldCloak, newCloak) in new[]
            {
                ("Komandor", "ClothingOuterCoatNavyGoldLong2", "ClothingOuterCloakNavyKomandor"),
                ("Captain", "ClothingOuterCoatNavyGoldLong", "ClothingOuterCloakNavyCaptain"),
            })
            {
                var group = prototypes.Index<LoadoutGroupPrototype>($"NAVY{role}OuterClothing");
                Assert.That(group.Hidden, Is.False, role);
                Assert.That(group.MaxLimit, Is.EqualTo(1), role);
                foreach (var cloak in new[] { oldCloak, newCloak })
                {
                    Assert.That(group.Loadouts, Does.Contain(new ProtoId<LoadoutPrototype>("NAVY" + cloak)), role);
                    var loadout = prototypes.Index<LoadoutPrototype>("NAVY" + cloak);
                    Assert.That(loadout.Equipment.TryGetValue("outerClothing", out var item), Is.True, cloak);
                    Assert.That(item.Id, Is.EqualTo(cloak));
                    var entity = prototypes.Index<EntityPrototype>(cloak);
                    Assert.That(entity.TryGetComponent<ClothingComponent>(out var clothing, factory), Is.True, cloak);
                    Assert.That(clothing!.Slots, Is.EqualTo(SlotFlags.OUTERCLOTHING), cloak);
                }
                var neck = prototypes.Index<LoadoutGroupPrototype>($"NAVY{role}Neck");
                Assert.That(neck.Loadouts, Does.Not.Contain(new ProtoId<LoadoutPrototype>("NAVY" + newCloak)));
            }
            TestContext.Out.WriteLine("Fleet old and new outer clothing selection assertions completed");
            var inventory = prototypes.Index<VendingMachineInventoryPrototype>("XomkaVendInventory");
            foreach (var id in new[]
            {
                "ClothingUniformRadiantWhiteTshirt", "ClothingUniformRadiantGrayTshirt",
                "ClothingUniformRadiantSlimBodyShorts", "ClothingOuterCoatOccult",
            })
            {
                Assert.That(prototypes.HasIndex<EntityPrototype>(id), Is.True, id);
                Assert.That(inventory.StartingInventory.TryGetValue(id, out var amount), Is.True, id);
                Assert.That(amount, Is.EqualTo(uint.MaxValue), id);
            }
            TestContext.Out.WriteLine("HomkoMat inventory assertions completed");
        });
        await pair.CleanReturnAsync();
    }
}
