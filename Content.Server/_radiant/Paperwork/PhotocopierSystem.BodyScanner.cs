using Content.Server.Power.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Paperwork;

public sealed partial class PhotocopierSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    private void InitializeBodyScanner()
    {
        SubscribeLocalEvent<PhotocopierComponent, GetVerbsEvent<Verb>>(OnBodyScanVerb);
        SubscribeLocalEvent<PhotocopierComponent, PhotocopierBodyScanDoAfterEvent>(OnBodyScanFinished);
    }

    private void OnBodyScanVerb(Entity<PhotocopierComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !TryComp<PhotocopierBodyScannerComponent>(ent, out var scanner) ||
            !TryGetBodyPrint(args.User, scanner, out _))
            return;

        var user = args.User;
        var reason = BodyScanBlockedReason(ent, user);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("photocopier-body-print"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
            Disabled = reason != null,
            Message = reason == null ? null : Loc.GetString(reason),
            Act = () => TryStartBodyPrint(ent, user),
        });
    }

    public bool TryStartBodyPrint(EntityUid copier, EntityUid user)
    {
        if (!TryComp<PhotocopierComponent>(copier, out var printer) ||
            !TryComp<PhotocopierBodyScannerComponent>(copier, out var scanner) ||
            !TryGetBodyPrint(user, scanner, out _))
            return false;

        if (BodyScanBlockedReason((copier, printer), user) is { } reason)
        {
            _popup.PopupEntity(Loc.GetString(reason), copier, user);
            return false;
        }

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, scanner.Delay,
            new PhotocopierBodyScanDoAfterEvent(), copier, target: copier)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            BlockDuplicate = true,
        });
        if (started)
            _popup.PopupEntity(Loc.GetString("photocopier-body-start"), copier, user);
        return started;
    }

    private string? BodyScanBlockedReason(Entity<PhotocopierComponent> ent, EntityUid user)
    {
        if (!_blocker.CanInteract(user, ent) || !_interaction.InRangeAndAccessible(user, ent.Owner))
            return "photocopier-body-unreachable";
        if (!TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered)
            return "photocopier-body-no-power";
        if (_timing.CurTime < ent.Comp.ReadyAt)
            return "photocopier-body-busy";
        if (_slots.GetItemOrNull(ent, "original") != null)
            return "photocopier-body-original";
        if (ent.Comp.Paper < 1 || ent.Comp.Ink < 1)
            return "photocopier-no-supplies";
        return null;
    }

    private bool TryGetBodyPrint(EntityUid user, PhotocopierBodyScannerComponent scanner, out EntProtoId prototype)
    {
        prototype = default;
        if (!HasComp<DoAfterComponent>(user))
            return false;
        if (HasComp<BorgChassisComponent>(user) && scanner.SiliconPrint is { } silicon)
        {
            prototype = silicon;
            return true;
        }
        if (!TryComp<HumanoidAppearanceComponent>(user, out var appearance))
            return false;
        if (!scanner.SpeciesPrints.TryGetValue(appearance.Species, out prototype))
            prototype = appearance.Sex == Sex.Female ? scanner.FemalePrint : scanner.MalePrint;
        return true;
    }

    private void OnBodyScanFinished(Entity<PhotocopierComponent> ent, ref PhotocopierBodyScanDoAfterEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (args.Cancelled || !TryComp<PhotocopierBodyScannerComponent>(ent, out var scanner) ||
            !TryGetBodyPrint(args.Args.User, scanner, out var prototype) ||
            BodyScanBlockedReason(ent, args.Args.User) != null)
            return;

        var sheet = Spawn(prototype, Transform(ent).Coordinates);
        _paper.SetContent(sheet, Loc.GetString("photocopier-body-caption"));
        ent.Comp.Paper--;
        ent.Comp.Ink--;
        ent.Comp.ReadyAt = _timing.CurTime + ent.Comp.PrintDuration;
        _audio.PlayPvs(new SoundCollectionSpecifier("PrinterPrint"), ent);
        _popup.PopupEntity(Loc.GetString("photocopier-body-finish"), sheet, args.Args.User);
        UpdateState(ent);
    }
}
