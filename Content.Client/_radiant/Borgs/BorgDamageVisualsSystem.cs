using Content.Client.Animations;
using Content.Client.Rotation;
using Content.Client.Silicons.Borgs;
using Content.Client.Toggleable;
using Content.Shared._radiant.Borgs;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Rotation;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Toggleable;
using Content.Shared.Stunnable;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._radiant.Borgs;

public sealed class BorgDamageVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgDamageVisualsComponent, AppearanceChangeEvent>(OnAppearance,
            after: [typeof(BorgSystem), typeof(RotationVisualizerSystem), typeof(ToggleableVisualsSystem)]);
        SubscribeLocalEvent<BorgDamageVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BorgDamageVisualsComponent, UpdateCanMoveEvent>(OnMobilityChanged);
    }

    private void OnAppearance(Entity<BorgDamageVisualsComponent> ent, ref AppearanceChangeEvent args) => UpdateVisuals(ent);
    private void OnStartup(Entity<BorgDamageVisualsComponent> ent, ref ComponentStartup args) => UpdateVisuals(ent);

    private void OnMobilityChanged(EntityUid uid, BorgDamageVisualsComponent component, UpdateCanMoveEvent args)
    {
        // StunnedComponent startup/shutdown refreshes mobility on both client and server.
        // Queue rather than render here, so removal of the stun component is already complete.
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.QueueUpdate(uid, appearance);
    }

    /// <summary>Reapply the current pose after movement or a chassis type update changes the body layer.</summary>
    public void UpdateVisuals(EntityUid uid)
    {
        if (TryComp<BorgDamageVisualsComponent>(uid, out var visuals))
            UpdateVisuals((uid, visuals));
    }

    private void UpdateVisuals(Entity<BorgDamageVisualsComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) || !TryComp<AppearanceComponent>(ent, out var appearance))
            return;

        var body = ent.Comp.BodyState;
        string? moving = null;
        if (TryComp<BorgSwitchableTypeComponent>(ent, out var type)
            && _prototypes.TryIndex(type.SelectedBorgType, out var prototype))
        {
            body = prototype.SpriteBodyState;
            moving = prototype.SpriteBodyMovementState;
        }
        if (TryComp<BorgChassisSkinComponent>(ent, out var skin))
        {
            body = skin.BodyState;
            moving = skin.MovementState;
        }

        var bodyLayer = _sprite.LayerMapTryGet((ent.Owner, sprite), BorgVisualLayers.Body, out var mapped, false) ? mapped : 0;
        var rsi = _sprite.LayerGetEffectiveRsi((ent.Owner, sprite), bodyLayer);
        if (!_appearance.TryGetData<MobState>(ent, MobStateVisuals.State, out var state, appearance))
            state = MobState.Alive;
        _appearance.TryGetData<RotationState>(ent, RotationVisuals.RotationState, out var rotation, appearance);
        _appearance.TryGetData<bool>(ent, BuckleVisuals.Buckled, out var buckled, appearance);
        var fallen = !buckled && (HasComp<StunnedComponent>(ent) || HasComp<KnockedDownComponent>(ent)
            || rotation == RotationState.Horizontal);
        var desired = state switch
        {
            MobState.Critical => body + ent.Comp.CriticalSuffix,
            MobState.Dead => body + ent.Comp.DeadSuffix,
            _ when fallen => body + ent.Comp.TippedSuffix,
            _ => TryComp<SpriteMovementComponent>(ent, out var movement) && movement.IsMoving ? moving ?? body : body,
        };
        var poseRequested = state != MobState.Alive || fallen;
        var customPose = poseRequested && rsi?.TryGetState(desired, out _) == true;
        // Missing pose art falls back to the existing body and normal rotation.
        var rendered = poseRequested && !customPose ? body : desired;
        if (rsi?.TryGetState(rendered, out _) == true)
            _sprite.LayerSetRsiState((ent.Owner, sprite), bodyLayer, rendered);

        if (customPose)
        {
            // RotationVisuals may have just queued a fall while the current angle is still zero.
            // Cancel it explicitly or it will rotate the already-drawn resting/wreck sprite later.
            if (TryComp<AnimationPlayerComponent>(ent, out var animation) && _animation.HasRunningAnimation(animation, "rotate"))
                _animation.Stop((ent.Owner, animation), "rotate");
            _sprite.SetRotation((ent.Owner, sprite), Angle.Zero);
        }

        var showLights = state == MobState.Alive && !customPose;
        if (!showLights && _sprite.LayerMapTryGet((ent.Owner, sprite), BorgVisualLayers.Light, out var eyes, false))
            _sprite.LayerSetVisible((ent.Owner, sprite), eyes, false);
        _appearance.TryGetData<bool>(ent, ToggleableVisuals.Enabled, out var lightEnabled, appearance);
        if (_sprite.LayerMapTryGet((ent.Owner, sprite), "light", out var light, false))
            _sprite.LayerSetVisible((ent.Owner, sprite), light, showLights && lightEnabled);

        if (state == MobState.Dead && sprite.DrawDepth > (int) DrawDepth.DeadMobs)
        {
            ent.Comp.OriginalDrawDepth ??= sprite.DrawDepth;
            _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepth.DeadMobs);
        }
        else if (state != MobState.Dead && ent.Comp.OriginalDrawDepth is { } depth)
        {
            _sprite.SetDrawDepth((ent.Owner, sprite), depth);
            ent.Comp.OriginalDrawDepth = null;
        }
    }
}
