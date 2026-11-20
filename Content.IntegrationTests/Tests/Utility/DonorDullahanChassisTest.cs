using System.Linq;
using Content.Client.Animations;
using Content.Shared._radiant.Borgs;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Rotation;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class DonorDullahanChassisTest
{
    [TestCase("Dullahan")]
    [TestCase("MMeka")]
    public async Task DonorModelsKeepStandardProfessionsAndSpawnWithBrainAndBattery(string model)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        var roles = new[] { "Mining", "Janitor", "Service", "Medical", "Engineer" };
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var prototypes = pair.Server.ProtoMan;
                var serialization = pair.Server.ResolveDependency<ISerializationManager>();
                var containers = em.System<SharedContainerSystem>();
                Assert.That(prototypes.Index<EntityPrototype>("BaseHomUplinkRadio")
                    .TryGetComponent<StoreComponent>(out var store), Is.True);
                Assert.That(store!.Categories.Contains("UplinkHomBorgs"), Is.True,
                    "The new cyborg category must be accessible from the donor uplink.");
                var advanced = prototypes.Index<ListingPrototype>("UplinkBorgChassisDonat");
                Assert.That(advanced.Categories.Select(category => category.ToString()), Is.EquivalentTo(new[] { "UplinkHomBorgs" }));
                Assert.That(advanced.Cost["Cristalhom"], Is.EqualTo(FixedPoint2.New(30)));
                foreach (var role in roles)
                {
                    var standardId = "BorgChassis" + role;
                    var donorId = "BorgChassis" + model + role;
                    var standardPrototype = prototypes.Index<EntityPrototype>(standardId);
                    var donorPrototype = prototypes.Index<EntityPrototype>(donorId);
                    // All professional capabilities remain inherited from the standard chassis.
                    foreach (var (name, entry) in standardPrototype.Components)
                    {
                        Assert.That(donorPrototype.Components.TryGetValue(name, out var donorEntry), Is.True, donorId + ": " + name);
                        var original = serialization.WriteValue(entry.Component.GetType(), entry.Component, alwaysWrite: true);
                        var donor = serialization.WriteValue(donorEntry.Component.GetType(), donorEntry.Component, alwaysWrite: true);
                        Assert.That(donor, Is.EqualTo(original), donorId + " must keep the standard " + name);
                    }
                    var added = donorPrototype.Components.Keys.Except(standardPrototype.Components.Keys).ToArray();
                    Assert.That(added.All(name => name is "BorgChassisSkin" or "EntityTableContainerFill"), Is.True,
                        "Only a cosmetic skin and ready-to-use brain may be added.");

                    var regular = em.SpawnEntity(standardId, map.GridCoords);
                    var bought = em.SpawnEntity(donorId, map.GridCoords);
                    var chassis = em.GetComponent<BorgChassisComponent>(bought);
                    var regularChassis = em.GetComponent<BorgChassisComponent>(regular);
                    Assert.That(chassis.MaxModules, Is.EqualTo(regularChassis.MaxModules));
                    Assert.That(chassis.ModuleContainer.ContainedEntities.Select(PrototypeId).OrderBy(id => id),
                        Is.EqualTo(regularChassis.ModuleContainer.ContainedEntities.Select(PrototypeId).OrderBy(id => id)),
                        role + ": default professional modules must match");
                    Assert.That(em.GetComponent<SiliconLawProviderComponent>(bought).Laws,
                        Is.EqualTo(em.GetComponent<SiliconLawProviderComponent>(regular).Laws));
                    Assert.That(chassis.BrainEntity, Is.Not.Null, role + ": positronic brain must already be installed");
                    Assert.That(PrototypeId(chassis.BrainEntity!.Value), Is.EqualTo("PositronicBrain"));
                    var cell = containers.GetContainer(bought, "cell_slot").ContainedEntities.Single();
                    Assert.That(PrototypeId(cell), Is.EqualTo("PowerCellMedium"));
                    var transponder = (SpriteSpecifier.Rsi) em.GetComponent<BorgTransponderComponent>(bought).Sprite!;
                    Assert.That(transponder.RsiState, Is.EqualTo(em.GetComponent<BorgChassisSkinComponent>(bought).BodyState));

                    var listing = prototypes.Index<ListingPrototype>("UplinkBorgChassis" + model + role);
                    Assert.That(listing.ProductEntity, Is.EqualTo(new EntProtoId(donorId)));
                    Assert.That(listing.Cost["Cristalhom"], Is.EqualTo(FixedPoint2.New(10)));
                    Assert.That(listing.Categories.Select(category => category.ToString()), Is.EquivalentTo(new[] { "UplinkHomBorgs" }));

                    string PrototypeId(EntityUid uid) => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
                }
            });
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var appearances = em.System<AppearanceSystem>();
                var sprites = em.System<SpriteSystem>();
                foreach (var role in roles)
                {
                    var uid = em.SpawnEntity("BorgChassis" + model + role, MapCoordinates.Nullspace);
                    var sprite = em.GetComponent<SpriteComponent>(uid);
                    var skin = em.GetComponent<BorgChassisSkinComponent>(uid);
                    var chassis = em.GetComponent<BorgChassisComponent>(uid);
                    Assert.That(em.HasComponent<SpriteMovementComponent>(uid), Is.False,
                        "An alternate model must not use the standard Meka movement art.");
                    Assert.That(chassis.HasMindState, Is.EqualTo(skin.BodyState + "_e"));
                    Assert.That(chassis.NoMindState, Is.EqualTo(skin.BodyState + "_e_r"));
                    appearances.SetData(uid, BorgVisuals.HasPlayer, true);
                    appearances.SetData(uid, ToggleableVisuals.Enabled, true);
                    foreach (var state in new[] { MobState.Alive, MobState.Critical, MobState.Dead, MobState.Alive })
                    {
                        appearances.SetData(uid, MobStateVisuals.State, state);
                        var changed = new AppearanceChangeEvent { Component = em.GetComponent<AppearanceComponent>(uid), Sprite = sprite };
                        em.EventBus.RaiseLocalEvent(uid, ref changed);
                        var expected = skin.BodyState + (state == MobState.Critical ? "_rest" : state == MobState.Dead ? "_wreck" : "");
                        Assert.That(sprite.BaseRSI!.TryGetState(expected, out _), Is.True);
                        Assert.That(sprites.LayerGetRsiState((uid, sprite), 0).Name, Is.EqualTo(expected));
                    }
                    appearances.SetData(uid, RotationVisuals.RotationState, RotationState.Horizontal);
                    var fallen = new AppearanceChangeEvent { Component = em.GetComponent<AppearanceComponent>(uid), Sprite = sprite };
                    em.EventBus.RaiseLocalEvent(uid, ref fallen);
                    Assert.That(sprite.BaseRSI!.TryGetState(skin.BodyState + "_tipped", out _), Is.True);
                    Assert.That(sprites.LayerGetRsiState((uid, sprite), 0).Name, Is.EqualTo(skin.BodyState + "_tipped"));
                    em.DeleteEntity(uid);
                }
            });
            // Mining's standard borgType replaces a server PointLight during MapInit and already logs this
            // engine bookkeeping error on the unmodified chassis. Permit only that same baseline error.
            Assert.That(pair.ServerLogHandler.FailingLogs.Where(message =>
                !(message.Contains("did not have PointLightComponent in its networked component dictionary during component deletion")
                    && (message.Contains(", BorgChassisMining)") || message.Contains($", BorgChassis{model}Mining)")))), Is.Empty);
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine($"{model} donor chassis failure: {exception}");
            throw;
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
