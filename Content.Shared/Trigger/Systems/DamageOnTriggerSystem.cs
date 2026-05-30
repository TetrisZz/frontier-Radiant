using Content.Shared.Damage;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Containers;

namespace Content.Shared.Trigger.Systems;

public sealed class DamageOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!; // Radiant add TargetContainer
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<DamageOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != null && !ent.Comp.KeysIn.Contains(args.Key))
            return;

// Radiant add TargetContainer
        EntityUid? target;
        if (ent.Comp.TargetContainer)
        {
            // damage whoever is wearing this clothing item
            if (!_container.TryGetContainingContainer(ent.Owner, out var container))
                return;
            target = container.Owner;
        }
        else
        {
            target = ent.Comp.TargetUser ? args.User : ent.Owner;
        }
// Radiant end
        if (target == null)
            return;

        var damage = new DamageSpecifier(ent.Comp.Damage);
        var ev = new BeforeDamageOnTriggerEvent(damage, target.Value);
        RaiseLocalEvent(ent.Owner, ref ev);

        args.Handled |= _damageableSystem.TryChangeDamage(target, ev.Damage, ent.Comp.IgnoreResistances, origin: ent.Owner) is not null;
    }
}

/// <summary>
/// Raised on an entity before it deals damage using DamageOnTriggerComponent.
/// Used to modify the damage that will be dealt.
/// </summary>
[ByRefEvent]
public record struct BeforeDamageOnTriggerEvent(DamageSpecifier Damage, EntityUid Tripper);
