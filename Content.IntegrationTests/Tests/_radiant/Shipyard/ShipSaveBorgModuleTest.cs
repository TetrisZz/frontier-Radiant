using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.Silicons.Borgs;
using Content.Shared.Actions;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveBorgModuleTest
{
    [Test]
    public async Task SavedModulesRemainSelectableAndKeepTheirActualTools()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var borgs = em.System<BorgSystem>();
                var actions = em.System<SharedActionsSystem>();
                var hands = em.System<SharedHandsSystem>();
                var containers = em.System<SharedContainerSystem>();
                foreach (var (prototype, legacy, missingAction) in new[]
                {
                    ("BorgModuleTool", false, false),
                    ("BorgModuleTool", true, false),
                    ("BorgModuleSurgery", true, false),
                    ("BorgModuleSurgery", false, true),
                })
                {
                    var original = em.SpawnEntity("BorgChassisSelectable", map.GridCoords);
                    var chassis = em.GetComponent<BorgChassisComponent>(original);
                    var module = em.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(containers.Insert(module, chassis.ModuleContainer), Is.True);
                    borgs.InstallModule(original, module, chassis);
                    var itemModule = em.GetComponent<ItemBorgModuleComponent>(module);
                    Assert.That(chassis.SelectedModule, Is.EqualTo(module));
                    var tools = hands.EnumerateHeld((original, em.GetComponent<HandsComponent>(original))).ToArray();
                    Assert.That(tools.Length, Is.EqualTo(itemModule.Hands.Count));
                    for (var i = 0; i < tools.Length; i++)
                        em.System<MetaDataSystem>().SetEntityName(tools[i], $"Preserved module tool {i}");
                    borgs.UninstallModule(original, module, chassis);
                    Assert.That(itemModule.StoredItems!.Count, Is.EqualTo(tools.Length));
                    var selectable = em.GetComponent<SelectableBorgModuleComponent>(module);
                    if (missingAction)
                        em.DeleteEntity(selectable.ModuleSwapActionEntity!.Value);
                    else
                        actions.SetCooldown(selectable.ModuleSwapActionEntity, TimeSpan.FromHours(8));
                    Assert.That(containers.Remove(module, chassis.ModuleContainer), Is.True);
                    em.DeleteEntity(original);
                    var stash = em.SpawnEntity("MilitaryStorageStash", map.GridCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(stash, module, out _, playSound: false), Is.True);
                    Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                    if (!missingAction)
                        Assert.That(actions.IsCooldownActive(actions.GetAction(selectable.ModuleSwapActionEntity)!.Value.Comp), Is.True,
                            "Saving must not alter live action timers.");

                    if (legacy)
                    {
                        var oldSave = new YamlStream();
                        oldSave.Load(new StringReader(yaml!));
                        var root = (YamlMappingNode) oldSave.Documents[0].RootNode;
                        foreach (YamlMappingNode group in (YamlSequenceNode) root.Children[new YamlScalarNode("entities")])
                        {
                            foreach (YamlMappingNode entity in (YamlSequenceNode) group.Children[new YamlScalarNode("entities")])
                            {
                                foreach (YamlMappingNode component in (YamlSequenceNode) entity.Children[new YamlScalarNode("components")])
                                {
                                    var type = ((YamlScalarNode) component.Children[new YamlScalarNode("type")]).Value;
                                    if (type == "BorgModule")
                                        component.Children[new YamlScalarNode("installedEntity")] = new YamlScalarNode("invalid");
                                    if (type == "ItemBorgModule" && component.Children.TryGetValue(new YamlScalarNode("storedItems"), out var savedItems)
                                        && savedItems is YamlMappingNode items)
                                    {
                                        foreach (var entry in items.Children.ToArray())
                                        {
                                            items.Children.Remove(entry.Key);
                                            items.Children.Add(new YamlScalarNode("999999-" + ((YamlScalarNode) entry.Key).Value), entry.Value);
                                        }
                                    }
                                    if (type == "Action" && group.Children.TryGetValue(new YamlScalarNode("proto"), out var id)
                                        && ((YamlScalarNode) id).Value == "ActionBorgSwapModule")
                                    {
                                        component.Children[new YamlScalarNode("cooldown")] = new YamlMappingNode("start", "28799", "end", "28800");
                                        component.Children[new YamlScalarNode("attachedEntity")] = new YamlScalarNode("invalid");
                                    }
                                }
                            }
                        }
                        using var writer = new StringWriter();
                        oldSave.Save(new YamlMappingFix(new Emitter(writer)), false);
                        yaml = writer.ToString();
                    }
                    em.DeleteEntity(stash);
                    using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved borg module", out var loaded,
                        offset: new Vector2(20, 20)), Is.True);
                    var loadedModule = em.EntityQuery<ItemBorgModuleComponent, TransformComponent>()
                        .Single(entry => entry.Item2.GridUid == loaded.Value.Owner).Item1.Owner;
                    Assert.That(em.GetComponent<BorgModuleComponent>(loadedModule).Installed, Is.False);
                    var replacement = em.SpawnEntity("BorgChassisSelectable", map.GridCoords);
                    var replacementChassis = em.GetComponent<BorgChassisComponent>(replacement);
                    Assert.That(containers.Insert(loadedModule, replacementChassis.ModuleContainer), Is.True);
                    borgs.InstallModule(replacement, loadedModule, replacementChassis);
                    var button = actions.GetAction(em.GetComponent<SelectableBorgModuleComponent>(loadedModule).ModuleSwapActionEntity);
                    Assert.That(button, Is.Not.Null);
                    Assert.That(button!.Value.Comp.Cooldown, Is.Null);
                    Assert.That(button.Value.Comp.UseDelay, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
                    Assert.That(actions.ValidAction(button.Value), Is.True);
                    Assert.That(actions.GetActions(replacement).Any(action => action.Owner == button.Value.Owner), Is.True);
                    var restored = hands.EnumerateHeld((replacement, em.GetComponent<HandsComponent>(replacement))).ToArray();
                    Assert.That(restored.Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityName),
                        Is.EquivalentTo(Enumerable.Range(0, tools.Length).Select(i => $"Preserved module tool {i}")));
                    actions.PerformAction(replacement, button.Value);
                    Assert.That(replacementChassis.SelectedModule, Is.Null);
                    Assert.That(actions.IsCooldownActive(button.Value.Comp), Is.True, "The normal anti-spam delay must still work.");
                    actions.RemoveCooldown(button.Value);
                    actions.PerformAction(replacement, button.Value);
                    Assert.That(replacementChassis.SelectedModule, Is.EqualTo(loadedModule));
                    Assert.That(hands.EnumerateHeld((replacement, em.GetComponent<HandsComponent>(replacement))),
                        Is.EquivalentTo(restored), "Switching modules must reuse the same tools, not create replacements.");
                    em.DeleteEntity(replacement);
                    em.DeleteEntity(loaded.Value.Owner);
                }
            });
            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Borg module save failure: {exception}");
            throw;
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
