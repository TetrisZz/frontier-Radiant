using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._radiant.Careers;
using Content.Server._radiant.Medical.Virology;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Medical.IV;
using Content.Shared._radiant.Skills;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Medical.IV;

public sealed class IVDripSystem : SharedIVDripSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly ReactiveSystem _reactions = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly VirologySystem _virology = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IVDripComponent, DragDropDraggedEvent>(OnDrop);
        SubscribeLocalEvent<IVDripComponent, InteractHandEvent>(OnHand, before: [typeof(ItemSlotsSystem)]);
        SubscribeLocalEvent<IVDripComponent, GetVerbsEvent<InteractionVerb>>(OnVerbs);
        SubscribeLocalEvent<IVDripComponent, GetVerbsEvent<AlternativeVerb>>(OnRates);
        SubscribeLocalEvent<IVDripComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<IVDripComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<IVDripComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<IVDripComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<IVDripComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<IVBagComponent, SolutionContainerChangedEvent>(OnBagChanged);
    }

    private bool CanOperate(EntityUid user, EntityUid stand, bool trained = true)
        => Exists(user) && !TerminatingOrDeleted(user) && _blocker.CanInteract(user, stand)
            && _interaction.InRangeUnobstructed(user, stand)
            && (!trained || _skills.Check(user, ProfessionalSkill.Medicine, 1, serverPopup: true));

    public bool TryAttach(Entity<IVDripComponent> stand, EntityUid user, EntityUid patient)
    {
        if (!CanOperate(user, stand) || !CanConnect(stand, patient)
            || !_interaction.InRangeUnobstructed(user, patient))
            return false;
        var others = EntityQueryEnumerator<IVDripComponent>();
        while (others.MoveNext(out var other, out var otherStand))
            if (otherStand.AttachedTo == patient || other == stand.Owner && otherStand.AttachedTo != null)
                Detach((other, otherStand));
        stand.Comp.AttachedTo = patient;
        stand.Comp.Operator = user;
        stand.Comp.Flowing = false;
        stand.Comp.NextTransfer = _timing.CurTime + Interval(stand.Comp);
        Dirty(stand);
        _popup.PopupEntity(Loc.GetString("radiant-iv-attached", ("patient", Name(patient))), stand, user);
        _logs.Add(LogType.Ingestion, LogImpact.Medium,
            $"{ToPrettyString(user):user} attached IV stand {ToPrettyString(stand):stand} to {ToPrettyString(patient):patient}");
        RefreshBag(stand);
        return true;
    }

    public void Detach(Entity<IVDripComponent> stand, bool ripped = false)
    {
        if (stand.Comp.AttachedTo is not { } patient)
            return;
        stand.Comp.AttachedTo = null;
        stand.Comp.Operator = null;
        stand.Comp.Flowing = false;
        Dirty(stand);
        if (!Exists(patient) || TerminatingOrDeleted(patient))
            return;
        if (ripped)
        {
            _damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Piercing"] = 3 } }, true);
            _popup.PopupEntity(Loc.GetString("radiant-iv-ripped", ("patient", Name(patient))), patient);
        }
        _logs.Add(LogType.Action, LogImpact.Low,
            $"IV stand {ToPrettyString(stand):stand} disconnected from {ToPrettyString(patient):patient}, ripped={ripped}");
    }

    public bool SetTransferAmount(Entity<IVDripComponent> stand, EntityUid user, FixedPoint2 amount)
    {
        if (!CanOperate(user, stand) || amount < 1 || !stand.Comp.TransferAmounts.Contains(amount))
            return false;
        stand.Comp.TransferAmount = amount;
        Dirty(stand);
        return true;
    }

    public bool SetInjecting(Entity<IVDripComponent> stand, EntityUid user, bool injecting)
    {
        if (!CanOperate(user, stand))
            return false;
        stand.Comp.Injecting = injecting;
        stand.Comp.Operator = user;
        stand.Comp.Flowing = false;
        stand.Comp.NextTransfer = _timing.CurTime + Interval(stand.Comp);
        Dirty(stand);
        _logs.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):user} set IV stand {ToPrettyString(stand):stand} to injecting={injecting}");
        return true;
    }

    private static TimeSpan Interval(IVDripComponent stand)
        // A prototype may slow the pump down, but must not bypass its normal cadence.
        => stand.TransferInterval < TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : stand.TransferInterval;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<IVDripComponent>();
        while (query.MoveNext(out var uid, out var stand))
        {
            if (MetaData(uid).EntityPaused)
                continue;
            if (stand.AttachedTo is not { } patient)
                continue;
            if (!CanConnect((uid, stand), patient))
            {
                Detach((uid, stand), ripped: InRange((uid, stand), patient) == false);
                continue;
            }
            if (_timing.CurTime < stand.NextTransfer)
                continue;
            // One measured dose per interval: lag or reconnecting must never create a bolus.
            stand.NextTransfer = _timing.CurTime + Interval(stand);
            var flowing = TransferDose((uid, stand), patient);
            if (stand.Flowing != flowing)
            {
                stand.Flowing = flowing;
                Dirty(uid, stand);
            }
            RefreshBag((uid, stand));
        }
    }

    private bool TransferDose(Entity<IVDripComponent> stand, EntityUid patient)
    {
        if (!TryComp<BloodstreamComponent>(patient, out var blood)
            || _slots.GetItemOrNull(stand, stand.Comp.Slot) is not { } bag
            || !TryComp<IVBagComponent>(bag, out var pack)
            || !_solutions.TryGetSolution(bag, pack.Solution, out var source, out var contents)
            || !_solutions.ResolveSolution(patient, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodContents)
            || !_solutions.ResolveSolution(patient, blood.ChemicalSolutionName, ref blood.ChemicalSolution, out var chemicals))
            return false;
        var amount = FixedPoint2.Max(1, stand.Comp.TransferAmount);
        if (!stand.Comp.Injecting)
        {
            amount = FixedPoint2.Min(amount, contents.AvailableVolume, bloodContents.Volume);
            if (amount <= 0)
                return false;
            var drawn = _solutions.SplitSolution(blood.BloodSolution!.Value, amount);
            _virology.Contaminate(patient, drawn, "blood");
            if (_solutions.TryAddSolution(source.Value, drawn))
            {
                if (!stand.Comp.Flowing)
                    _logs.Add(LogType.ForceFeed, LogImpact.Medium,
                        $"IV stand {ToPrettyString(stand):stand} operated by {ToPrettyString(stand.Comp.Operator):user} began drawing blood from {ToPrettyString(patient):patient}, dose={amount}");
                return true;
            }
            _solutions.TryAddSolution(blood.BloodSolution.Value, drawn);
            return false;
        }

        amount = FixedPoint2.Min(amount, contents.Volume);
        if (amount <= 0)
            return false;
        var sample = contents.Clone().SplitSolution(amount);
        var compatibleBlood = sample.GetTotalPrototypeQuantity(blood.BloodReagent.Id);
        var medicine = sample.Volume - compatibleBlood;
        var permitted = amount;
        if (compatibleBlood > 0)
            permitted = FixedPoint2.Min(permitted, FixedPoint2.New(amount.Double() * bloodContents.AvailableVolume.Double() / compatibleBlood.Double()));
        if (medicine > 0)
            permitted = FixedPoint2.Min(permitted, FixedPoint2.New(amount.Double() * chemicals.AvailableVolume.Double() / medicine.Double()));
        if (permitted <= 0)
            return false;

        var dose = _solutions.SplitSolution(source.Value, permitted);
        var bloodDose = dose.SplitSolutionWithOnly(dose.Volume, blood.BloodReagent.Id);
        var applied = new Solution();
        if (bloodDose.Volume > 0)
        {
            if (_solutions.TryAddSolution(blood.BloodSolution!.Value, bloodDose))
                applied.AddSolution(bloodDose, _prototypes);
            else
                _solutions.TryAddSolution(source.Value, bloodDose);
        }
        if (dose.Volume > 0)
        {
            if (_solutions.TryAddSolution(blood.ChemicalSolution!.Value, dose))
                applied.AddSolution(dose, _prototypes);
            else
                _solutions.TryAddSolution(source.Value, dose);
        }
        if (applied.Volume <= 0)
            return false;
        // Use the ordinary injection reaction path, including blood-borne infections.
        if (stand.Comp.Operator is { } medic && Exists(medic) && !TerminatingOrDeleted(medic))
        {
            var administered = new CareerMedicineAdministeredEvent(medic, patient, applied);
            RaiseLocalEvent(ref administered);
        }
        _reactions.DoEntityReaction(patient, applied, ReactionMethod.Injection);
        if (!stand.Comp.Flowing)
            _logs.Add(LogType.Ingestion, LogImpact.Medium,
                $"IV stand {ToPrettyString(stand):stand} operated by {ToPrettyString(stand.Comp.Operator):user} began infusing {SharedSolutionContainerSystem.ToPrettyString(applied):solution} into {ToPrettyString(patient):patient}");
        return true;
    }

    private void OnDrop(Entity<IVDripComponent> ent, ref DragDropDraggedEvent args)
    {
        if (args.Handled)
            return;
        if (ent.Comp.AttachedTo == args.Target && CanOperate(args.User, ent, trained: false))
        {
            Detach(ent);
            args.Handled = true;
        }
        else
            args.Handled = TryAttach(ent, args.User, args.Target);
    }

    private void OnHand(Entity<IVDripComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || ent.Comp.AttachedTo == null || !CanOperate(args.User, ent, trained: false))
            return;
        Detach(ent);
        args.Handled = true;
    }

    private void OnVerbs(Entity<IVDripComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;
        var user = args.User;
        if (ent.Comp.AttachedTo != null)
            args.Verbs.Add(new InteractionVerb { Text = Loc.GetString("radiant-iv-detach"), Act = () =>
            { if (CanOperate(user, ent, trained: false)) Detach(ent); } });
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString(ent.Comp.Injecting ? "radiant-iv-draw-mode" : "radiant-iv-inject-mode"),
            Act = () => SetInjecting(ent, user, !ent.Comp.Injecting),
        });
    }

    private void OnRates(Entity<IVDripComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;
        var user = args.User;
        foreach (var amount in ent.Comp.TransferAmounts.Where(amount => amount >= 1).Distinct())
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("radiant-iv-set-rate", ("amount", amount), ("interval", Interval(ent.Comp).TotalSeconds)),
                Category = VerbCategory.SetTransferAmount,
                Act = () => SetTransferAmount(ent, user, amount),
            });
    }

    private void OnExamined(Entity<IVDripComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("radiant-iv-examine-rate", ("amount", ent.Comp.TransferAmount), ("interval", Interval(ent.Comp).TotalSeconds)));
        args.PushMarkup(Loc.GetString(ent.Comp.Injecting ? "radiant-iv-mode-inject" : "radiant-iv-mode-draw"));
        if (ent.Comp.Injecting)
            args.PushMarkup(Loc.GetString("radiant-iv-dose-help"));
        if (ent.Comp.AttachedTo is { } patient && Exists(patient))
            args.PushMarkup(Loc.GetString("radiant-iv-patient", ("patient", Name(patient))));
    }

    private void OnInserted(Entity<IVDripComponent> ent, ref EntInsertedIntoContainerMessage args)
    { if (args.Container.ID == ent.Comp.Slot) RefreshBag(ent); }

    private void OnRemoved(Entity<IVDripComponent> ent, ref EntRemovedFromContainerMessage args)
    { if (args.Container.ID == ent.Comp.Slot) { ent.Comp.Flowing = false; RefreshBag(ent); } }

    private void OnInit(Entity<IVDripComponent> ent, ref MapInitEvent args) => RefreshBag(ent);

    private void OnShutdown(Entity<IVDripComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.AttachedTo = null;
        ent.Comp.Operator = null;
        ent.Comp.Flowing = false;
    }

    private void OnBagChanged(Entity<IVBagComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == ent.Comp.Solution
            && _containers.TryGetContainingContainer(ent.Owner, out var container)
            && TryComp<IVDripComponent>(container.Owner, out var stand))
            RefreshBag((container.Owner, stand));
    }

    private void RefreshBag(Entity<IVDripComponent> stand)
    {
        var hasBag = false;
        var percent = 0;
        var color = Color.White;
        if (_slots.GetItemOrNull(stand, stand.Comp.Slot) is { } bag
            && TryComp<IVBagComponent>(bag, out var pack)
            && _solutions.TryGetSolution(bag, pack.Solution, out _, out var solution))
        {
            hasBag = true;
            percent = solution.MaxVolume > 0 ? (int)(solution.Volume.Double() / solution.MaxVolume.Double() * 100) : 0;
            color = solution.GetColor(_prototypes);
        }
        if (stand.Comp.HasBag == hasBag && stand.Comp.FillPercentage == percent && stand.Comp.FillColor == color)
            return;
        stand.Comp.HasBag = hasBag;
        stand.Comp.FillPercentage = percent;
        stand.Comp.FillColor = color;
        Dirty(stand);
    }
}
