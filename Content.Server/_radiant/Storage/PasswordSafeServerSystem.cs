using Content.Shared._radiant.Storage;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Storage;

public sealed class PasswordSafeServerSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly LockSystem _locks = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PasswordSafeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PasswordSafeComponent, PasswordSafeChangedEvent>(OnChanged);
        Subs.BuiEvents<PasswordSafeComponent>(PasswordSafeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<PasswordSafeSubmitMessage>(OnSubmit);
            subs.Event<PasswordSafeCloseMessage>(OnClose);
        });
    }

    private void OnMapInit(Entity<PasswordSafeComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Broken)
            _locks.Unlock(ent, null);
        _appearance.SetData(ent, PasswordSafeVisuals.State,
            _locks.IsLocked(ent.Owner) ? PasswordSafeState.Closed : PasswordSafeState.Open);
        Publish(ent);
    }

    private void OnOpened(Entity<PasswordSafeComponent> ent, ref BoundUIOpenedEvent args)
    {
        Publish(ent);
        if (!_locks.IsLocked(ent.Owner) && CanUse(ent, args.Actor))
            _storage.OpenStorageUI(ent, args.Actor);
    }

    private void OnChanged(Entity<PasswordSafeComponent> ent, ref PasswordSafeChangedEvent args) => Publish(ent);

    private void OnSubmit(Entity<PasswordSafeComponent> ent, ref PasswordSafeSubmitMessage args)
        => SubmitCode(ent, args.Actor, args.Code, args.Confirmation, args.Configure);

    public bool SubmitCode(Entity<PasswordSafeComponent> ent, EntityUid user, string code, string confirmation, bool configure)
    {
        if (!CanUse(ent, user) || ent.Comp.Broken || !_locks.IsLocked(ent.Owner) ||
            !TryComp<PasswordSafeCodeComponent>(ent, out var secret))
            return false;

        if (!PasswordSafeCode.IsValid(code) || configure && code != confirmation)
        {
            _popup.PopupEntity(Loc.GetString("radiant-safe-invalid-code"), ent, user);
            return false;
        }

        // An outdated setup window must not overwrite a code set by another player.
        if (configure != (secret.Code == null))
        {
            Publish(ent);
            return false;
        }

        if (secret.NextAttempt > _timing.CurTime)
            return false;
        secret.NextAttempt = _timing.CurTime + TimeSpan.FromSeconds(1);

        if (configure)
            secret.Code = code;
        else if (secret.Code != code)
        {
            _popup.PopupEntity(Loc.GetString("radiant-safe-wrong-code"), ent, user);
            return false;
        }

        _locks.Unlock(ent, null);
        Publish(ent);
        _storage.OpenStorageUI(ent, user);
        return true;
    }

    private void OnClose(Entity<PasswordSafeComponent> ent, ref PasswordSafeCloseMessage args) => CloseSafe(ent, args.Actor);

    public bool CloseSafe(Entity<PasswordSafeComponent> ent, EntityUid user)
    {
        if (!CanUse(ent, user) || ent.Comp.Broken || _locks.IsLocked(ent.Owner) ||
            !TryComp<PasswordSafeCodeComponent>(ent, out var secret) || secret.Code == null)
            return false;
        _locks.Lock(ent, null);
        _ui.CloseUi(ent.Owner, StorageComponent.StorageUiKey.Key);
        Publish(ent);
        return true;
    }

    private bool CanUse(EntityUid safe, EntityUid user)
        => _blocker.CanComplexInteract(user) && _blocker.CanInteract(user, safe) &&
            _interaction.InRangeUnobstructed(user, safe);

    private void Publish(Entity<PasswordSafeComponent> ent)
    {
        var configured = TryComp<PasswordSafeCodeComponent>(ent, out var secret) && secret.Code != null;
        _ui.SetUiState(ent.Owner, PasswordSafeUiKey.Key,
            new PasswordSafeUiState(configured, !_locks.IsLocked(ent.Owner), ent.Comp.Broken));
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PasswordSafeComponent>();
        while (query.MoveNext(out var uid, out var safe))
        {
            if (safe.AnimationUntil == TimeSpan.Zero || safe.AnimationUntil > _timing.CurTime)
                continue;
            safe.AnimationUntil = TimeSpan.Zero;
            _appearance.SetData(uid, PasswordSafeVisuals.State,
                _locks.IsLocked(uid) ? PasswordSafeState.Closed : PasswordSafeState.Open);
        }
    }
}
