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
    private readonly HashSet<EntityUid> _pendingCooldowns = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<FightingForLifeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FightingForLifeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MobStateActionsComponent, FightForLifeEvent>(OnFightForLife);
        SubscribeLocalEvent<MobStateChangedEvent>(OnCriticalStateChanged);
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
            DamageDict = new Dictionary<string, FixedPoint2> { ["Asphyxiation"] = -10 }
        }, ignoreResistances: true, interruptsDoAfters: false, origin: uid);

        args.Handled = true;
    }

    private void OnCriticalStateChanged(MobStateChangedEvent args)
    {
        if (!HasComp<MobStateActionsComponent>(args.Target))
            return;

        if (args.NewMobState == MobState.Critical)
            _pendingCooldowns.Add(args.Target);
        else
            _pendingCooldowns.Remove(args.Target);
    }

    private void OnMobStateChanged(EntityUid uid, FightingForLifeComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
            RemCompDeferred<FightingForLifeComponent>(uid);
    }

    private void OnShutdown(EntityUid uid, FightingForLifeComponent component, ComponentShutdown args)
    {
        _expires.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // MobStateActionsSystem grants the action during the state-change event.
        // Apply the initial cooldown on the following frame, once the action exists.
        foreach (var uid in _pendingCooldowns.ToArray())
        {
            SetFightForLifeCooldown(uid);
            _pendingCooldowns.Remove(uid);
        }

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
            _actions.SetCooldown((action, (ActionComponent?) action.Comp), TimeSpan.FromSeconds(_random.Next(25, 121)));
            break;
        }
    }
}
