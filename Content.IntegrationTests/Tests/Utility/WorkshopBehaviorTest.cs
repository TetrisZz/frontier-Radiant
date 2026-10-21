using System;
using System.Linq;
using System.IO;
using System.Numerics;
using Content.Server._radiant.Decorations;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class WorkshopBehaviorTest
{
    [Test]
    public async Task FishSurviveTransportAndRecoverInWaterWithoutBecomingInvulnerable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var damage = entities.System<DamageableSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var fish = entities.SpawnEntity("FishBass", map.GridCoords);
            var life = entities.GetComponent<FishComponent>(fish);
            var health = entities.GetComponent<DamageableComponent>(fish);
            var suffocation = new DamageSpecifier();
            suffocation.DamageDict["Asphyxiation"] = 1;
            Assert.That(life.AirGraceEnds - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            damage.TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            life.AirGraceEnds = timing.CurTime - TimeSpan.FromSeconds(1);
            damage.TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.New(1)));

            var tank = entities.SpawnEntity("RadiantAquariumFilled", map.GridCoords);
            var storage = containers.GetContainer(tank, "storagebase");
            Assert.That(containers.Insert(fish, storage), Is.True);
            Assert.That(life.InAquariumWater, Is.True);
            var respiration = entities.GetComponent<RespiratorComponent>(fish);
            entities.System<RespiratorSystem>().UpdateSaturation(fish, -100, respiration);
            entities.System<AquariumSystem>().UpdateRespiration((fish, life));
            Assert.That(respiration.Saturation, Is.EqualTo(respiration.MaxSaturation), "Water must supply oxygen, not just cancel suffocation damage");
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.Zero), "Water should heal existing suffocation damage");
            for (var i = 0; i < 20; i++)
                damage.TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.Zero), "Water protects even after transport grace expires");
            Assert.That(containers.Remove(fish, storage), Is.True);
            Assert.That(life.InAquariumWater, Is.False);
            Assert.That(life.AirGraceEnds - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            damage.TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.Zero));

            var injury = new DamageSpecifier();
            injury.DamageDict["Blunt"] = 1;
            damage.TryChangeDamage(fish, injury, ignoreResistances: true);
            Assert.That(health.TotalDamage, Is.EqualTo(FixedPoint2.New(1)), "Transport protection must not prevent injuries");

            var deadFish = entities.SpawnEntity("FishCat", map.GridCoords);
            entities.GetComponent<FishComponent>(deadFish).AirGraceEnds = timing.CurTime - TimeSpan.FromSeconds(1);
            damage.TryChangeDamage(deadFish, suffocation * 10, ignoreResistances: true);
            Assert.That(containers.Insert(deadFish, storage), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(deadFish).TotalDamage, Is.EqualTo(FixedPoint2.New(10)), "Water must not resurrect dead fish");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AquariumCanBeFilledFedAndStockedByClick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var hands = entities.System<ServerHands>();
            var interaction = entities.System<SharedInteractionSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var appearance = entities.System<SharedAppearanceSystem>();
            var user = entities.SpawnEntity("MobHumanDummy", map.GridCoords.Offset(new Vector2(1, 0)));
            hands.AddHand(user, "aquarium-test", HandLocation.Right);
            hands.SetActiveHand(user, "aquarium-test");
            var tank = entities.SpawnEntity("RadiantAquarium", map.GridCoords);
            var storage = containers.GetContainer(tank, "storagebase");
            Assert.That(solutions.TryGetSolution(tank, "tank", out _, out var water), Is.True);
            Assert.That(water!.Volume, Is.EqualTo(FixedPoint2.Zero));

            var bucket = entities.SpawnEntity("Bucket", map.GridCoords);
            Assert.That(solutions.TryGetSolution(bucket, "drink", out var bucketSolution, out var bucketWater), Is.True);
            Assert.That(solutions.TryAddReagent(bucketSolution!.Value, "Water", FixedPoint2.New(250)), Is.True);
            Assert.That(hands.TryPickup(user, bucket, "aquarium-test", checkActionBlocker: false, animate: false), Is.True);
            for (var i = 0; i < 2; i++)
                Assert.That(interaction.InteractUsing(user, bucket, tank, map.GridCoords), Is.True);
            Assert.That(water.Volume, Is.EqualTo(FixedPoint2.New(200)));
            Assert.That(bucketWater!.Volume, Is.EqualTo(FixedPoint2.New(50)));
            Assert.That(storage.ContainedEntities, Does.Not.Contain(bucket));
            Assert.That(appearance.TryGetData<bool>(tank, AquariumVisuals.Water, out var visible), Is.True);
            Assert.That(visible, Is.True, "Water appearance must update immediately after filling");
            Assert.That(interaction.InteractUsing(user, bucket, tank, map.GridCoords), Is.True);
            Assert.That(water.Volume, Is.EqualTo(FixedPoint2.New(200)), "A full aquarium must not overflow");
            Assert.That(bucketWater.Volume, Is.EqualTo(FixedPoint2.New(50)));
            Assert.That(hands.TryDrop(user, bucket), Is.True);

            var aquarium = entities.GetComponent<AquariumComponent>(tank);
            aquarium.Food = 0;
            var feed = entities.SpawnEntity("RadiantAquariumFeed", map.GridCoords);
            Assert.That(hands.TryPickup(user, feed, "aquarium-test", checkActionBlocker: false, animate: false), Is.True);
            Assert.That(interaction.InteractUsing(user, feed, tank, map.GridCoords), Is.True);
            Assert.That(aquarium.Food, Is.EqualTo(10));
            Assert.That(entities.GetComponent<AquariumFeedComponent>(feed).Uses, Is.EqualTo(19));
            Assert.That(storage.ContainedEntities, Does.Not.Contain(feed));
            Assert.That(hands.TryDrop(user, feed), Is.True);

            foreach (var prototype in new[] { "RadiantAquariumRocks", "FishBass" })
            {
                var item = entities.SpawnEntity(prototype, map.GridCoords);
                Assert.That(hands.TryPickup(user, item, "aquarium-test", checkActionBlocker: false, animate: false), Is.True);
                Assert.That(interaction.InteractUsing(user, item, tank, map.GridCoords), Is.True);
                Assert.That(storage.ContainedEntities, Does.Contain(item));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HoneyAquariumCartAndHourglass()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var containers = entities.System<SharedContainerSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var appearance = entities.System<SharedAppearanceSystem>();
            var user = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
            entities.SpawnEntity("RadiantTestTubeRackFilled", map.GridCoords);

            var cart = entities.SpawnEntity("RadiantSurgicalCartFilled", map.GridCoords);
            var cartComponent = entities.GetComponent<SurgicalCartVisualsComponent>(cart);
            entities.System<SurgicalCartSystem>().UpdateContents((cart, cartComponent));
            Assert.That(appearance.TryGetData<string[]>(cart, SurgicalCartVisuals.Tools, out var tools), Is.True);
            Assert.That(tools, Does.Contain("scalpel_normal"));
            var cartStorage = containers.GetContainer(cart, "storagebase");
            var scalpel = cartStorage.ContainedEntities.First(e => entities.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID == "Scalpel");
            Assert.That(containers.Remove(scalpel, cartStorage), Is.True);
            appearance.TryGetData<string[]>(cart, SurgicalCartVisuals.Tools, out tools);
            Assert.That(tools, Does.Not.Contain("scalpel_normal"));

            var hive = entities.SpawnEntity("RadiantBeehive", map.GridCoords);
            var hiveComponent = entities.GetComponent<BeehiveComponent>(hive);
            var frame = entities.SpawnEntity("RadiantHoneyFrame", map.GridCoords);
            Assert.That(slots.TryInsert(hive, "frame1", frame, null), Is.True);
            var jar = entities.SpawnEntity("BeeSeeds", map.GridCoords);
            Assert.That(entities.HasComponent<SeedComponent>(jar), Is.False);
            var settle = new InteractUsingEvent(user, jar, hive, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(hive, settle);
            Assert.That(hiveComponent.Occupied, Is.True);
            var apiary = entities.System<ApiarySystem>();
            Assert.That(solutions.TryGetSolution(frame, "honey", out var honey, out var honeySolution), Is.True);
            apiary.ProduceHoney((hive, hiveComponent));
            Assert.That(honeySolution!.Volume, Is.EqualTo(FixedPoint2.Zero), "Plants are required for production");
            var plant = entities.SpawnEntity("HydroponicsTrayEmpty", map.GridCoords.Offset(new Vector2(1, 0)));
            var holder = entities.GetComponent<PlantHolderComponent>(plant);
            var seed = pair.Server.ResolveDependency<IPrototypeManager>().Index<SeedPrototype>("wheat");
            holder.Seed = seed;
            holder.Age = (int)Math.Ceiling(seed.Maturation) + 1;
            holder.Health = 10;
            apiary.ProduceHoney((hive, hiveComponent));
            Assert.That(honeySolution.Volume, Is.EqualTo(FixedPoint2.New(2)));
            holder.Dead = true;
            apiary.ProduceHoney((hive, hiveComponent));
            Assert.That(honeySolution.Volume, Is.EqualTo(FixedPoint2.New(2)), "Dead plants cannot support a hive");
            Assert.That(slots.TryEject(hive, "frame1", null, out _), Is.True);
            var cup = entities.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(cup, "beaker", out var cupSolution), Is.True);
            var transferred = entities.System<SolutionTransferSystem>().Transfer(user, frame, honey!.Value, cup, cupSolution!.Value, FixedPoint2.New(1));
            Assert.That(transferred, Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(honeySolution.Volume, Is.EqualTo(FixedPoint2.New(1)), "Draining must not duplicate honey");
            var condition = new ApiaryUnoccupied();
            Assert.That(condition.Condition(hive, entities), Is.False);
            hiveComponent.Occupied = false;
            Assert.That(condition.Condition(hive, entities), Is.True);

            var tank = entities.SpawnEntity("RadiantAquariumFilled", map.GridCoords);
            var aquarium = entities.System<AquariumSystem>();
            Assert.That(aquarium.HasWater(tank), Is.True);
            var fishStorage = containers.GetContainer(tank, "storagebase");
            var fish = entities.SpawnEntity("FishBass", map.GridCoords);
            Assert.That(containers.Insert(fish, fishStorage), Is.True);
            Assert.That(appearance.TryGetData<AquariumFishVisualState>(tank, AquariumVisuals.Fish, out var fishVisuals), Is.True);
            Assert.That(fishVisuals.States, Is.EqualTo(new[] { "bass" }));
            Assert.That(fishVisuals.Alive, Is.EqualTo(new[] { true }));
            using (var stream = new MemoryStream())
            {
                var serializer = pair.Server.ResolveDependency<IRobustSerializer>();
                serializer.Serialize(stream, fishVisuals);
                stream.Position = 0;
                var restored = serializer.Deserialize<AquariumFishVisualState>(stream);
                Assert.That(restored.States, Is.EqualTo(fishVisuals.States));
                Assert.That(restored.Alive, Is.EqualTo(fishVisuals.Alive));
            }
            var suffocation = new DamageSpecifier();
            suffocation.DamageDict["Asphyxiation"] = 1;
            entities.System<DamageableSystem>().TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(entities.GetComponent<DamageableComponent>(fish).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            for (var i = 0; i < 3; i++)
                Assert.That(containers.Insert(entities.SpawnEntity("FishCat", map.GridCoords), fishStorage), Is.True);
            Assert.That(containers.Insert(entities.SpawnEntity("FishPuffer", map.GridCoords), fishStorage), Is.False, "Aquarium capacity is four fish");
            var feed = entities.SpawnEntity("RadiantAquariumFeed", map.GridCoords);
            entities.GetComponent<AquariumComponent>(tank).Food = 0;
            var feeding = new AfterInteractEvent(user, feed, tank, map.GridCoords, true);
            entities.EventBus.RaiseLocalEvent(feed, feeding);
            Assert.That(entities.GetComponent<AquariumFeedComponent>(feed).Uses, Is.EqualTo(19));
            Assert.That(entities.GetComponent<AquariumComponent>(tank).Food, Is.EqualTo(10));
            Assert.That(containers.Remove(fish, fishStorage), Is.True);
            entities.GetComponent<FishComponent>(fish).AirGraceEnds = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
            entities.System<DamageableSystem>().TryChangeDamage(fish, suffocation, ignoreResistances: true);
            Assert.That(entities.GetComponent<DamageableComponent>(fish).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));

            var clock = entities.SpawnEntity("RadiantHourglass", map.GridCoords);
            var clockComponent = entities.GetComponent<HourglassComponent>(clock);
            var clocks = entities.System<HourglassSystem>();
            clocks.Start((clock, clockComponent));
            Assert.That(clockComponent.Running, Is.True);
            appearance.TryGetData<string>(clock, HourglassVisuals.State, out var state);
            Assert.That(state, Is.EqualTo("flip"));
            clockComponent.EndsAt = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
            clocks.Update(0);
            Assert.That(clockComponent.Running, Is.False);
            appearance.TryGetData<string>(clock, HourglassVisuals.State, out state);
            Assert.That(state, Is.EqualTo("idle"));
        });
        await pair.CleanReturnAsync();
    }
}
