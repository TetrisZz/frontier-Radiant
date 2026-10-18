using Content.Shared.Emag.Systems;
using Content.Shared.Lock;
using Content.Shared.Storage;
using Robust.Shared.Timing;

namespace Content.Shared._radiant.Storage;

public sealed class PasswordSafeSystem : EntitySystem
{
    [Dependency] private readonly LockSystem _locks = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PasswordSafeComponent, LockToggleAttemptEvent>(OnToggleAttempt);
        SubscribeLocalEvent<PasswordSafeComponent, BoundUserInterfaceMessageAttempt>(OnUiAttempt);
        SubscribeLocalEvent<PasswordSafeComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<PasswordSafeComponent, LockToggledEvent>(OnLockToggled);
    }

    private void OnToggleAttempt(Entity<PasswordSafeComponent> ent, ref LockToggleAttemptEvent args)
        => args.Cancelled = true;

    private void OnUiAttempt(Entity<PasswordSafeComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.UiKey is StorageComponent.StorageUiKey && _locks.IsLocked(ent.Owner))
            args.Cancel();
    }

    private void OnEmagged(Entity<PasswordSafeComponent> ent, ref GotEmaggedEvent args)
    {
        if (ent.Comp.Broken || !_locks.IsLocked(ent.Owner) ||
            (args.Type & (EmagType.Interaction | EmagType.Access)) == 0)
            return;

        ent.Comp.Broken = true;
        Dirty(ent);
        _locks.Unlock(ent, null);
        Animate(ent, PasswordSafeState.Sparking, 1.2);
        args.Handled = true;
    }

    private void OnLockToggled(Entity<PasswordSafeComponent> ent, ref LockToggledEvent args)
    {
        if (ent.Comp.Broken && args.Locked)
        {
            _locks.Unlock(ent, null);
            return;
        }
        Animate(ent, args.Locked ? PasswordSafeState.Locking : PasswordSafeState.Open,
            args.Locked ? 0.6 : 0);
    }

    private void Animate(Entity<PasswordSafeComponent> ent, PasswordSafeState state, double seconds)
    {
        ent.Comp.AnimationUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        _appearance.SetData(ent, PasswordSafeVisuals.State, state);
        var ev = new PasswordSafeChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }
}
