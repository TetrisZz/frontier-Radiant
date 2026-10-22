using Content.Shared._radiant.Social;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Social;

public sealed class DancePoleSystem : EntitySystem
{
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly PointLightSystem _lights = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DancePoleComponent, MapInitEvent>((Entity<DancePoleComponent> ent, ref MapInitEvent _) => UpdateState(ent));
        SubscribeLocalEvent<DancePoleComponent, BeforeActivatableUIOpenEvent>((Entity<DancePoleComponent> ent, ref BeforeActivatableUIOpenEvent _) => UpdateState(ent));
        SubscribeLocalEvent<DancePoleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PoleDancerComponent, UnbuckledEvent>((Entity<PoleDancerComponent> ent, ref UnbuckledEvent _) => Stop(ent.Comp.Pole));
        SubscribeLocalEvent<PoleDancerComponent, DamageChangedEvent>((Entity<PoleDancerComponent> ent, ref DamageChangedEvent args) =>
        {
            if (args.DamageIncreased)
                Stop(ent.Comp.Pole);
        });
        Subs.BuiEvents<DancePoleComponent>(DancePoleUiKey.Key, s => s.Event<DancePoleMessage>(OnMessage));
    }

    private void OnMessage(Entity<DancePoleComponent> ent, ref DancePoleMessage args)
    {
        if (!_interaction.InRangeUnobstructed(args.Actor, ent.Owner) || !_blocker.CanInteract(args.Actor, ent) ||
            !Transform(ent).Anchored)
            return;
        switch (args.Action)
        {
            case DancePoleAction.Dance:
                if (args.Value is < 0 or > 2 || !_mobs.IsAlive(args.Actor) || !HasComp<HumanoidAppearanceComponent>(args.Actor))
                    return;
                if (ent.Comp.Dancer is { } other && other != args.Actor)
                {
                    _popup.PopupEntity(Loc.GetString("dance-pole-occupied"), ent, args.Actor);
                    return;
                }
                if (!TryComp<BuckleComponent>(args.Actor, out var buckle) ||
                    buckle.BuckledTo != ent.Owner && !_buckle.TryBuckle(args.Actor, args.Actor, ent))
                    return;
                ent.Comp.Dancer = args.Actor;
                var dancer = EnsureComp<PoleDancerComponent>(args.Actor);
                dancer.Pole = ent;
                dancer.Move = (PoleDanceMove) args.Value;
                dancer.StartedAt = _timing.CurTime;
                Dirty(args.Actor, dancer);
                break;
            case DancePoleAction.Stop:
                if (ent.Comp.Dancer != args.Actor)
                    return;
                Stop(ent.Owner);
                return;
            case DancePoleAction.Finish:
                if (args.Value < 0 || args.Value >= DancePolePalette.Finishes.Length)
                    return;
                ent.Comp.Finish = args.Value;
                break;
            case DancePoleAction.LightColor:
                if (args.Value < 0 || args.Value >= DancePolePalette.Lights.Length)
                    return;
                ent.Comp.LightColor = args.Value;
                break;
            case DancePoleAction.LightToggle:
                ent.Comp.LightEnabled = !ent.Comp.LightEnabled;
                break;
            default:
                return;
        }
        UpdateState(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DancePoleComponent>();
        while (query.MoveNext(out var uid, out var pole))
        {
            if (pole.Dancer is not { } dancer)
                continue;
            if (TerminatingOrDeleted(dancer) || !_mobs.IsAlive(dancer) || !_blocker.CanInteract(dancer, uid) ||
                !Transform(uid).Anchored || !TryComp<BuckleComponent>(dancer, out var buckle) || buckle.BuckledTo != uid)
                Stop(uid);
        }
    }

    private void OnShutdown(Entity<DancePoleComponent> ent, ref ComponentShutdown args) => Stop(ent.Owner, update: false);

    private void Stop(Entity<DancePoleComponent?> ent, bool update = true)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false) || ent.Comp.Dancer is not { } dancer)
            return;
        ent.Comp.Dancer = null;
        if (!TerminatingOrDeleted(dancer))
        {
            RemComp<PoleDancerComponent>(dancer);
            if (TryComp<BuckleComponent>(dancer, out var buckle) && buckle.BuckledTo == ent.Owner)
                // Ending a dance must also release a performer during the normal buckle cooldown.
                _buckle.Unbuckle((dancer, buckle), null);
        }
        if (update && !TerminatingOrDeleted(ent.Owner))
            UpdateState((ent.Owner, ent.Comp));
    }

    private void UpdateState(Entity<DancePoleComponent> ent)
    {
        _appearance.SetData(ent, DancePoleVisuals.Finish, ent.Comp.Finish);
        _lights.SetEnabled(ent.Owner, ent.Comp.LightEnabled);
        _lights.SetColor(ent.Owner, DancePolePalette.Lights[Math.Clamp(ent.Comp.LightColor, 0, DancePolePalette.Lights.Length - 1)]);
        var move = ent.Comp.Dancer is { } dancer && TryComp<PoleDancerComponent>(dancer, out var dancing)
            ? dancing.Move : PoleDanceMove.Sway;
        _ui.SetUiState(ent.Owner, DancePoleUiKey.Key,
            new DancePoleUiState(GetNetEntity(ent.Comp.Dancer), move, ent.Comp.Finish, ent.Comp.LightEnabled, ent.Comp.LightColor));
    }
}
