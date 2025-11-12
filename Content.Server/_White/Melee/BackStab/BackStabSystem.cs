using System.Numerics;
using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._White.Melee.BackStab;

public sealed class BackStabSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BackStabComponent, MeleeHitEvent>(HandleHit);
    }

    private void HandleHit(Entity<BackStabComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count != 1)
            return;

        var target = args.HitEntities[0];

        if (target == args.User
            || !HasComp<MobStateComponent>(target)
            || !TryComp(target, out TransformComponent? xform))
        {
            return;
        }

        var userXform = Transform(args.User);
        Vector2 v1 = (_transform.GetWorldRotation(xform) + MathHelper.PiOver2).ToVec();
        Vector2 v2 = _transform.GetWorldPosition(userXform) - _transform.GetWorldPosition(xform);

        Vector3 vector1 = new Vector3(v1.X, v1.Y, 0);  
        Vector3 vector2 = new Vector3(v2.X, v2.Y, 0);

        float dotProduct = Vector3.Dot(vector1, vector2);
        float magnitude1 = vector1.Length();
        float magnitude2 = vector2.Length();

        float angle = MathF.Acos(dotProduct / (magnitude1 * magnitude2));

        float angleInDegrees = angle * (180f / MathF.PI);

        if (angle > ent.Comp.Tolerance.Theta)
            return;

        var damage = args.BaseDamage.GetTotal() * ent.Comp.DamageMultiplier;

        args.BonusDamage = new DamageSpecifier(_prototypeManager.Index<DamageTypePrototype>("Slash"),
            damage - args.BaseDamage.GetTotal());

        var message = Loc.GetString("melee-backstab-damage", ("damage", damage));
        _popup.PopupEntity(message, args.User, args.User, PopupType.MediumCaution);
    }
}