using System.Linq;
using Content.Shared._radiant.Careers;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Careers;

[RegisterComponent]
public sealed partial class CareerChemicalOperatorComponent : Component
{
    public EntityUid User;
    public Guid Character;
    public int Level;
    public TimeSpan Expires;
}

[RegisterComponent]
public sealed partial class CareerMedicationCreditComponent : Component
{
    public Dictionary<string, MedicalCredit> Reagents = new();
}

public readonly record struct MedicalCredit(EntityUid User, Guid Character, int Level, TimeSpan Expires);

public sealed class CareerMedicalSystem : EntitySystem
{
    [Dependency] private readonly CareerSystem _careers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CareerChemicalOperationEvent>(OnOperation);
        SubscribeLocalEvent<CareerChemicalSynthesisEvent>(OnSynthesis);
        SubscribeLocalEvent<CareerMedicineAdministeredEvent>(OnMedication);
        SubscribeLocalEvent<CareerChemicalHealingEvent>(OnChemicalHealing);
        SubscribeLocalEvent<CareerImplantInstalledEvent>(OnImplant);
        SubscribeLocalEvent<SolutionContainerManagerComponent, InteractUsingEvent>(OnContainerInteraction,
            before: new[] { typeof(Content.Shared.Chemistry.EntitySystems.SolutionTransferSystem),
                typeof(Content.Server.Chemistry.EntitySystems.InjectorSystem) });
    }

    private bool IsDoctor(EntityUid user) => _careers.IsWorking(user)
        && _careers.TryGetCareer(user, out var career) && career.Profession == "Medical";

    private void OnImplant(ref CareerImplantInstalledEvent args)
    {
        if (args.User != args.Patient)
            RecordTask(args.User, CareerObjectiveType.Implantation);
    }

    private bool Current(MedicalCredit credit) => !TerminatingOrDeleted(credit.User)
        && credit.Expires >= _timing.CurTime && IsDoctor(credit.User)
        && _careers.TryGetCareer(credit.User, out var career)
        && career.CharacterId == credit.Character && career.Level == credit.Level;

    public void RecordTask(EntityUid user, CareerObjectiveType type, string? qualifier = null, double amount = 1)
    {
        if (!IsDoctor(user))
            return;
        foreach (var participant in _careers.GetProgressParticipants(user).Distinct().ToArray())
            _careers.RecordActivity(participant, type, amount, [], [], qualifier: qualifier);
    }

    public MedicalCredit? Capture(EntityUid user) => IsDoctor(user) && _careers.TryGetCareer(user, out var career)
        ? new MedicalCredit(user, career.CharacterId, career.Level, _timing.CurTime + TimeSpan.FromMinutes(30))
        : null;

    public void Complete(MedicalCredit? credit, CareerObjectiveType type, string? qualifier = null)
    {
        if (credit is { } source && Current(source))
            RecordTask(source.User, type, qualifier);
    }

    private void OnContainerInteraction(EntityUid uid, SolutionContainerManagerComponent comp, ref InteractUsingEvent args)
    {
        if (!args.Handled)
        {
            SetOperator(uid, args.User);
            if (HasComp<SolutionContainerManagerComponent>(args.Used))
                SetOperator(args.Used, args.User);
        }
    }

    private void OnOperation(ref CareerChemicalOperationEvent args) => SetOperator(args.Container, args.User);

    private void SetOperator(EntityUid container, EntityUid user)
    {
        if (!IsDoctor(user) || !_careers.TryGetCareer(user, out var career))
        {
            RemComp<CareerChemicalOperatorComponent>(container);
            return;
        }
        var op = EnsureComp<CareerChemicalOperatorComponent>(container);
        op.User = user;
        op.Character = career.CharacterId;
        op.Level = career.Level;
        op.Expires = _timing.CurTime + TimeSpan.FromMinutes(5);
    }

    private void OnSynthesis(ref CareerChemicalSynthesisEvent args)
    {
        if (!TryComp<ContainedSolutionComponent>(args.Solution, out var solution)
            || !TryComp<CareerChemicalOperatorComponent>(solution.Container, out var op)
            || !Current(new MedicalCredit(op.User, op.Character, op.Level, op.Expires)))
            return;
        RecordTask(op.User, CareerObjectiveType.Synthesis, args.Reagent, args.Quantity);
    }

    private void OnMedication(ref CareerMedicineAdministeredEvent args)
    {
        if (args.User == args.Patient || !IsDoctor(args.User)
            || !_careers.TryGetCareer(args.User, out var career) || !HasComp<MobStateComponent>(args.Patient))
            return;
        var credit = EnsureComp<CareerMedicationCreditComponent>(args.Patient);
        foreach (var reagent in args.Solution.Contents)
            credit.Reagents[reagent.Reagent.Prototype] = new MedicalCredit(args.User, career.CharacterId,
                career.Level, _timing.CurTime + TimeSpan.FromMinutes(10));
    }

    private void OnChemicalHealing(ref CareerChemicalHealingEvent args)
    {
        if (!HasComp<MobStateComponent>(args.Patient)
            || !TryComp<CareerMedicationCreditComponent>(args.Patient, out var credit)
            || !credit.Reagents.TryGetValue(args.Reagent, out var source) || !Current(source))
            return;
        var level = _careers.GetLevel(source.User);
        var healed = args.Healed.DamageDict.Where(pair => pair.Value < 0).ToArray();
        RecordTask(source.User, CareerObjectiveType.Healing, amount: -healed.Sum(pair => pair.Value.Double()));
        // A healing tick must not carry credit into a newly unlocked stage.
        if (_careers.GetLevel(source.User) != level)
            return;
        foreach (var (type, amount) in healed)
            RecordTask(source.User, CareerObjectiveType.ChemicalHealing, type, -amount.Double());
    }

    public void RecordHealing(EntityUid uid, DamageChangedEvent args)
    {
        if (args.Origin is not { } user || user == uid
            || args.DamageDelta == null)
            return;
        var healed = -args.DamageDelta.DamageDict.Values.Where(value => value < 0).Sum(value => value.Double());
        if (healed > 0)
            RecordTask(user, CareerObjectiveType.Healing, amount: healed);
    }
}
