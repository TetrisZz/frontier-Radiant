using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Shared.Store;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Donor;

public sealed class DonorUplinkSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SoundSpecifier _open = new SoundPathSpecifier("/Audio/_radiant/Uplink/open.ogg", AudioParams.Default.WithVolume(-10));
    private readonly SoundSpecifier _close = new SoundPathSpecifier("/Audio/_radiant/Uplink/close.ogg", AudioParams.Default.WithVolume(-12));
    private readonly SoundSpecifier _deny = new SoundPathSpecifier("/Audio/_radiant/Uplink/denied.ogg", AudioParams.Default.WithVolume(-12));

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<DonorUplinkComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<DonorUplinkComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<DonorUplinkComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
        Subs.BuiEvents<DonorUplinkComponent>(StoreUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<BoundUIClosedEvent>(OnClosed);
        });
    }

    private NetUserId? UserId(EntityUid user)
    {
        if (TryComp<ActorComponent>(user, out var actor))
            return actor.PlayerSession.UserId;
        return _minds.TryGetMind(user, out _, out var mind) ? mind.UserId : null;
    }

    public bool CanUse(Entity<DonorUplinkComponent> ent, EntityUid user)
    {
        if (UserId(user) is not { } id)
            return false;
        BindToHolder(ent);
        ent.Comp.Owner ??= id;
        return ent.Comp.Owner == id;
    }

    private void BindToHolder(Entity<DonorUplinkComponent> ent)
    {
        if (ent.Comp.Owner != null)
            return;
        var parent = Transform(ent).ParentUid;
        while (parent.IsValid())
        {
            if (UserId(parent) is { } id)
            {
                ent.Comp.Owner = id;
                return;
            }
            parent = Transform(parent).ParentUid;
        }
    }

    private void OnInserted(Entity<DonorUplinkComponent> ent, ref EntGotInsertedIntoContainerMessage args)
        => BindToHolder(ent);

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        // Items can be placed in backpacks before the character receives its mind.
        var query = EntityQueryEnumerator<DonorUplinkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Owner != null)
                continue;
            var parent = Transform(uid).ParentUid;
            while (parent.IsValid())
            {
                if (parent == args.Mob)
                {
                    comp.Owner = args.Player.UserId;
                    break;
                }
                parent = Transform(parent).ParentUid;
            }
        }
    }

    private void Deny(Entity<DonorUplinkComponent> ent, EntityUid user)
    {
        if (ent.Comp.NextFeedback > _timing.CurTime)
            return;
        ent.Comp.NextFeedback = _timing.CurTime + TimeSpan.FromSeconds(1);
        _popup.PopupEntity(Loc.GetString("donor-uplink-owner-only"), ent, user);
        _audio.PlayEntity(_deny, user, ent);
    }

    private void OnOpenAttempt(Entity<DonorUplinkComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (CanUse(ent, args.User))
            return;
        args.Cancel();
        Deny(ent, args.User);
    }

    private void OnMessageAttempt(Entity<DonorUplinkComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (!Equals(args.UiKey, StoreUiKey.Key) || CanUse(ent, args.Actor))
            return;
        args.Cancel();
        _ui.CloseUi(ent.Owner, StoreUiKey.Key, args.Actor);
        Deny(ent, args.Actor);
    }

    private void OnOpened(Entity<DonorUplinkComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Also guard direct server-side opens, which bypass the network message attempt.
        if (!CanUse(ent, args.Actor))
        {
            _ui.CloseUi(ent.Owner, StoreUiKey.Key, args.Actor);
            Deny(ent, args.Actor);
            return;
        }
        _audio.PlayEntity(_open, args.Actor, ent);
    }

    private void OnClosed(Entity<DonorUplinkComponent> ent, ref BoundUIClosedEvent args)
    {
        if (UserId(args.Actor) is { } id && id == ent.Comp.Owner)
            _audio.PlayEntity(_close, args.Actor, ent);
    }
}
