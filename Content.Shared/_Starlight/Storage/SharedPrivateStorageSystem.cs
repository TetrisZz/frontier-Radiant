using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Strip;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._Starlight.Storage;

public abstract partial class SharedPrivateStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrivateStorageComponent, PrivateStorageDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PrivateStorageComponent, GetVerbsEvent<ActivationVerb>>(AddPrivateStorageVerb);
        SubscribeLocalEvent<PrivateStorageComponent, ActivateInWorldEvent>(OnActivate,
            after: [typeof(SharedStrippableSystem)]);
    }

    private void OnDoAfter(EntityUid uid, PrivateStorageComponent component, DoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !Exists(args.Args.User))
            return;

        if (TryComp<StorageComponent>(uid, out var storage))
            _storage.OpenStorageUI(uid, args.Args.User, storage, false);

        args.Handled = true;
    }

    private void AddPrivateStorageVerb(EntityUid uid, PrivateStorageComponent component,
        GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<StorageComponent>(uid))
            return;

        var uiOpen = _ui.IsUiOpen(uid, StorageComponent.StorageUiKey.Key, args.User);
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString(uiOpen ? "comp-storage-verb-close-storage" : "comp-storage-verb-open-storage"),
            Icon = new SpriteSpecifier.Texture(new(uiOpen
                ? "/Textures/Interface/VerbIcons/close.svg.192dpi.png"
                : "/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
            Act = () =>
            {
                if (uiOpen)
                    _ui.CloseUi(uid, StorageComponent.StorageUiKey.Key, args.User);
                else
                    StartPrivateStorageAccess(uid, args.User, component);
            },
        });
    }

    private void OnActivate(EntityUid uid, PrivateStorageComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !CanInteract(args.User, uid))
            return;

        if (_ui.IsUiOpen(uid, StorageComponent.StorageUiKey.Key, args.User))
            _ui.CloseUi(uid, StorageComponent.StorageUiKey.Key, args.User);
        else
            StartPrivateStorageAccess(uid, args.User, component);

        args.Handled = true;
    }

    private bool CanInteract(EntityUid user, EntityUid storage)
    {
        if (HasComp<BypassInteractionChecksComponent>(user))
            return true;

        var ev = new StorageInteractAttemptEvent(true);
        RaiseLocalEvent(storage, ref ev);
        return !ev.Cancelled;
    }

    private void StartPrivateStorageAccess(EntityUid uid, EntityUid user, PrivateStorageComponent component)
    {
        if (uid == user)
        {
            if (TryComp<StorageComponent>(uid, out var storage))
                _storage.OpenStorageUI(uid, user, storage, false);
            return;
        }

        var args = new DoAfterArgs(EntityManager, user, component.AccessDelay,
            new PrivateStorageDoAfterEvent(), uid, target: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            RequireCanInteract = true,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };

        _popup.PopupEntity(Loc.GetString(component.AccessPopup, ("user", user)), uid, uid, PopupType.Medium);
        _doAfter.TryStartDoAfter(args);
    }
}
