using System;
using Content.Shared._radiant.Skills;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class ProfessionalSkillGameplayTest
{
    [Test]
    public async Task TestProfessionalSkillGameplay()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        await server.WaitAssertion(() =>
        {
            var entities = server.ResolveDependency<IEntityManager>();
            var system = entities.System<SharedProfessionalSkillsSystem>();
            var user = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var skills = entities.AddComponent<ProfessionalSkillsComponent>(user);

            // Exercise the actual gun event, including prototype inheritance and mining exceptions.
            var cases = new[]
            {
                ("NFWeaponPistolMk58", ProfessionalSkill.Shooting, 1, 0.1f),
                ("WeaponSubMachineGunFighter", ProfessionalSkill.Shooting, 2, 0.1f),
                ("WeaponSubMachineGunFighterRS", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponLauncherChinaLake", ProfessionalSkill.Shooting, 3, 0f),
                ("WeaponLauncherM203", ProfessionalSkill.Shooting, 3, 0f),
                ("NFWeaponHoloflareGun", ProfessionalSkill.Shooting, 0, 0f),
                ("NFWeaponPka", ProfessionalSkill.Salvage, 1, 0f),
                ("NFWeaponPkaCannon", ProfessionalSkill.Salvage, 1, 0f),
                ("NFWeaponPkaSawn", ProfessionalSkill.Salvage, 1, 0f),
                ("WeaponCrusher", ProfessionalSkill.Salvage, 1, 0f),
                ("WeaponGrapplingGun", ProfessionalSkill.Salvage, 0, 0.8f),
            };
            foreach (var (id, skill, required, chance) in cases)
            {
                var gun = entities.SpawnEntity(id, MapCoordinates.Nullspace);
                var component = entities.GetComponent<GunComponent>(gun);
                var rule = system.Requirement(gun, SkillAction.Shoot);
                Assert.That(rule, Is.Not.Null, $"{id}: missing inherited weapon rule");
                TestContext.Out.WriteLine($"{id}: {rule!.ID}, accuracy={rule.InaccurateAtLevel}, chance={rule.MisfireChance}, override={component.InaccurateAtSkillLevel}");
                Assert.That(system.GunRequirement((gun, component)), Is.EqualTo((skill, required)), id);
                for (var level = 0; level <= ProfessionalSkillRules.Maximum(skill); level++)
                {
                    Array.Clear(skills.Levels);
                    skills.Levels[(int) skill] = level;
                    var attempt = new AttemptShootEvent(user, null);
                    entities.EventBus.RaiseLocalEvent(gun, ref attempt);
                    Assert.That(attempt.Cancelled, Is.EqualTo(level < required), $"{id}, level {level}");
                    Assert.That(system.GunMisfireChance(user, (gun, component)),
                        Is.EqualTo(level == required ? chance : 0).Within(0.0001), $"{id}, accuracy {level}");
                    var spread = skill == ProfessionalSkill.Shooting && required > 0 && level >= required
                        ? level switch { 1 => 10d, 2 => 5d, _ => 0d } : 0d;
                    Assert.That(system.GunSkillSpreadDegrees(user, (gun, component)), Is.EqualTo(spread),
                        $"{id}, spread at level {level}");
                }
                entities.DeleteEntity(gun);
            }

            Array.Clear(skills.Levels);
            var welder = entities.SpawnEntity("Welder", MapCoordinates.Nullspace);
            Assert.That(system.CanUse(user, welder, SkillAction.Use, false), Is.True);
            Assert.That(system.ToolDelay(user, welder, TimeSpan.Zero, 1), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(system.ToolDelay(user, welder, TimeSpan.FromSeconds(4), 1), Is.EqualTo(TimeSpan.FromSeconds(8)));
            skills.Levels[(int) ProfessionalSkill.Engineering] = 1;
            Assert.That(system.ToolDelay(user, welder, TimeSpan.FromSeconds(4), 1), Is.EqualTo(TimeSpan.FromSeconds(4)));
            entities.DeleteEntity(welder);

            var canister = entities.SpawnEntity("AirCanister", MapCoordinates.Nullspace);
            Assert.That(system.CanUse(user, canister, SkillAction.Anchor, false), Is.False);
            skills.Levels[(int) ProfessionalSkill.Engineering] = 2;
            Assert.That(system.CanUse(user, canister, SkillAction.Anchor, false), Is.True);
            entities.DeleteEntity(canister);
            Array.Clear(skills.Levels);
            foreach (var reagent in new[] { "Water", "Blood", "Nutriment", "Beer", "Milk", "Sugar" })
                Assert.That(system.CanIdentifyReagent(user, reagent), Is.True, reagent);
            foreach (var reagent in new[] { "Bicaridine", "UnstableMutagen", "Toxin" })
                Assert.That(system.CanIdentifyReagent(user, reagent), Is.False, reagent);
            skills.Levels[(int) ProfessionalSkill.Cooking] = 3;
            Assert.That(system.CanIdentifyReagent(user, "Toxin"), Is.False);
            skills.Levels[(int) ProfessionalSkill.Medicine] = 2;
            Assert.That(system.CanIdentifyReagent(user, "Bicaridine"), Is.True);
            entities.DeleteEntity(user);
        });
    }
}
