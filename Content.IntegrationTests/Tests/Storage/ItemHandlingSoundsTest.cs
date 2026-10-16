using System;
using System.Linq;
using Content.Shared.Clothing.Components;
using Content.Shared.Sound.Components;
using Content.Shared.Storage;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class ItemHandlingSoundsTest
{
    private static readonly string[] HandledIds =
    [
        "BoxCardboard", "BaseHumanOrganUnGibbable", "Flash", "BaseSword",
        "Spear", "Pickaxe", "MiningDrill", "WeaponCrusher",
        "BaseKnife", "FireAxe", "Chainsaw", "BaseBallBat",
        "BaseWeaponPistol", "BaseWeaponLightMachineGun", "BaseWeaponSniper", "BaseWeaponHeavyMachineGun",
        "BaseWeaponSubMachineGun", "WeaponFlareGun", "BaseWeaponShotgun", "BaseWeaponRifle",
        "BaseWeaponRevolver", "BaseWeaponBattery", "BoxDonkSoftBase", "Paper",
        "BaseStockPart", "IDCardStandard", "Handcuffs", "Welder",
        "NukeDisk", "NukeDiskFake", "Wirecutter", "Screwdriver",
        "Wrench", "Multitool", "ToolboxBase", "Matchbox",
        "BookBase", "BaseCrowbar", "GasPipeSansLayers", "ResearchDisk",
        "PhoneInstrument", "SecBreachingHammer", "Kanabou", "UnsensoredClothingUniformBase",
        "ClothingShoesBase", "ClothingOuterBase", "ClothingOuterHardsuitBase", "PillCanister",
        "ClothingNeckBronzeheart", "ClothingNeckGoldmedal", "ClothingNeckCargomedal", "ClothingNeckEngineermedal",
        "ClothingNeckMedicalmedal", "ClothingNeckSciencemedal", "ClothingNeckSecuritymedal", "ClothingNeckClownmedal",
        "ClothingMaskBase", "ClothingHeadHelmetBase", "ClothingHeadBase", "RemoteSignaller",
        "ClothingHandsBase", "ClothingBeltBase", "ClothingBackpack", "DrinkBaseCup",
        "BaseItem", "MaintenanceJack",
    ];

    [Test]
    public async Task HandlingSoundsResolveAndPreserveExistingOverrides()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var factory = pair.Server.ResolveDependency<IComponentFactory>();
                var resources = pair.Server.ResolveDependency<IResourceManager>();
                EntityPrototype? GetPrototype(string id)
                {
                    if (prototypes.TryIndex<EntityPrototype>(id, out var prototype))
                        return prototype;
                    Assert.That(prototypes.TryGetMapping<EntityPrototype>(id, out _), Is.True, id);
                    return prototypes.EnumeratePrototypes<EntityPrototype>().FirstOrDefault(candidate =>
                        prototypes.EnumerateAllParents<EntityPrototype>(candidate.ID)
                            .Any(parent => parent.id == id));
                }
                void CheckSound(SoundSpecifier? sound)
                {
                    Assert.That(sound, Is.Not.Null);
                    switch (sound)
                    {
                        case SoundPathSpecifier path:
                            Assert.That(resources.ContentFileExists(path.Path), Is.True, path.Path.ToString());
                            break;
                        case SoundCollectionSpecifier collection:
                            var definition = prototypes.Index<SoundCollectionPrototype>(collection.Collection!);
                            Assert.That(definition.PickFiles, Is.Not.Empty);
                            foreach (var file in definition.PickFiles)
                                Assert.That(resources.ContentFileExists(file), Is.True, file.ToString());
                            break;
                        default:
                            Assert.Fail($"Unexpected sound specifier: {sound}");
                            break;
                    }
                }

                foreach (var id in HandledIds.Concat(new[]
                {
                    "ClothingUniformJumpsuitColorBlack", "ClothingOuterCoatLab",
                    "ClothingHandsGlovesColorBlack", "ClothingMaskGas",
                    "ClothingHeadHatTophat", "WeaponPistolViper", "BookSpaceEncyclopedia",
                }))
                {
                    var prototype = GetPrototype(id);
                    if (prototype == null)
                    {
                        TestContext.Out.WriteLine($"No concrete descendants for unused abstract prototype {id}");
                        continue;
                    }
                    Assert.That(prototype.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnDropComponent>(out var drop, factory), Is.True, id);
                    CheckSound(pickup!.Sound);
                    CheckSound(drop!.Sound);
                    if (prototype.TryGetComponent<EmitSoundOnLandComponent>(out var land, factory))
                        CheckSound(land.Sound);
                }

                foreach (var id in new[]
                {
                    "Cautery", "Drill", "Scalpel", "ScalpelAdvanced", "Retractor",
                    "Hemostat", "Saw", "SawElectric", "Medkit", "MedkitBurn",
                    "MedkitAdvanced", "SurgicalSterileDrape",
                })
                {
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    var medkit = id.StartsWith("Medkit", StringComparison.Ordinal);
                    var drape = id == "SurgicalSterileDrape";
                    var pickupPath = medkit ? "medical/medkit_pickup" :
                        drape ? "medical/drape_pickup" : "medical/surgery_pickup";
                    var dropPath = medkit ? "medical/medkit_drop" :
                        drape ? "handling/cloth_drop" : "medical/metal_drop";
                    Assert.That(prototype.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnDropComponent>(out var drop, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnLandComponent>(out var land, factory), Is.True, id);
                    Assert.That(pickup!.Sound, Is.TypeOf<SoundPathSpecifier>(), id);
                    Assert.That(drop!.Sound, Is.TypeOf<SoundPathSpecifier>(), id);
                    Assert.That(land!.Sound, Is.TypeOf<SoundPathSpecifier>(), id);
                    Assert.That(((SoundPathSpecifier) pickup.Sound!).Path.ToString(),
                        Is.EqualTo($"/Audio/_radiant/Items/{pickupPath}.ogg"), id);
                    Assert.That(((SoundPathSpecifier) drop.Sound!).Path.ToString(),
                        Is.EqualTo($"/Audio/_radiant/Items/{dropPath}.ogg"), id);
                    Assert.That(((SoundPathSpecifier) land.Sound!).Path.ToString(),
                        Is.EqualTo($"/Audio/_radiant/Items/{dropPath}.ogg"), id);
                    CheckSound(pickup.Sound);
                    CheckSound(drop.Sound);
                    CheckSound(land.Sound);
                    if (!medkit)
                        continue;
                    Assert.That(prototype.TryGetComponent<StorageComponent>(out var storage, factory), Is.True, id);
                    CheckSound(storage!.StorageOpenSound);
                    CheckSound(storage.StorageCloseSound);
                    Assert.That(((SoundPathSpecifier) storage.StorageOpenSound!).Path.ToString(),
                        Is.EqualTo("/Audio/_radiant/Items/medical/medkit_latch.ogg"), id);
                    Assert.That(((SoundPathSpecifier) storage.StorageCloseSound!).Path.ToString(),
                        Is.EqualTo("/Audio/_radiant/Items/medical/medkit_latch.ogg"), id);
                }

                foreach (var (id, fabric) in new[]
                {
                    ("SheetSteel", false), ("SheetSteel10", false), ("SheetSteel1", false),
                    ("SheetBrass", false), ("SheetPlasteel", false), ("SheetUranium", false),
                    ("IngotGold", false), ("IngotGold1", false), ("IngotSilver", false),
                    ("PartRodMetal", false), ("PartRodMetal10", false), ("PartRodMetal1", false),
                    ("MaterialCloth", true), ("MaterialCloth10", true), ("MaterialCloth1", true),
                    ("MaterialDurathread", true), ("MaterialDurathread1", true),
                })
                {
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnDropComponent>(out var drop, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnLandComponent>(out var land, factory), Is.True, id);
                    var pickupPath = fabric ? "handling/cloth_pickup" : "medical/surgery_pickup";
                    var dropPath = fabric ? "handling/cloth_drop" : "medical/metal_drop";
                    foreach (var (sound, path) in new[]
                    {
                        (pickup!.Sound, pickupPath), (drop!.Sound, dropPath), (land!.Sound, dropPath),
                    })
                    {
                        CheckSound(sound);
                        Assert.That(sound, Is.TypeOf<SoundPathSpecifier>(), id);
                        Assert.That(((SoundPathSpecifier) sound!).Path.ToString(),
                            Is.EqualTo($"/Audio/_radiant/Items/{path}.ogg"), id);
                    }
                    Assert.That(prototype.TryGetComponent<EmitSoundOnThrowComponent>(out _, factory), Is.False, id);
                }

                foreach (var id in new[] { "SheetGlass", "SheetPlastic", "SheetPlasma", "MaterialDiamond", "MaterialWoodPlank" })
                {
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                    Assert.That(pickup!.Sound, Is.TypeOf<SoundCollectionSpecifier>(), id);
                    Assert.That(((SoundCollectionSpecifier) pickup.Sound!).Collection, Is.EqualTo("GenericPickup"), id);
                }

                // Check actual game weapons, not just legacy abstract weapon bases.
                foreach (var (id, category) in new[]
                {
                    ("NFWeaponPistolMk58", "Pistols"),
                    ("NFWeaponPistolViper", "Pistols"),
                    ("WeaponPistolVP78", "Pistols"),
                    ("PhX", "Pistols"),
                    ("NFWeaponRevolverInspector", "Revolvers"),
                    ("NFWeaponSubMachineGunAtreides", "SMG"),
                    ("NFWeaponShotgunKammerer", "Shotguns"),
                    ("NFWeaponRifleBarlowsBolt", "Rifles"),
                    ("WeaponRifleTW44Acorp", "Rifles"),
                    ("NFWeaponRifleSniperHristov", "Snipers"),
                    ("NFWeaponLightMachineGunL6", "Rifles"),
                    ("NFWeaponLauncherChinaLake", "Rifles"),
                    ("NFWeaponEnergyPistolLaserSvalinn", "Lasers"),
                    ("WeaponLaserCarbineAcorp", "Lasers"),
                })
                {
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnDropComponent>(out var drop, factory), Is.True, id);
                    Assert.That(prototype.TryGetComponent<EmitSoundOnLandComponent>(out var land, factory), Is.True, id);
                    Assert.That(pickup!.Sound, Is.TypeOf<SoundCollectionSpecifier>(), id);
                    Assert.That(drop!.Sound, Is.TypeOf<SoundCollectionSpecifier>(), id);
                    Assert.That(land!.Sound, Is.TypeOf<SoundCollectionSpecifier>(), id);
                    Assert.That(((SoundCollectionSpecifier) pickup.Sound!).Collection, Is.EqualTo(category + "PickUp"), id);
                    Assert.That(((SoundCollectionSpecifier) drop.Sound!).Collection, Is.EqualTo(category + "Drop"), id);
                    Assert.That(((SoundCollectionSpecifier) land.Sound!).Collection, Is.EqualTo(category + "Drop"), id);
                    CheckSound(pickup.Sound);
                    CheckSound(drop.Sound);
                    CheckSound(land.Sound);
                }

                foreach (var id in new[]
                {
                    "ClothingUniformJumpsuitColorBlack", "ClothingOuterCoatLab",
                    "ClothingHandsGlovesColorBlack", "ClothingMaskGas", "ClothingHeadHatTophat",
                    "ClothingShoesColorBlack", "ClothingBeltUtility", "ClothingBackpack",
                })
                {
                    var prototype = GetPrototype(id);
                    Assert.That(prototype, Is.Not.Null, id);
                    Assert.That(prototype!.TryGetComponent<ClothingComponent>(out var clothing, factory), Is.True, id);
                    CheckSound(clothing!.EquipSound);
                    CheckSound(clothing.UnequipSound);
                }

                Assert.That(GetPrototype("ClothingUniformJumpsuitColorBlack")!
                    .TryGetComponent<ClothingComponent>(out var uniform, factory), Is.True);
                Assert.That(((SoundPathSpecifier) uniform!.EquipSound!).Path.ToString(),
                    Is.EqualTo("/Audio/Items/jumpsuit_equip.ogg"));
                Assert.That(GetPrototype("ClothingBeltUtility")!
                    .TryGetComponent<ClothingComponent>(out var belt, factory), Is.True);
                Assert.That(((SoundPathSpecifier) belt!.EquipSound!).Path.ToString(),
                    Is.EqualTo("/Audio/Items/belt_equip.ogg"));
                Assert.That(GetPrototype("DonorDecisionToken")!
                    .TryGetComponent<EmitSoundOnLandComponent>(out var baseLand, factory), Is.True);
                Assert.That(((SoundPathSpecifier) baseLand!.Sound!).Path.ToString(),
                    Is.EqualTo("/Audio/Effects/drop.ogg"));
                TestContext.Out.WriteLine("Item handling sound resources, inheritance and existing override assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Item handling sound regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
