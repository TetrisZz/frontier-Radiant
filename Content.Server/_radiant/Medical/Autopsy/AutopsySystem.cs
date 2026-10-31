using System.Linq;
using System.Text;
using Content.Server._radiant.Dossiers;
using Content.Shared._radiant.Medical.Autopsy;
using Content.Shared._radiant.Skills;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Medical.Autopsy;

[RegisterComponent]
public sealed partial class AutopsyHistoryComponent : Component
{
    [DataField] public TimeSpan? DeathAt;
    [DataField] public Dictionary<string, float> ChemicalTraces = new();
}

public sealed class AutopsySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedRottingSystem _rotting = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly DossierSystem _dossiers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, MobStateChangedEvent>(OnDeath);
        SubscribeLocalEvent<AutopsyScannerComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<AutopsyScannerComponent, AutopsyScanDoAfterEvent>(OnScanned);
        SubscribeLocalEvent<AutopsyScannerComponent, BoundUIOpenedEvent>((Entity<AutopsyScannerComponent> ent, ref BoundUIOpenedEvent ev) => UpdateUi(ent, ev.Actor));
        SubscribeLocalEvent<AutopsyScannerComponent, AutopsyActionMessage>(OnAction);
    }

    private void OnDeath(Entity<BodyComponent> ent, ref MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
        {
            if (ev.OldMobState == MobState.Dead && TryComp<AutopsyHistoryComponent>(ent, out var previous))
            { previous.DeathAt = null; previous.ChemicalTraces.Clear(); }
            return;
        }
        var history = EnsureComp<AutopsyHistoryComponent>(ent);
        history.DeathAt = _timing.CurTime;
        history.ChemicalTraces = Chemicals(ent);
    }

    private Dictionary<string, float> Chemicals(EntityUid target)
    {
        var result = new Dictionary<string, float>();
        if (_solutions.TryGetSolution(target, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var solution))
            foreach (var entry in solution.Contents.OrderByDescending(r => r.Quantity).Take(16))
                if (entry.Quantity > 0) result[entry.Reagent.Prototype] = (float) entry.Quantity;
        return result;
    }

    private void OnInteract(Entity<AutopsyScannerComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Handled || !ev.CanReach || ev.Target is not { } target) return;
        ev.Handled = true;
        StartScan(ent, ev.User, target, false);
    }

    private bool Dead(EntityUid target) => TryComp<MobStateComponent>(target, out var state)
        && state.CurrentState == MobState.Dead && HasComp<BodyComponent>(target);

    internal bool HasSurgicalAccess(EntityUid target)
    {
        foreach (var (part, data) in _body.GetBodyChildren(target))
        {
            if (data.PartType != BodyPartType.Torso) continue;
            if (TryComp<SurgicalCavityStateComponent>(part, out var cavity) &&
                (cavity.RibcageOpen || cavity.AbdomenOpen)) return true;
        }
        return false;
    }

    private void Fail(EntityUid scanner, EntityUid user, string key)
        => _popup.PopupEntity(Loc.GetString(key), scanner, user);

    internal bool StartScan(Entity<AutopsyScannerComponent> ent, EntityUid user, EntityUid target, bool detailed)
    {
        if (ent.Comp.Busy || !_interaction.InRangeUnobstructed(user, target) || !_interaction.InRangeUnobstructed(user, ent.Owner)) return false;
        if (!_skills.Check(user, ProfessionalSkill.Medicine, detailed ? ent.Comp.DetailedSkill : ent.Comp.RequiredSkill, serverPopup: true)) return false;
        if (!Dead(target)) { Fail(ent, user, "autopsy-needs-corpse"); return false; }
        if (detailed && !HasSurgicalAccess(target)) { Fail(ent, user, "autopsy-needs-surgery"); return false; }
        var ev = new AutopsyScanDoAfterEvent { Detailed = detailed };
        var args = new DoAfterArgs(EntityManager, user, ent.Comp.ScanSeconds * (detailed ? 2 : 1), ev, ent, target, ent)
        { BreakOnMove = true, BreakOnDamage = true, NeedHand = true, BreakOnHandChange = true };
        if (!_doAfter.TryStartDoAfter(args)) return false;
        ent.Comp.Busy = true;
        UpdateUi(ent, user);
        return true;
    }

    private void OnScanned(Entity<AutopsyScannerComponent> ent, ref AutopsyScanDoAfterEvent ev)
    {
        if (ev.Handled) return;
        ev.Handled = true;
        ent.Comp.Busy = false;
        if (!ev.Cancelled && ev.Target is { } target && Dead(target)
            && _interaction.InRangeUnobstructed(ev.User, target)
            && _skills.Check(ev.User, ProfessionalSkill.Medicine, ev.Detailed ? ent.Comp.DetailedSkill : ent.Comp.RequiredSkill, popup: false)
            && (!ev.Detailed || HasSurgicalAccess(target)))
        {
            ent.Comp.Target = target;
            ent.Comp.Subject = Name(target);
            ent.Comp.Report = BuildReport(target, ev.User, ev.Detailed);
            ent.Comp.Summary = Loc.GetString("autopsy-record-summary", ("name", Name(target)),
                ("kind", Loc.GetString(ev.Detailed ? "autopsy-detailed" : "autopsy-external")));
            ent.Comp.Recorded = false;
            UpdateUi(ent, ev.User);
            _ui.TryOpenUi(ent.Owner, AutopsyUiKey.Key, ev.User);
        }
        UpdateUi(ent, ev.User);
    }

    internal string BuildReport(EntityUid target, EntityUid user, bool detailed)
    {
        var report = new StringBuilder();
        report.AppendLine(Loc.GetString("autopsy-report-title"));
        report.AppendLine(Loc.GetString("autopsy-report-person", ("name", Name(target)), ("examiner", Name(user))));
        report.AppendLine(Loc.GetString(detailed ? "autopsy-detailed" : "autopsy-external"));
        var rotten = _rotting.IsRotten(target);
        var known = TryComp<AutopsyHistoryComponent>(target, out var history) && history.DeathAt is { };
        if (known)
        {
            var minutes = Math.Max(0, (_timing.CurTime - history!.DeathAt!.Value).TotalMinutes);
            var uncertainty = rotten ? 15 : detailed ? 1 : 3;
            report.AppendLine(Loc.GetString("autopsy-death-interval", ("min", (int) Math.Max(0, Math.Floor(minutes) - uncertainty)),
                ("max", (int) Math.Ceiling(minutes) + uncertainty)));
        }
        else report.AppendLine(Loc.GetString("autopsy-death-unknown"));
        report.AppendLine(Loc.GetString(rotten ? "autopsy-rotten" : "autopsy-preserved"));
        report.AppendLine();
        report.AppendLine(Loc.GetString("autopsy-damage-heading"));
        if (TryComp<DamageableComponent>(target, out var damage))
        {
            foreach (var (kind, amount) in damage.Damage.DamageDict.Where(d => d.Value > 0).OrderByDescending(d => d.Value).Take(12))
            {
                var label = _prototypes.TryIndex<DamageTypePrototype>(kind, out var type) ? type.LocalizedName : kind;
                report.AppendLine($"- {label}: {Math.Ceiling((float) amount / (detailed && !rotten ? 5 : 10)) * (detailed && !rotten ? 5 : 10):0}");
            }
        }
        report.AppendLine(Loc.GetString("autopsy-damage-caveat"));
        report.AppendLine();
        report.AppendLine(Loc.GetString("autopsy-chem-heading"));
        if (detailed && !rotten)
        {
            var chemicals = Chemicals(target);
            foreach (var (kind, amount) in history?.ChemicalTraces ?? new()) chemicals.TryAdd(kind, amount);
            if (chemicals.Count == 0) report.AppendLine(Loc.GetString("autopsy-no-traces"));
            foreach (var (kind, _) in chemicals.Take(16))
            {
                var identified = _skills.CanIdentifyReagent(user, kind);
                var label = identified && _prototypes.TryIndex<ReagentPrototype>(kind, out var reagent)
                    ? reagent.LocalizedName : Loc.GetString("autopsy-unidentified-trace");
                report.AppendLine("- " + label);
            }
        }
        else report.AppendLine(Loc.GetString(rotten ? "autopsy-traces-lost" : "autopsy-chem-needs-surgery"));
        report.AppendLine();
        report.AppendLine(Loc.GetString("autopsy-conclusion-caveat"));
        return report.ToString();
    }

    private bool CanRecord(EntityUid user) => _access.FindAccessTags(user).Any(tag => tag.Id is "MedicalRecords" or "Medical" or "MedicalPhoenix");

    private void OnAction(Entity<AutopsyScannerComponent> ent, ref AutopsyActionMessage ev)
    {
        if (!_interaction.InRangeUnobstructed(ev.Actor, ent.Owner) || ent.Comp.Busy) return;
        if (ev.Action == AutopsyAction.Detailed && ent.Comp.Target is { } target)
        { StartScan(ent, ev.Actor, target, true); return; }
        if (ent.Comp.Report == "" || !_skills.Check(ev.Actor, ProfessionalSkill.Medicine, ent.Comp.RequiredSkill, serverPopup: true)) return;
        if (ev.Action == AutopsyAction.Print)
        {
            if (!_materials.TryChangeMaterialAmount(ent, "Paper", -100)) { Fail(ent, ev.Actor, "autopsy-no-paper"); return; }
            var paper = Spawn("RadiantAutopsyReport", Transform(ev.Actor).Coordinates);
            var body = ent.Comp.Report[(ent.Comp.Report.IndexOf('\n') + 1)..];
            _paper.SetContent(paper, "[head=2]" + FormattedMessage.EscapeText(Loc.GetString("autopsy-report-title")) + "[/head]\n\n" + FormattedMessage.EscapeText(body));
            // Hand items to the operator first; spawning on the scanner may overlap and
            // auto-insert the report into the patient or nearby storage containers.
            _hands.TryPickupAnyHand(ev.Actor, paper);
        }
        if (ev.Action == AutopsyAction.Record && !ent.Comp.Recorded && CanRecord(ev.Actor) && ent.Comp.Target is { } patient)
        {
            if (!_dossiers.AppendMedicalNote(patient, ev.Actor, ent.Comp.Summary))
                Fail(ent, ev.Actor, "autopsy-record-unavailable");
            else { ent.Comp.Recorded = true; Fail(ent, ev.Actor, "autopsy-recorded"); }
        }
        UpdateUi(ent, ev.Actor);
    }

    private void UpdateUi(Entity<AutopsyScannerComponent> ent, EntityUid user)
    {
        _ui.SetUiState(ent.Owner, AutopsyUiKey.Key, new AutopsyUiState(ent.Comp.Report,
            ent.Comp.Target is { } target && Dead(target) && HasSurgicalAccess(target),
            !ent.Comp.Recorded && ent.Comp.Target is { } patient && Exists(patient) && CanRecord(user), ent.Comp.Busy,
            _materials.GetMaterialAmount(ent, "Paper") / 100));
    }
}
