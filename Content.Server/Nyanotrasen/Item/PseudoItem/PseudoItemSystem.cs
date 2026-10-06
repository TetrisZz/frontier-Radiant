using Content.Server.Carrying;
using Content.Server.Popups;
using Content.Shared.Bed.Sleep;
using Content.Shared.IdentityManagement;
using Content.Shared.Item;
using Content.Shared.Nyanotrasen.Item.PseudoItem;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems; // Frontier

namespace Content.Server.Nyanotrasen.Item.PseudoItem;

public sealed partial class PseudoItemSystem : SharedPseudoItemSystem
{
    [Dependency] private CarryingSystem _carrying = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!; // Frontier

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PseudoItemComponent, GetVerbsEvent<AlternativeVerb>>(AddInsertAltVerb);
        SubscribeLocalEvent<PseudoItemComponent, TryingToSleepEvent>(OnTrySleeping);
    }

    private void AddInsertAltVerb(EntityUid uid, PseudoItemComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!component.Enabled || component.Active)
            return;

        if (args.User == uid)
            return;

        if (!TryComp<StorageComponent>(args.Using, out var targetStorage))
            return;

        if (!CheckItemFits((uid, component), (args.Using.Value, targetStorage)))
            return;

        if (!_hands.TryGetActiveItem(args.User, out var item))
            return;

        if (item.Value != args.Using.Value)
            return;

        AlternativeVerb verb = new()
        {
            Act = () =>
            {
                StartInsertDoAfter(args.User, uid, item.Value, component);
            },
            Text = Loc.GetString(
                "action-name-insert-other",
                ("target", Identity.Entity(uid, EntityManager))
            ),
            Priority = 2
        };

        args.Verbs.Add(verb);
    }

    protected override void OnGettingPickedUpAttempt(EntityUid uid, PseudoItemComponent component, GettingPickedUpAttemptEvent args)
    {
        // Try to pick the entity up instead first
        if (args.User != args.Item && _carrying.TryCarry(args.User, uid))
        {
            args.Cancel();
            return;
        }

        // If could not pick up, just take it out onto the ground as per default
        base.OnGettingPickedUpAttempt(uid, component, args);
    }

    // Show a popup when a pseudo-item falls asleep inside a bag.
    private void OnTrySleeping(EntityUid uid, PseudoItemComponent component, TryingToSleepEvent args)
    {
        var parent = Transform(uid).ParentUid;
        if (!HasComp<SleepingComponent>(uid) && parent is { Valid: true } && HasComp<AllowsSleepInsideComponent>(parent))
            _popup.PopupEntity(Loc.GetString("popup-sleep-in-bag", ("entity", uid)), uid);
    }
}
