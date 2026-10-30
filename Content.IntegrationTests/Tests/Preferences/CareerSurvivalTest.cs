using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class CareerSurvivalTest
{
    [Test]
    public async Task EveryCareerIssuesExactlyOneCompatibleBoxIncludingOldSavedSelections()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var spawning = em.System<StationSpawningSystem>();
                var inventory = em.System<InventorySystem>();
                foreach (var (job, group, oldGroup, oldBox) in new[]
                {
                    ("SalvageSpecialist", "CareerSurvival", "Survival", "EmergencyOxygen"),
                    ("MedicalDoctor", "CareerSurvivalMedical", "SurvivalMedical", "EmergencyOxygenMedical"),
                    ("StationEngineer", "CareerSurvivalEngineering", "SurvivalExtended", "EmergencyOxygenExtended"),
                })
                foreach (var species in new[] { "Human", "Vulpkanin", "Felinid", "Feroxi", "Diona", "Vox", "SlimePerson", "Sheleg" })
                foreach (var saved in new[] { false, true })
                {
                    var profile = HumanoidCharacterProfile.DefaultWithSpecies(species);
                    var role = "Job" + job;
                    RoleLoadout? oldSelection = null;
                    if (saved)
                    {
                        oldSelection = new RoleLoadout(role);
                        oldSelection.SelectedLoadouts[oldGroup] =
                            [new Loadout { Prototype = oldBox }, new Loadout { Prototype = oldBox }];
                        // Also cover duplicate and species-incompatible selections in the replacement group.
                        var first = pair.Server.ProtoMan.Index<LoadoutGroupPrototype>(group).Loadouts[0];
                        oldSelection.SelectedLoadouts[group] =
                            [new Loadout { Prototype = first }, new Loadout { Prototype = first }];
                        profile.SetLoadout(oldSelection);
                    }

                    var before = CountBoxes();
                    var patient = spawning.SpawnPlayerMob(map.GridCoords, job, profile, station: null);
                    var context = $"{job}, {species}, saved={saved}";
                    Assert.That(CountBoxes() - before, Is.EqualTo(1), context + ": exactly one box must be spawned");
                    Assert.That(inventory.TryGetSlotEntity(patient, "back", out var back), Is.True, context);
                    var storage = em.GetComponent<StorageComponent>(back!.Value);
                    var boxes = storage.Container.ContainedEntities.Where(uid => IsBox(em.GetComponent<MetaDataComponent>(uid))).ToArray();
                    Assert.That(boxes, Has.Length.EqualTo(1), context + ": the box must actually be in the backpack");
                    var expected = species switch
                    {
                        "Sheleg" => job == "StationEngineer" ? "NFBoxSurvivalExtendedSheleg" : "NFBoxSurvivalSheleg",
                        "Vox" or "SlimePerson" => job switch
                        {
                            "MedicalDoctor" => "BoxSurvivalMedicalNitrogen",
                            "StationEngineer" => "BoxSurvivalEngineeringNitrogen",
                            _ => "NFBoxSurvivalNitrogen",
                        },
                        _ => job switch
                        {
                            "MedicalDoctor" => "BoxSurvivalMedical",
                            "StationEngineer" => "BoxSurvivalEngineering",
                            _ => "NFBoxSurvival",
                        },
                    };
                    Assert.That(em.GetComponent<MetaDataComponent>(boxes[0]).EntityPrototype!.ID, Is.EqualTo(expected), context);
                    if (oldSelection != null)
                    {
                        Assert.That(oldSelection.SelectedLoadouts[oldGroup], Has.Count.EqualTo(2), "Spawning must not mutate stored preferences");
                        Assert.That(oldSelection.SelectedLoadouts[group], Has.Count.EqualTo(2));
                    }
                    em.DeleteEntity(patient);
                }

                int CountBoxes() => em.EntityQuery<MetaDataComponent>().Count(IsBox);
                bool IsBox(MetaDataComponent meta) => meta.EntityPrototype?.ID is { } id
                    && (id.StartsWith("BoxSurvival") || id.StartsWith("NFBoxSurvival"));
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
