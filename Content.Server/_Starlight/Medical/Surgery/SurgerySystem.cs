using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.Body.Part;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Interaction;
using Content.Shared.Prototypes;
using Content.Shared.Bed.Sleep;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared.Damage;
using Content.Server.Body.Systems;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Inventory;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;
using Content.Shared._radiant;
using Content.Shared.DetailExaminable;

namespace Content.Server._Starlight.Medical.Surgery;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
public sealed partial class SurgerySystem : SharedSurgerySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private SleepingSystem _sleeping = default!;
    [Dependency] private MobStateSystem _mobState = default!; // Radiant sector: vital organ removal.
    [Dependency] private InventorySystem _inventorySystem = default!; // Radiant sector: hide facial surgery behind masks.

    private readonly List<EntProtoId> _surgeries = [];
    public override void Initialize()
    {
        base.Initialize();
        InitializeSteps();

        SubscribeLocalEvent<SurgeryToolComponent, AfterInteractEvent>(OnToolAfterInteract);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        // Radiant sector: the ported surgery previously allowed a body to live
        // indefinitely after its heart or brain had been physically removed.
        SubscribeLocalEvent<OrganHeartComponent, SurgeryOrganExtracted>(OnVitalOrganExtracted);
        SubscribeLocalEvent<OrganBrainComponent, SurgeryOrganExtracted>(OnVitalOrganExtracted);

        LoadPrototypes();
    }

    private void OnVitalOrganExtracted<T>(Entity<T> ent, ref SurgeryOrganExtracted args)
        where T : Component
    {
        var body = args.Body;
        var organ = ent.Owner;

        // Radiant sector: check on the next tick. A replacement surgery removes
        // the old organ immediately before inserting the held one, and that
        // brief internal transition must not kill an otherwise valid patient.
        Timer.Spawn(TimeSpan.Zero, () =>
        {
            if (TerminatingOrDeleted(body)
                || !HasComp<Content.Shared.Mobs.Components.MobStateComponent>(body)
                || _body.GetBodyOrganEntityComps<T>(body).Count != 0)
                return;

            _mobState.ChangeMobState(body, MobState.Dead, origin: organ);
        });
    }

    protected override void RefreshUI(EntityUid body)
    {
        if (!HasComp<SurgeryTargetComponent>(body))
            return;

        var surgeries = new Dictionary<NetEntity, List<(EntProtoId, string suffix, bool isCompleted)>>();
        if (HasComp<BodyPartComponent>(body))
        {
            AddSurgeries(body, body, surgeries);
        }
        else
        {
            foreach (var part in _body.GetBodyChildren(body))
            {
                AddSurgeries(part.Id, body, surgeries);
            }
        }

        _ui.SetUiState(body, SurgeryUIKey.Key, new SurgeryBuiState() { Choices = surgeries });
    }

    private void AddSurgeries(EntityUid part, EntityUid body, Dictionary<NetEntity, List<(EntProtoId, string suffix, bool isCompleted)>> surgeries)
    {
        // Radiant sector: a worn mask physically covers the face. Do not offer
        // head/face operations until it is removed; the step-level armor check
        // remains as a safety check if equipment changes while the UI is open.
        if (TryComp<BodyPartComponent>(part, out var selectedPart)
            && selectedPart.PartType == BodyPartType.Head
            && _inventorySystem.TryGetSlotEntity(body, "mask", out _))
            return;

        if (!TryComp<SurgeryProgressComponent>(part, out var progress))
        {
            progress = new SurgeryProgressComponent();
            AddComp(part, progress);
        }

        foreach (var surgery in _surgeries)
        {
            if (!_entity.TryGetSingleton(surgery, out var surgeryEnt)
                || !TryComp(surgeryEnt, out SurgeryComponent? surgeryComp))
                continue;

            // Radiant sector: hide adult surgery immediately when the patient
            // opts out, including an operation that had already been started.
            if (IsAdultSurgery(surgeryEnt) && IsErpDenied(body))
                continue;

            // Radiant sector: species never changes while an operation is in
            // progress. Check it even for started/completed surgeries, otherwise
            // stale progress can expose both the normal and slime attachment
            // variants with the same localized name.
            if (TryComp<SurgerySpeciesConditionComponent>(surgeryEnt, out var speciesCondition))
            {
                if (!TryComp<HumanoidAppearanceComponent>(body, out var appearance)
                    || speciesCondition.SpeciesBlacklist.Contains(appearance.Species)
                    || speciesCondition.SpeciesWhitelist.Count > 0
                    && !speciesCondition.SpeciesWhitelist.Contains(appearance.Species))
                    continue;
            }

            // Radiant sector: an empty limb socket has to advertise its
            // attachment operation immediately. Selecting it will perform the
            // required incision first, instead of leaving no way to reinstall
            // a removed arm or leg in the torso UI.
            var unmetRequirement = surgeryComp.Requirement.Count > 0
                && !surgeryComp.Requirement.Any(requirement => IsSurgeryRequirementSatisfied(part, progress, requirement));
            if (unmetRequirement && !HasComp<SurgeryLimbSlotConditionComponent>(surgeryEnt))
                continue;

            var ev = new SurgeryValidEvent(body, part);

            var isCompleted = progress.CompletedSurgeries.Contains(surgery);
            if (!progress.StartedSurgeries.Contains(surgery)
                && !isCompleted)
            {
                RaiseLocalEvent(surgeryEnt, ref ev);

                if (ev.Cancelled)
                    continue;
            }

            surgeries.GetOrNew(GetNetEntity(part)).Add((surgery, ev.Suffix, isCompleted));
        }
    }

    private void OnToolAfterInteract(Entity<SurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        var user = args.User;
        if (args.Handled ||
            !args.CanReach ||
            args.Target == null ||
            _ui.IsUiOpen(user, SurgeryUIKey.Key, user) ||
            !HasComp<SurgeryTargetComponent>(args.Target)) return;

        if (user == args.Target)
        {
            _popup.PopupEntity(Loc.GetString("starlight-surgery-popup-self"), user, user);
            return;
        }

        args.Handled = true;
        _ui.OpenUi(args.Target.Value, SurgeryUIKey.Key, user);

        RefreshUI(args.Target.Value);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            LoadPrototypes();
    }

    private void LoadPrototypes()
    {
        _surgeries.Clear();

        foreach (var entity in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (entity.HasComponent<SurgeryComponent>())
                _surgeries.Add(new EntProtoId(entity.ID));
        }
    }
}
