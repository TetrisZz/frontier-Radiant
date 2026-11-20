using Content.Shared._radiant.Borgs;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Toggleable;
using Content.Shared.Stunnable;
using Content.Shared.Buckle.Components;
using Content.Shared.Rotation;
using Content.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Analyzers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.ContentPack;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class BorgDamageVisualsTest
{
    [Test]
    public async Task ChassisUseTheirOwnDamagePosesAndRestoreAfterRevival()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var borg = em.SpawnEntity("BorgChassisGeneric", map.GridCoords);
                var transponder = em.GetComponent<BorgTransponderComponent>(borg);
                Assert.That(transponder.Sprite, Is.TypeOf<SpriteSpecifier.Rsi>());
                var icon = (SpriteSpecifier.Rsi) transponder.Sprite;
                Assert.That(icon.RsiPath, Is.EqualTo(new ResPath("_radiant/Mobs/Silicon/chassis.rsi")));
                Assert.That(icon.RsiState, Is.EqualTo("dullahansci"));
            });
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var appearances = em.System<AppearanceSystem>();
                var sprites = em.System<SpriteSystem>();
                // An unnamed source DMI cell used to shift the imported poses by one cell:
                // the first resting direction was standing, and the base body's first cell was a light overlay.
                var resources = pair.Client.ResolveDependency<IResourceManager>();
                foreach (var resting in new[] { false, true })
                {
                    using var stream = resources.ContentFileRead(new ResPath("/Textures/_radiant/Mobs/Silicon/chassis.rsi/"
                        + (resting ? "dullahansci_rest.png" : "dullahansci.png")));
                    using var image = Image.Load<Rgba32>(stream);
                    var columns = image.Width / 32;
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var top = 64;
                        var bottom = -1;
                        for (var y = 0; y < 64; y++)
                        for (var x = 0; x < 32; x++)
                        {
                            if (image[(direction % columns) * 32 + x, (direction / columns) * 64 + y].A == 0)
                                continue;
                            top = System.Math.Min(top, y);
                            bottom = System.Math.Max(bottom, y);
                        }
                        Assert.That(bottom - top + 1, resting ? Is.InRange(25, 38) : Is.InRange(40, 52),
                            $"Dullahan direction {direction}: all four cells must belong to the same pose.");
                    }
                }
                foreach (var id in new[]
                {
                    "BorgChassisGeneric", "BorgChassisEngineer", "BorgChassisMining", "BorgChassisJanitor",
                    "BorgChassisMedical", "BorgChassisService", "BorgChassisMercenary", "BorgChassisDonat",
                    "BorgChassisSyndicateAssault", "BorgChassisSyndicateMedical", "BorgChassisSyndicateSaboteur",
                })
                {
                    var borg = em.SpawnEntity(id, MapCoordinates.Nullspace);
                    var sprite = em.GetComponent<SpriteComponent>(borg);
                    var visuals = em.GetComponent<BorgDamageVisualsComponent>(borg);
                    var body = visuals.BodyState;
                    string moving = null;
                    if (em.TryGetComponent<BorgSwitchableTypeComponent>(borg, out var selected)
                        && pair.Client.ProtoMan.TryIndex(selected.SelectedBorgType, out var type))
                    {
                        body = type.SpriteBodyState;
                        moving = type.SpriteBodyMovementState;
                    }
                    if (id == "BorgChassisGeneric")
                        Assert.That(body, Is.EqualTo("dullahansci"));
                    var aliveDepth = sprite.DrawDepth;
                    appearances.SetData(borg, BorgVisuals.HasPlayer, true);
                    appearances.SetData(borg, ToggleableVisuals.Enabled, true);
                    SetState(MobState.Alive);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(body), id);
                    em.AddComponent<StunnedComponent>(borg);
                    // Exercise the actual mobility refresh queued by stun startup/removal.
                    appearances.FrameUpdate(0);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(body + "_tipped"), id + ": stun");
                    Assert.That(sprite[sprites.LayerMapGet((borg, sprite), BorgVisualLayers.Light)].Visible, Is.False);
                    em.RemoveComponent<StunnedComponent>(borg);
                    appearances.FrameUpdate(0);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(body), id + ": stun ended");
                    Assert.That(sprite[sprites.LayerMapGet((borg, sprite), BorgVisualLayers.Light)].Visible, Is.True);
                    SetState(MobState.Alive, fallen: true);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(body + "_tipped"), id + ": prone");
                    Assert.That(sprite.Rotation, Is.EqualTo(Angle.Zero));
                    appearances.SetData(borg, BuckleVisuals.Buckled, true);
                    SetState(MobState.Alive, fallen: true);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(body),
                        "Buckling must not use the knockdown pose.");
                    appearances.SetData(borg, BuckleVisuals.Buckled, false);
                    SetState(MobState.Alive);
                    em.AddComponent<StunnedComponent>(borg);
                    foreach (var state in new[] { MobState.Critical, MobState.Dead })
                    {
                        sprites.SetRotation((borg, sprite), Angle.FromDegrees(90));
                        SetState(state);
                        var expected = body + (state == MobState.Dead ? "_wreck" : "_rest");
                        Assert.That(sprite.BaseRSI!.TryGetState(expected, out _), Is.True, id);
                        Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(expected), id);
                        Assert.That(sprite.Rotation, Is.EqualTo(Angle.Zero), "Custom poses must not be rotated on their side.");
                        if (em.TryGetComponent<AnimationPlayerComponent>(borg, out var animation))
                            Assert.That(em.System<AnimationPlayerSystem>().HasRunningAnimation(animation, "rotate"), Is.False,
                                "A pending falling animation must not rotate the custom pose on a later frame.");
                        Assert.That(sprite[sprites.LayerMapGet((borg, sprite), BorgVisualLayers.Light)].Visible, Is.False);
                        Assert.That(sprite[sprites.LayerMapGet((borg, sprite), "light")].Visible, Is.False);
                        if (state == MobState.Dead)
                            Assert.That(sprite.DrawDepth, Is.LessThanOrEqualTo((int) DrawDepth.DeadMobs));
                        if (em.TryGetComponent<SpriteMovementComponent>(borg, out var movement))
                        {
                            movement.IsMoving = true;
                            var changed = new AfterAutoHandleStateEvent(null!);
                            em.EventBus.RaiseLocalEvent(borg, ref changed);
                            Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(expected), "Movement/type state updates must not overwrite the damage pose.");
                        }
                    }
                    em.RemoveComponent<StunnedComponent>(borg);
                    SetState(MobState.Alive);
                    Assert.That(sprites.LayerGetRsiState((borg, sprite), 0).Name, Is.EqualTo(moving ?? body));
                    Assert.That(sprite.DrawDepth, Is.EqualTo(aliveDepth));
                    Assert.That(sprite[sprites.LayerMapGet((borg, sprite), BorgVisualLayers.Light)].Visible, Is.True);
                    Assert.That(sprite[sprites.LayerMapGet((borg, sprite), "light")].Visible, Is.True);
                    em.DeleteEntity(borg);

                    void SetState(MobState state, bool fallen = false)
                    {
                        appearances.SetData(borg, MobStateVisuals.State, state);
                        appearances.SetData(borg, RotationVisuals.RotationState,
                            state == MobState.Alive && !fallen ? RotationState.Vertical : RotationState.Horizontal);
                        var changed = new AppearanceChangeEvent
                        {
                            Component = em.GetComponent<AppearanceComponent>(borg),
                            Sprite = sprite,
                        };
                        em.EventBus.RaiseLocalEvent(borg, ref changed);
                    }
                }
                foreach (var id in new[] { "BorgChassisDerelict", "EngineeringBorgChassisDerelict", "JanitorBorgChassisDerelict",
                    "MedicalBorgChassisDerelict", "MiningBorgChassisDerelict", "SyndicateAssaultBorgChassisDerelict" })
                {
                    Assert.That(pair.Client.ProtoMan.Index<EntityPrototype>(id)
                        .TryGetComponent<BorgDamageVisualsComponent>(out _), Is.False, "Derelict borgs must retain their existing visuals.");
                }
            });
            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine($"Borg visuals failure: {exception}");
            throw;
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
