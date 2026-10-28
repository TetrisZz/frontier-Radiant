using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Mono.FireControl.UI;
using Content.Server._Mono.FireControl;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Mono.FireControl;
using Content.Shared._radiant.Skills;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class ProfessionalShootingTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: SkillSpreadTestBullet
  parent: BulletPistol
  components:
  - type: Ammo
    muzzleFlash: null

- type: entity
  id: SkillSpreadTestPellets
  parent: SkillSpreadTestBullet
  components:
  - type: ProjectileSpread
    proto: SkillSpreadTestBullet
    count: 3
    spread: 20
";

    [Test]
    public async Task ZeroNativeSpreadAndPelletFansReceiveDegreesAndRespectMastery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var guns = em.System<GunSystem>();
                var user = em.SpawnEntity(null, map.GridCoords);
                var ranks = em.AddComponent<ProfessionalSkillsComponent>(user);
                var weapon = em.SpawnEntity(null, map.GridCoords);
                var gun = em.AddComponent<GunComponent>(weapon);
                gun.MinAngleModified = gun.MaxAngleModified = gun.CurrentAngle = Angle.Zero;
                gun.AngleIncreaseModified = gun.AngleDecayModified = Angle.Zero;
                gun.ProjectileSpeedModified = 25;
                gun.SoundGunshotModified = null;
                var target = map.GridCoords.Offset(new Vector2(10, 0));

                foreach (var (ammoId, count) in new[] { ("SkillSpreadTestBullet", 1), ("SkillSpreadTestPellets", 3) })
                {
                    for (var tier = 0; tier <= 4; tier++)
                    {
                        gun.SkillRequiredLevel = tier;
                        ranks.Levels[(int)ProfessionalSkill.Shooting] = tier;
                        var ordinary = new List<double>();
                        var misfires = 0;
                        for (var shot = 0; shot < 256; shot++)
                        {
                            var degrees = Fire();
                            if (Math.Abs(degrees) > 2.501)
                                misfires++;
                            else
                                ordinary.Add(degrees);
                        }
                        if (tier <= 2)
                            Assert.That(misfires, Is.InRange(6, 60), $"{ammoId}, tier {tier}: random-direction shots");
                        else
                            Assert.That(misfires, Is.Zero, $"{ammoId}, tier {tier}: no random-direction shots");
                        if (tier <= 3)
                        {
                            Assert.That(ordinary.Min(), Is.LessThan(-2), $"{ammoId}, tier {tier}: negative spread");
                            Assert.That(ordinary.Max(), Is.GreaterThan(2), $"{ammoId}, tier {tier}: positive spread");
                        }
                        else
                            Assert.That(ordinary.All(d => Math.Abs(d) < .0001), Is.True);

                        // The same weapon must become accurate after its tier has been mastered.
                        ranks.Levels[(int)ProfessionalSkill.Shooting] = 4;
                        for (var shot = 0; shot < 12; shot++)
                            Assert.That(Fire(), Is.EqualTo(0).Within(.0001), $"{ammoId}, mastered tier {tier}");
                    }

                    double Fire()
                    {
                        var ammo = em.SpawnEntity(ammoId, map.GridCoords);
                        guns.Shoot(weapon, gun,
                            new List<(EntityUid?, IShootable)> { (ammo, em.GetComponent<AmmoComponent>(ammo)) },
                            map.GridCoords, target, out _, user);
                        var projectiles = new List<EntityUid>();
                        var vectors = new List<Vector2>();
                        var query = em.EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();
                        while (query.MoveNext(out var uid, out var projectile, out var body))
                        {
                            if (projectile.Weapon != weapon)
                                continue;
                            projectiles.Add(uid);
                            vectors.Add(Vector2.Normalize(body.LinearVelocity));
                        }
                        Assert.That(projectiles, Has.Count.EqualTo(count));
                        var center = vectors.Aggregate(Vector2.Zero, (sum, next) => sum + next);
                        var result = Math.Atan2(center.Y, center.X) * 180 / Math.PI;
                        if (count == 3)
                        {
                            var offsets = vectors.Select(v => Math.Atan2(center.X * v.Y - center.Y * v.X,
                                Vector2.Dot(center, v)) * 180 / Math.PI).ToArray();
                            Assert.That(offsets.Max() - offsets.Min(), Is.EqualTo(20).Within(.001),
                                "Skill error rotates the whole pellet fan without changing its native width.");
                        }
                        foreach (var uid in projectiles)
                            em.DeleteEntity(uid);
                        return result;
                    }
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AllStaffAndRevolverDescendantsInheritTheirShootingTier()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var skills = em.System<SharedProfessionalSkillsSystem>();
                var prototypes = pair.Server.ProtoMan;
                var staffRoots = new[] { "WeaponStaffBase", "WeaponInertStaffBase", "WeaponChargedStaffBase" };
                var revolverRoots = new[] { "BaseWeaponRevolver", "NFBaseWeaponFrameRevolver" };
                var staves = 0;
                var revolvers = 0;
                foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.Components.ContainsKey("Gun"))
                        continue;
                    var parents = prototypes.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true)
                        .Select(p => p.Item1).ToHashSet();
                    var staff = staffRoots.Any(parents.Contains);
                    var toy = parents.Contains("NFBaseWeaponFrameRevolverFoamForce")
                        && !parents.Contains("NFRevolverCapGunFake");
                    if (!staff && !revolverRoots.Any(parents.Contains))
                        continue;
                    var uid = em.SpawnEntity(proto.ID, MapCoordinates.Nullspace);
                    var gun = em.GetComponent<GunComponent>(uid);
                    Assert.That(skills.GunRequirement((uid, gun)),
                        Is.EqualTo((ProfessionalSkill.Shooting, staff || toy ? 0 : 1)), proto.ID);
                    if (staff) staves++; else revolvers++;
                    em.DeleteEntity(uid);
                }
                Assert.That(staves, Is.GreaterThan(5));
                Assert.That(revolvers, Is.GreaterThan(5));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task GunneryChecksTheOperatorEvenWhenGenericUiValidationIsDisabled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var operatorUid = em.SpawnEntity(null, map.GridCoords);
                var ranks = em.AddComponent<ProfessionalSkillsComponent>(operatorUid);
                var server = em.SpawnEntity(null, map.GridCoords);
                var control = em.AddComponent<FireControlServerComponent>(server);
                var weapon = em.SpawnEntity("WeaponRifleBB", map.GridCoords);
                var gun = em.GetComponent<GunComponent>(weapon);
                control.Controlled.Add(weapon);
                var target = em.GetNetCoordinates(map.GridCoords.Offset(new Vector2(10, 0)));
                foreach (var id in new[] { "ComputerGunneryConsole", "ComputerTabletopComputerGunneryConsole" })
                {
                    var console = em.SpawnEntity(id, map.GridCoords.Offset(new Vector2(0, 3)));
                    em.GetComponent<FireControlConsoleComponent>(console).ConnectedServer = server;
                    Assert.That(em.System<SharedUserInterfaceSystem>().TryGetInterfaceData(console,
                        FireControlConsoleUiKey.Key, out var uiData), Is.True);
                    Assert.That(uiData!.RequireInputValidation, Is.False);
                    for (var level = 0; level <= 4; level++)
                    {
                        ranks.Levels[(int)ProfessionalSkill.Shooting] = level;
                        var open = new BoundUserInterfaceMessageAttempt(operatorUid, console,
                            FireControlConsoleUiKey.Key, new OpenBoundInterfaceMessage());
                        em.EventBus.RaiseEvent(EventSource.Local, open);
                        Assert.That(open.Cancelled, Is.False, "The radar remains viewable at every rank.");
                        gun.NextFire = TimeSpan.Zero;
                        gun.ShotCounter = 0;
                        var message = new FireControlConsoleFireMessage(new() { em.GetNetEntity(weapon) }, target)
                            { Actor = operatorUid, UiKey = FireControlConsoleUiKey.Key };
                        // Raise the actual directed message: this UI deliberately skips the generic attempt event.
                        em.EventBus.RaiseLocalEvent(console, message);
                        var shots = em.EntityQuery<ProjectileComponent>().Count(p => p.Weapon == weapon);
                        Assert.That(shots, Is.EqualTo(level == 4 ? 1 : 0), $"{id}, shooting {level}");
                    }
                    foreach (var (_, uid) in em.EntityQuery<ProjectileComponent, MetaDataComponent>()
                                 .Where(p => p.Item1.Weapon == weapon).Select(p => (p.Item1, p.Item2.Owner)).ToArray())
                        em.DeleteEntity(uid);
                    em.DeleteEntity(console);
                }
            });

            await pair.Client.WaitAssertion(() =>
            {
                var em = Robust.Shared.IoC.IoCManager.Resolve<IEntityManager>();
                var weapon = em.SpawnEntity(null, MapCoordinates.Nullspace);
                var nav = new NavInterfaceState(256, null, null, new(), default, default, null, null, true, false);
                var state = new FireControlConsoleBoundInterfaceState(true,
                    new[] { new FireControllableEntry(em.GetNetEntity(weapon), default, "Test turret", 5, true) }, nav);
                using var window = new FireControlWindow();
                window.UpdateStatus(state);
                Assert.That(window.WeaponsList.Values.Single().Disabled, Is.True);
                Assert.That(window.RefreshButton.Disabled, Is.False);
                window.SetFiringPermission(true);
                var button = window.WeaponsList.Values.Single();
                Assert.That(button.Disabled, Is.False);
                button.Pressed = true;
                window.SetFiringPermission(false);
                Assert.That(button.Disabled, Is.True);
                Assert.That(button.Pressed, Is.False, "Revoking permission clears the old selection.");
                window.UpdateStatus(state);
                Assert.That(button.Disabled, Is.True, "State refresh cannot re-enable firing.");
                Assert.That(window.SelectAllButton.Disabled, Is.True);
                Assert.That(window.RefreshButton.Disabled, Is.False);
                em.DeleteEntity(weapon);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
