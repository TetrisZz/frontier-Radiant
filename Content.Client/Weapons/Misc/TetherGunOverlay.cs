using Content.Shared.Weapons.Misc;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client.Weapons.Misc;

public sealed class TetherGunOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    private static readonly SpriteSpecifier.Rsi LeashRopeSprite = new(new ResPath("/Textures/Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    private IEntityManager _entManager;

    public TetherGunOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var query = _entManager.EntityQueryEnumerator<TetheredComponent>();
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var tetherGunQuery = _entManager.GetEntityQuery<TetherGunComponent>();
        var forceQuery = _entManager.GetEntityQuery<ForceGunComponent>();
        var leashPullerQuery = _entManager.GetEntityQuery<PlayerLeashPullerComponent>();
        var worldHandle = args.WorldHandle;
        var xformSystem = _entManager.System<SharedTransformSystem>();
        var spriteSystem = _entManager.System<SpriteSystem>();
        var leashRopeTexture = spriteSystem.Frame0(LeashRopeSprite);
        var leashRopeWidth = leashRopeTexture.Width / (float) EyeManager.PixelsPerMeter;

        while (query.MoveNext(out var uid, out var tethered))
        {
            var gun = tethered.Tetherer;

            if (!xformQuery.TryGetComponent(gun, out var gunXform) ||
                !xformQuery.TryGetComponent(uid, out var xform))
            {
                continue;
            }

            if (xform.MapID != gunXform.MapID)
                continue;

            var worldPos = xformSystem.GetWorldPosition(xform, xformQuery);
            var gunWorldPos = xformSystem.GetWorldPosition(gunXform, xformQuery);
            var diff = worldPos - gunWorldPos;
            var angle = diff.ToWorldAngle();
            var length = diff.Length() / 2f;
            var midPoint = gunWorldPos + diff / 2;
            const float Width = 0.05f;

            var box = new Box2(-Width, -length, Width, length);
            var rotated = new Box2Rotated(box.Translated(midPoint), angle, midPoint);

            var color = Color.Red;

            if (forceQuery.TryGetComponent(tethered.Tetherer, out var force))
            {
                color = force.LineColor;
            }
            else if (tetherGunQuery.TryGetComponent(tethered.Tetherer, out var tether))
            {
                color = tether.LineColor;
            }
            else if (leashPullerQuery.TryGetComponent(tethered.Tetherer, out var leashPuller))
            {
                color = leashPuller.LineColor;
                var ropeBox = new Box2(-leashRopeWidth / 2f, -length, leashRopeWidth / 2f, length);
                var ropeRotated = new Box2Rotated(ropeBox.Translated(midPoint), angle, midPoint);
                worldHandle.DrawTextureRect(leashRopeTexture, ropeRotated, color);
                continue;
            }

            worldHandle.DrawRect(rotated, color.WithAlpha(0.3f));
        }
    }
}
