using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.Mobs;

/// <summary>
/// Handles the short burst of activity a player can make while in critical condition.
/// </summary>
public sealed class FightForLifeSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _expires = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<FightingForLifeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FightingForLifeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MobStateActionsComponent, FightForLifeEvent>(OnFightForLife);
        SubscribeLocalEvent<MobStateActionsComponent, ActionAddedEvent>(OnActionAdded);
    }

    private void OnActionAdded(EntityUid uid, MobStateActionsComponent component, ref ActionAddedEvent args)
    {
        if (MetaData(args.Action).EntityPrototype?.ID != "ActionFightForLife")
            return;

        // Apply before the newly granted action can be used, including after resuscitation.
        _actions.SetCooldown((args.Action, (ActionComponent?) args.Component), RecoveryDelay());
    }

    private void OnFightForLife(EntityUid uid, MobStateActionsComponent component, FightForLifeEvent args)
    {
        if (!_mobState.IsCritical(uid) || HasComp<FightingForLifeComponent>(uid))
            return;

        EnsureComp<FightingForLifeComponent>(uid);
        _expires[uid] = _timing.CurTime + TimeSpan.FromSeconds(11);
        _standing.Stand(uid, force: true);
        _blocker.UpdateCanMove(uid);

        // The adrenaline surge relieves a small amount of suffocation damage.
        _damageable.TryChangeDamage(uid, new DamageSpecifier
        {
            DamageDict = new Dictionary<string, FixedPoint2> { ["Asphyxiation"] = -5 }
        }, ignoreResistances: true, interruptsDoAfters: false, origin: uid);

        args.Handled = true;
    }

    private void OnMobStateChanged(EntityUid uid, FightingForLifeComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
        {
            RemComp<FightingForLifeComponent>(uid);
            _blocker.UpdateCanMove(uid);
            if (args.NewMobState == MobState.Dead)
                _standing.Down(uid, force: true);
        }
    }

    private void OnShutdown(EntityUid uid, FightingForLifeComponent component, ComponentShutdown args)
    {
        _expires.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (uid, expires) in _expires.ToArray())
        {
            if (_timing.CurTime < expires)
                continue;

            _expires.Remove(uid);
            if (Exists(uid) && _mobState.IsCritical(uid))
            {
                _standing.Down(uid, dropHeldItems: false, force: true);
                SetFightForLifeCooldown(uid);
            }

            RemComp<FightingForLifeComponent>(uid);
            _blocker.UpdateCanMove(uid);
        }
    }

    private void SetFightForLifeCooldown(EntityUid uid)
    {
        foreach (var action in _actions.GetActions(uid))
        {
            if (MetaData(action).EntityPrototype?.ID != "ActionFightForLife")
                continue;

            // Radiant Sector: extended fight-for-life recovery cooldown.
            _actions.SetCooldown((action, (ActionComponent?) action.Comp), RecoveryDelay());
            break;
        }
    }

    private TimeSpan RecoveryDelay() => TimeSpan.FromSeconds(_random.Next(35, 150));
}
