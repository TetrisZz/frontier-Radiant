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
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var server = pair.Server;
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
                ("WeaponSubMachineGunFighter", ProfessionalSkill.Shooting, 3, 0f),
                ("WeaponSubMachineGunFighterRS", ProfessionalSkill.Shooting, 3, 0f),
                ("NFWeaponLauncherChinaLake", ProfessionalSkill.Shooting, 4, 0f),
                ("WeaponLauncherM203", ProfessionalSkill.Shooting, 4, 0f),
                ("WeaponSprayNozzle", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFWeaponRifleFoam", ProfessionalSkill.Shooting, 0, 0.1f),
                ("WeaponRifleBB", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFRevolverCapGun", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFRevolverCapGunFake", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponLauncherMail", ProfessionalSkill.Shooting, 0, 0.1f),
                ("WeaponChargedStaffWizardCausticMaster", ProfessionalSkill.Shooting, 0, 0.1f),
                ("WeaponFlareGun", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFWeaponHoloflareGun", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFWeaponPistolViper", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponEnergyRifleAssaultLaserRepeater", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponEnergySubMachineGunDeltaV", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponShotgunHandmade", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponPistolFlintlock", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponRevolverArgenti", ProfessionalSkill.Shooting, 1, 0.1f),
                ("WeaponEnergyShotgunRS", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponRifleAssaultGestio", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponShotgunBulldogRS", ProfessionalSkill.Shooting, 3, 0f),
                ("NFWeaponPka", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponPkaCannon", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponPkaSawn", ProfessionalSkill.Shooting, 1, 0.1f),
                ("WeaponTaserBorg", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponEnergyPistolDisabler", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponEnergySubMachineGunDisabler", ProfessionalSkill.Shooting, 1, 0.1f),
                ("NFWeaponEnergyPistolDisablerPractice", ProfessionalSkill.Shooting, 0, 0.1f),
                ("NFWeaponEnergyPistolPulse", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponEnergyRiflePulse", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponEnergyRifleSniperXrayCannon", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponEnergyLightMachineGunTurbo", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponRifleSVS", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponShotgunKammerer", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponShotgunBulldog", ProfessionalSkill.Shooting, 3, 0f),
                ("NFWeaponLightMachineGunL6C", ProfessionalSkill.Shooting, 4, 0f),
                ("NFWeaponRifleAssaultSm", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponRifleAssaultVulcan", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponRifleAssaultNovaliteC1", ProfessionalSkill.Shooting, 2, 0.1f),
                ("NFWeaponPistolCHIMP", ProfessionalSkill.Science, 3, 0.1f),
                ("NFWeaponPistolCHIMPUpgraded", ProfessionalSkill.Science, 3, 0.1f),
                ("WeaponCrusher", ProfessionalSkill.Salvage, 2, 0.1f),
                ("WeaponGrapplingGun", ProfessionalSkill.Salvage, 0, 0.1f),
                };
                foreach (var (id, skill, required, chance) in cases)
                {
                    var gun = entities.SpawnEntity(id, MapCoordinates.Nullspace);
                    var component = entities.GetComponent<GunComponent>(gun);
                    var rule = system.Requirement(gun, SkillAction.Shoot);
                    Assert.That(rule, Is.Not.Null, $"{id}: missing inherited weapon rule");
                    Assert.That(system.GunRequirement((gun, component)), Is.EqualTo((skill, required)), id);
                    for (var level = 0; level <= ProfessionalSkillRules.Maximum(skill); level++)
                    {
                        Array.Clear(skills.Levels);
                        skills.Levels[(int)skill] = level;
                        var attempt = new AttemptShootEvent(user, null);
                        entities.EventBus.RaiseLocalEvent(gun, ref attempt);
                        Assert.That(attempt.Cancelled, Is.EqualTo(level < required), $"{id}, level {level}");
                        var ownTier = skill == ProfessionalSkill.Shooting ? level == required : level >= required;
                        Assert.That(system.GunMisfireChance(user, (gun, component)),
                            Is.EqualTo(ownTier ? chance : 0).Within(0.0001), $"{id}, accuracy {level}");
                        var spread = ownTier && (skill != ProfessionalSkill.Shooting || level <= 3) ? 5 : 0;
                        Assert.That(system.GunSkillSpread(user, (gun, component)).Degrees, Is.EqualTo(spread).Within(.0001),
                            $"{id}, spread at level {level}");
                    }
                    // A higher shooting rank removes penalties for tools too, without bypassing their profession gate.
                    Array.Fill(skills.Levels, 4);
                    Assert.That(system.GunMisfireChance(user, (gun, component)), Is.Zero, id);
                    Assert.That(system.GunSkillSpread(user, (gun, component)).Degrees, Is.Zero, id);
                    entities.DeleteEntity(gun);
                }

                Array.Clear(skills.Levels);
                var welder = entities.SpawnEntity("Welder", MapCoordinates.Nullspace);
                Assert.That(system.CanUse(user, welder, SkillAction.Use, false), Is.True);
                Assert.That(system.ToolDelay(user, welder, TimeSpan.Zero, 1), Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(system.ToolDelay(user, welder, TimeSpan.FromSeconds(4), 1), Is.EqualTo(TimeSpan.FromSeconds(8)));
                skills.Levels[(int)ProfessionalSkill.Engineering] = 1;
                Assert.That(system.ToolDelay(user, welder, TimeSpan.FromSeconds(4), 1), Is.EqualTo(TimeSpan.FromSeconds(4)));
                entities.DeleteEntity(welder);

                var canister = entities.SpawnEntity("AirCanister", MapCoordinates.Nullspace);
                Assert.That(system.CanUse(user, canister, SkillAction.Anchor, false), Is.False);
                skills.Levels[(int)ProfessionalSkill.Engineering] = 2;
                Assert.That(system.CanUse(user, canister, SkillAction.Anchor, false), Is.True);
                entities.DeleteEntity(canister);
                Array.Clear(skills.Levels);
                foreach (var reagent in new[] { "Water", "Blood", "Nutriment", "Beer", "Milk", "Sugar" })
                    Assert.That(system.CanIdentifyReagent(user, reagent), Is.True, reagent);
                foreach (var reagent in new[] { "Bicaridine", "UnstableMutagen", "Toxin" })
                    Assert.That(system.CanIdentifyReagent(user, reagent), Is.False, reagent);
                skills.Levels[(int)ProfessionalSkill.Cooking] = 3;
                Assert.That(system.CanIdentifyReagent(user, "Toxin"), Is.False);
                skills.Levels[(int)ProfessionalSkill.Medicine] = 4;
                Assert.That(system.CanIdentifyReagent(user, "Bicaridine"), Is.True);
                entities.DeleteEntity(user);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
