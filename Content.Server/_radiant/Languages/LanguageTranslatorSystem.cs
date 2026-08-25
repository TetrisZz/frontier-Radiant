using Content.Server.PowerCell;
using Content.Server.DoAfter;
using Content.Shared._radiant.Languages;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Languages;

/// <summary>
/// Scans paper documents and displays a machine translation to the user.
/// </summary>
public sealed class LanguageTranslatorSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LanguageTranslatorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LanguageTranslatorComponent, LanguageTranslatorScanDoAfterEvent>(OnScanFinished);
    }

    private void OnAfterInteract(Entity<LanguageTranslatorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<PaperComponent>(target))
            return;

        args.Handled = true;
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.ScanDelay,
            new LanguageTranslatorScanDoAfterEvent(), ent.Owner, target: target, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnScanFinished(Entity<LanguageTranslatorComponent> ent, ref LanguageTranslatorScanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target ||
            !TryComp<PaperComponent>(target, out var paper))
            return;

        args.Handled = true;
        if (!_powerCell.TryUseCharge(ent.Owner, ent.Comp.ChargePerScan, user: args.User))
            return;

        var original = FormattedMessage.RemoveMarkupPermissive(paper.Content);
        var language = paper.Language ?? Loc.GetString("paper-ui-language-common");
        var translated = Translate(original);
        _ui.SetUiState(ent.Owner, LanguageTranslatorUiKey.Key,
            new LanguageTranslatorUiState(language, translated));
        _ui.TryOpenUi(ent.Owner, LanguageTranslatorUiKey.Key, args.User);
        _popup.PopupEntity(Loc.GetString("language-translator-scan-complete"), ent.Owner, args.User);
    }

    private static string Translate(string original)
    {
        return original;
    }
}
