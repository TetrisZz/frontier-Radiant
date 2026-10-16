using System;
using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Remotes.Components;
using Content.Shared.Power;
using Content.Shared.Sound.Components;
using Content.Shared.Tools.Components;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class AdditionalItemHandlingSoundsTest
{
    private const string Root = "/Audio/_radiant/Items/handling/";

    [Test]
    public async Task ImportedSoundsResolveAndRemoteStillRespectsPower()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var prototypes = pair.Server.ProtoMan;
            var factory = pair.Server.ResolveDependency<IComponentFactory>();
            var resources = pair.Server.ResolveDependency<IResourceManager>();
            var coinBases = new[]
            {
                "TreasureCoinIron", "FrontierUplinkCoin", "GovernorUplinkCoin",
                "NavyToken", "SeparatistToken", "DonorDecisionToken",
            };
            var coins = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => prototypes.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true)
                    .Any(parent => coinBases.Contains(parent.id)))
                .ToArray();
            var drinkBottles = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => prototypes.EnumerateAllParents<EntityPrototype>(proto.ID)
                    .Any(parent => parent.id is "DrinkBottleGlassBaseFull" or "DrinkBottlePlasticBaseFull"))
                .ToArray();
            Assert.That(coins.Select(proto => proto.ID), Is.SupersetOf(coinBases));
            Assert.That(drinkBottles.Select(proto => proto.ID), Is.SupersetOf(new[]
            {
                "DrinkBeerBottleFull", "DrinkBottleBeer", "DrinkWaterBottleFull",
                "DrinkColaBottleFull", "DrinkCarpLiteBottle", "DrinkWaterBottleLarge",
            }));
            var musicalTapes = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => prototypes.EnumerateAllParents<EntityPrototype>(proto.ID)
                    .Any(parent => parent.id == "BaseRecord"))
                .ToArray();
            var doorRemotes = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => proto.TryGetComponent<DoorRemoteComponent>(out _, factory))
                .ToArray();
            Assert.That(musicalTapes.Select(proto => proto.ID), Does.Contain("RadiantRecord"));
            Assert.That(doorRemotes.Select(proto => proto.ID), Does.Contain("DoorRemoteShipyard"));
            foreach (var (id, pickupName, dropName) in new[]
            {
                ("DrinkGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("DrinkAbsintheGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("DrinkShotGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("DrinkGlassCoupeShaped", "drinkglass_pickup", "drinkglass_drop"),
                ("ChemistryEmptyBottle01", "bottle_pickup", "bottle_drop"),
                ("Beaker", "beaker_pickup", "beaker_drop"),
                ("LargeBeaker", "beaker_pickup", "beaker_drop"),
                ("SheetGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("SheetGlass1", "drinkglass_pickup", "drinkglass_drop"),
                ("SheetRGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("SheetRPGlass", "drinkglass_pickup", "drinkglass_drop"),
                ("SpaceCash", "dosh_pickup", "dosh_drop"),
                ("SpaceCash1000", "dosh_pickup", "dosh_drop"),
                ("CassetteTape", "tape_pickup", "tape_drop"),
                ("CassetteTapeInterview", "tape_pickup", "tape_drop"),
                ("RollingPin", "rolling_pin_pickup", "rolling_pin_drop"),
            }.Concat(musicalTapes.Select(proto => (proto.ID, "tape_pickup", "tape_drop")))
                .Concat(coins.Select(proto => (proto.ID, "coin_pickup", "coin_drop")))
                .Concat(drinkBottles.Select(proto => (proto.ID, "bottle_pickup", "bottle_drop")))
                .Concat(doorRemotes.Select(proto => (proto.ID, "door_remote_pick_up1", "door_remote_drop1"))))
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                Assert.That(proto.TryGetComponent<EmitSoundOnDropComponent>(out var drop, factory), Is.True, id);
                Assert.That(proto.TryGetComponent<EmitSoundOnLandComponent>(out var land, factory), Is.True, id);
                foreach (var (sound, name) in new[]
                {
                    (pickup!.Sound, pickupName), (drop!.Sound, dropName), (land!.Sound, dropName),
                })
                {
                    Assert.That(sound, Is.TypeOf<SoundPathSpecifier>(), id);
                    var path = ((SoundPathSpecifier) sound!).Path;
                    Assert.That(path.ToString(), Is.EqualTo(Root + name + ".wav"), id);
                    Assert.That(resources.ContentFileExists(path), Is.True, path.ToString());
                }
            }
            foreach (var proto in doorRemotes)
            {
                Assert.That(proto.TryGetComponent<DoorRemoteComponent>(out var config, factory), Is.True, proto.ID);
                Assert.That(config!.SwitchSound, Is.TypeOf<SoundCollectionSpecifier>(), proto.ID);
                Assert.That(config.ActionSound, Is.TypeOf<SoundCollectionSpecifier>(), proto.ID);
                Assert.That(((SoundCollectionSpecifier) config.SwitchSound!).Collection,
                    Is.EqualTo("DoorRemoteSwitch"), proto.ID);
                Assert.That(((SoundCollectionSpecifier) config.ActionSound!).Collection,
                    Is.EqualTo("DoorRemoteAction"), proto.ID);
            }
            TestContext.Out.WriteLine($"Checked {musicalTapes.Length} musical tapes and {doorRemotes.Length} door remotes");
            TestContext.Out.WriteLine($"Checked {coins.Length} coins and tokens and {drinkBottles.Length} drink bottles");

            var mug = prototypes.Index<EntityPrototype>("DrinkTeacupEmpty");
            Assert.That(mug.TryGetComponent<EmitSoundOnPickupComponent>(out var mugPickup, factory), Is.True);
            Assert.That(((SoundPathSpecifier) mugPickup!.Sound!).Path.ToString(),
                Is.EqualTo("/Audio/_radiant/Items/other/Cup/pickup.ogg"));
            foreach (var id in new[] { "DrinkWaterCup", "BluespaceBeaker", "SheetPlastic", "SheetSteel" })
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent<EmitSoundOnPickupComponent>(out var pickup, factory), Is.True, id);
                if (pickup!.Sound is SoundPathSpecifier path)
                    Assert.That(path.Path.ToString().StartsWith(Root, StringComparison.Ordinal), Is.False, id);
            }
            foreach (var (collection, count) in new[]
            {
                ("RollingPinRolling", 6), ("DoorRemoteSwitch", 3), ("DoorRemoteAction", 2),
            })
            {
                var definition = prototypes.Index<SoundCollectionPrototype>(collection);
                Assert.That(definition.PickFiles, Has.Count.EqualTo(count));
                foreach (var file in definition.PickFiles)
                    Assert.That(resources.ContentFileExists(file), Is.True, file.ToString());
            }
            var pin = prototypes.Index<EntityPrototype>("RollingPin");
            Assert.That(pin.TryGetComponent<ToolComponent>(out var tool, factory), Is.True);
            Assert.That(((SoundCollectionSpecifier) tool!.UseSound!).Collection, Is.EqualTo("RollingPinRolling"));

            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var remote = em.SpawnEntity("DoorRemoteCommand", map.GridCoords);
            var remoteComp = em.GetComponent<DoorRemoteComponent>(remote);
            Assert.That(((SoundCollectionSpecifier) remoteComp.SwitchSound!).Collection, Is.EqualTo("DoorRemoteSwitch"));
            Assert.That(((SoundCollectionSpecifier) remoteComp.ActionSound!).Collection, Is.EqualTo("DoorRemoteAction"));
            foreach (var mode in new[] { OperatingMode.ToggleBolts, OperatingMode.ToggleEmergencyAccess, OperatingMode.OpenClose })
            {
                var use = new UseInHandEvent(user);
                em.EventBus.RaiseLocalEvent(remote, use);
                Assert.That(remoteComp.Mode, Is.EqualTo(mode));
            }
            var doorCoords = new EntityCoordinates(map.GridCoords.EntityId, map.GridCoords.Position + new Vector2(1, 0));
            var door = em.SpawnEntity("Airlock", doorCoords);
            var power = em.GetComponent<ApcPowerReceiverComponent>(door);
            var airlock = em.GetComponent<AirlockComponent>(door);
            remoteComp.Mode = OperatingMode.ToggleEmergencyAccess;
            power.NeedsPower = false;
            power.Powered = false;
            var powerOff = new PowerChangedEvent(false, 0);
            em.EventBus.RaiseLocalEvent(door, ref powerOff);
            var before = airlock.EmergencyAccess;
            em.EventBus.RaiseLocalEvent(remote, new BeforeRangedInteractEvent(user, remote, door, doorCoords, true));
            Assert.That(airlock.EmergencyAccess, Is.EqualTo(before), "Unpowered doors must remain unchanged");
            power.Powered = true;
            var powerOn = new PowerChangedEvent(true, power.Load);
            em.EventBus.RaiseLocalEvent(door, ref powerOn);
            em.EventBus.RaiseLocalEvent(remote, new BeforeRangedInteractEvent(user, remote, door, doorCoords, true));
            Assert.That(airlock.EmergencyAccess, Is.EqualTo(!before), "Powered remote commands must still work");
            TestContext.Out.WriteLine("Additional item handling inheritance, collections and remote behavior assertions completed");
        });
        await pair.Client.WaitAssertion(() =>
        {
            var cache = pair.Client.ResolveDependency<IResourceCache>();
            var resources = pair.Client.ResolveDependency<IResourceManager>();
            var files = resources.ContentFindFiles(new ResPath(Root))
                .Where(path => path.Extension == "wav").ToArray();
            Assert.That(files, Has.Length.EqualTo(28));
            foreach (var path in files)
            {
                var audio = cache.GetResource<AudioResource>(path).AudioStream;
                Assert.That(audio.ChannelCount, Is.EqualTo(1), path.ToString());
                Assert.That(audio.Length, Is.GreaterThan(TimeSpan.Zero), path.ToString());
            }
            TestContext.Out.WriteLine("All 28 imported mono WAV files loaded by the client");
        });
        await pair.CleanReturnAsync();
    }
}
