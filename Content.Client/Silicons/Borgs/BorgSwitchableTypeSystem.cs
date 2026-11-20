using Content.Shared.Movement.Components;
using Content.Shared.Silicons.Borgs;
using Content.Client._radiant.Borgs;
using Content.Shared._radiant.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Silicons.Borgs;

/// <summary>
/// Client side logic for borg type switching. Sets up primarily client-side visual information.
/// </summary>
/// <seealso cref="SharedBorgSwitchableTypeSystem"/>
/// <seealso cref="BorgSwitchableTypeComponent"/>
public sealed class BorgSwitchableTypeSystem : SharedBorgSwitchableTypeSystem
{
    [Dependency] private readonly BorgSystem _borgSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly BorgDamageVisualsSystem _damageVisuals = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableTypeComponent, AfterAutoHandleStateEvent>(AfterStateHandler);
        SubscribeLocalEvent<BorgSwitchableTypeComponent, ComponentStartup>(OnComponentStartup);
    }

    private void OnComponentStartup(Entity<BorgSwitchableTypeComponent> ent, ref ComponentStartup args)
    {
        UpdateEntityAppearance(ent);
    }

    private void AfterStateHandler(Entity<BorgSwitchableTypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateEntityAppearance(ent);
    }

    protected override void UpdateEntityAppearance(
        Entity<BorgSwitchableTypeComponent> entity,
        BorgTypePrototype prototype)
    {
        TryComp<BorgChassisSkinComponent>(entity, out var skin);
        var bodyState = skin?.BodyState ?? prototype.SpriteBodyState;
        var movementState = skin == null ? prototype.SpriteBodyMovementState : skin.MovementState;
        if (TryComp(entity, out SpriteComponent? sprite))
        {
            _sprite.LayerSetRsiState((entity, sprite), BorgVisualLayers.Body, bodyState);
            _sprite.LayerSetRsiState((entity, sprite), BorgVisualLayers.LightStatus,
                skin == null ? prototype.SpriteToggleLightState : skin.LightState ?? bodyState + "_l");
        }

        if (TryComp(entity, out BorgChassisComponent? chassis))
        {
            _borgSystem.SetMindStates(
                (entity.Owner, chassis),
                skin == null ? prototype.SpriteHasMindState : skin.HasMindState ?? bodyState + "_e",
                skin == null ? prototype.SpriteNoMindState : skin.NoMindState ?? bodyState + "_e_r");

            if (TryComp(entity, out AppearanceComponent? appearance))
            {
                // Queue update so state changes apply.
                _appearance.QueueUpdate(entity, appearance);
            }
        }

        if (movementState != null)
        {
            var spriteMovement = EnsureComp<SpriteMovementComponent>(entity);
            spriteMovement.NoMovementLayers.Clear();
            spriteMovement.NoMovementLayers["movement"] = new PrototypeLayerData
            {
                State = bodyState,
            };
            spriteMovement.MovementLayers.Clear();
            spriteMovement.MovementLayers["movement"] = new PrototypeLayerData
            {
                State = movementState,
            };
        }
        else
        {
            RemComp<SpriteMovementComponent>(entity);
        }

        base.UpdateEntityAppearance(entity, prototype);
        _damageVisuals.UpdateVisuals(entity.Owner);
    }
}
