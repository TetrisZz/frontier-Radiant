using System;
using System.Linq;
using Content.Client.Medical;
using Content.Client.Storage.Visualizers;
using Content.Server._radiant.Medical.Genetics;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Medical.Virology;
using Content.Shared._radiant.SmartFridge;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class LaboratoryEquipmentVisualsTest
{
    [Test]
    public async Task LabStorageAcceptsSamplesAndEquipmentDisplaysWorkPowerAndSpentStates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var containers = em.System<SharedContainerSystem>();
                var hands = em.System<SharedHandsSystem>();
                var appearance = em.System<SharedAppearanceSystem>();
                var fridge = em.SpawnEntity("SmartFridgeVirologyRadiant", map.GridCoords);
                var user = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(containers.TryGetContainer(fridge, "smart_fridge_inventory", out var inventory), Is.True);
                foreach (var id in new[] { "DiseaseSwab", "Vaccine", "VirologyCultureHematic", "GeneticSampleCartridge", "GeneticHematopoiesisInjector", "GeneticHematopoiesisRemover" })
                {
                    var item = em.SpawnEntity(id, map.GridCoords);
                    Assert.That(hands.TryPickupAnyHand(user, item), Is.True, id);
                    var interaction = new InteractUsingEvent(user, item, fridge, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(fridge, interaction);
                    Assert.That(interaction.Handled, Is.True, id);
                    Assert.That(inventory!.ContainedEntities, Does.Contain(item), id);
                    Assert.That(appearance.TryGetData<int>(fridge, SmartFridgeFillVisuals.Level, out var level), Is.True);
                    Assert.That(level, Is.EqualTo(1));
                    Assert.That(containers.Remove(item, inventory), Is.True);
                    Assert.That(appearance.TryGetData<int>(fridge, SmartFridgeFillVisuals.Level, out level), Is.True);
                    Assert.That(level, Is.Zero);
                }
                var tool = em.SpawnEntity("Crowbar", map.GridCoords);
                Assert.That(hands.TryPickupAnyHand(user, tool), Is.True);
                var rejected = new InteractUsingEvent(user, tool, fridge, map.GridCoords);
                em.EventBus.RaiseLocalEvent(fridge, rejected);
                Assert.That(rejected.Handled, Is.False);
                Assert.That(inventory!.ContainedEntities, Is.Empty);
            });

            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var appearance = em.System<AppearanceSystem>();
                var sprites = em.System<SpriteSystem>();
                var localMap = em.System<SharedMapSystem>().CreateMap(out _);
                try
                {
                    var coordinates = new EntityCoordinates(localMap, 0, 0);
                    var sequencer = em.SpawnEntity("VirologySequencer", coordinates);
                    foreach (var powered in new[] { false, true })
                    foreach (var running in new[] { false, true })
                    {
                        appearance.SetData(sequencer, VirologyMachineVisuals.Running, running);
                        appearance.SetData(sequencer, VirologyMachineVisuals.Powered, powered);
                        Refresh(sequencer);
                        var sprite = em.GetComponent<SpriteComponent>(sequencer);
                        var work = sprite[sprite.LayerMapGet(DiseaseMachineVisualLayers.IsRunning)];
                        var off = sprite[sprite.LayerMapGet(DiseaseMachineVisualLayers.IsOn)];
                        Assert.That(work.Visible, Is.EqualTo(powered));
                        Assert.That(off.Visible, Is.EqualTo(!powered));
                        Assert.That(work.RsiState.ToString(), Is.EqualTo(running ? "working" : "idle"));
                    }
                    var pod = em.SpawnEntity("GeneticBodyGrower", coordinates);
                    foreach (var stage in Enum.GetNames<GeneticGrowthStage>().Append("Off"))
                    {
                        appearance.SetData(pod, GeneticBodyVisuals.State, stage);
                        Refresh(pod);
                        var sprite = em.GetComponent<SpriteComponent>(pod);
                        var expected = stage switch
                        {
                            "Off" => "closed",
                            "Idle" or "Ready" => "open",
                            _ => "working",
                        };
                        Assert.That(sprite[sprite.LayerMapGet("base")].RsiState.ToString(), Is.EqualTo(expected));
                    }
                    var fridge = em.SpawnEntity("SmartFridgeVirologyRadiant", coordinates);
                    foreach (var level in new[] { 0, 1, 2, 3 })
                    {
                        appearance.SetData(fridge, PowerDeviceVisuals.Powered, true);
                        appearance.SetData(fridge, SmartFridgeFillVisuals.Level, level);
                        Refresh(fridge);
                        var sprite = em.GetComponent<SpriteComponent>(fridge);
                        Assert.That(sprite[sprite.LayerMapGet(StorageVisualLayers.Base)].RsiState.ToString(),
                            Is.EqualTo(level == 0 ? "normal" : $"fill{level}"));
                        appearance.SetData(fridge, PowerDeviceVisuals.Powered, false);
                        Refresh(fridge);
                        Assert.That(sprite[sprite.LayerMapGet(StorageVisualLayers.Base)].RsiState.ToString(), Is.EqualTo("off"));
                    }
                    foreach (var id in new[] { "GeneticHematopoiesisInjector", "GeneticHematopoiesisRemover" })
                    {
                        var injector = em.SpawnEntity(id, coordinates);
                        foreach (var used in new[] { false, true })
                        {
                            appearance.SetData(injector, GeneticTherapyVisuals.Used, used);
                            Refresh(injector);
                            var sprite = em.GetComponent<SpriteComponent>(injector);
                            Assert.That(sprite[sprite.LayerMapGet("base")].RsiState.ToString(), Is.EqualTo(used ? "used" : "icon"));
                        }
                        foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
                        {
                            var held = new GetInhandVisualsEvent(injector, hand);
                            em.EventBus.RaiseLocalEvent(injector, held);
                            Assert.That(held.Layers, Has.Count.EqualTo(1));
                            var data = held.Layers.Single().Item2;
                            Assert.That(data.RsiPath, Is.EqualTo("/Textures/_radiant/Objects/Medical/genetic_injector.rsi"));
                            Assert.That(em.GetComponent<SpriteComponent>(injector).BaseRSI!.TryGetState(data.State!, out _), Is.True);
                        }
                    }

                    void Refresh(EntityUid uid)
                    {
                        var sprite = em.GetComponent<SpriteComponent>(uid);
                        var changed = new AppearanceChangeEvent { Component = em.GetComponent<AppearanceComponent>(uid), Sprite = sprite };
                        em.EventBus.RaiseLocalEvent(uid, ref changed);
                        foreach (var layer in sprite.AllLayers)
                            Assert.That(layer.ActualRsi!.TryGetState(layer.RsiState, out _), Is.True, layer.RsiState.ToString());
                    }
                }
                finally { em.DeleteEntity(localMap); }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
