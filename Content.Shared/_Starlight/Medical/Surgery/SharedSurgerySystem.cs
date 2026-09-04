using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Tag;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._radiant;
using Content.Shared.DetailExaminable;

namespace Content.Shared._Starlight.Medical.Surgery;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
public abstract partial class SharedSurgerySystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IReflectionManager _reflectionManager = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private StarlightEntitySystem _entitySystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeSteps();
        InitializeConditions();
    }

    public bool IsSurgeryValid
        (
            EntityUid body,
            EntityUid targetPart,
            EntProtoId surgery,
            EntProtoId stepId,
            out Entity<SurgeryComponent> surgeryEnt,
            out Entity<BodyPartComponent> partEnt,
            out EntityUid step
        )
    {
        surgeryEnt = default;
        partEnt = default;
        step = default;

        if (!HasComp<SurgeryTargetComponent>(body)
             || !IsLyingDown(body)
             || !_entitySystem.TryEntity(targetPart, out partEnt)
             || !_entitySystem.TryGetSingleton(surgery, out var surgeryEntId)
             || !_entitySystem.TryEntity(surgeryEntId, out surgeryEnt)
             || !_entitySystem.TryGetSingleton(stepId, out step)
             || !surgeryEnt.Comp.Steps.Contains(stepId))
            return false;

        // Radiant sector: ERP opt-out is a live safety condition. Unlike
        // anatomical conditions, it must remain enforced after surgery starts.
        if (IsAdultSurgery(surgeryEnt)
            && IsErpDenied(body))
            return false;

        var progress = EnsureComp<SurgeryProgressComponent>(targetPart);

        var ev = new SurgeryValidEvent(body, targetPart);

        if (!progress.StartedSurgeries.Contains(surgery))
        {
            RaiseLocalEvent(step, ref ev);
            RaiseLocalEvent(surgeryEntId, ref ev);
        }

        return !ev.Cancelled;
    }

    // Radiant sector: adult surgery prototypes have both the explicit consent
    // marker and anatomy-specific conditions. Checking all of them keeps the
    // safety rule intact if prototype component inheritance is changed later.
    public bool IsAdultSurgery(EntityUid surgery)
        => HasComp<SurgeryErpConsentConditionComponent>(surgery)
           || HasComp<SurgeryAdultOrganConditionComponent>(surgery)
           || HasComp<SurgeryAdultBreastSizeConditionComponent>(surgery);

    // Radiant sector: shared by UI filtering and authoritative server checks.
    public bool IsErpDenied(EntityUid entity)
        => TryComp<DetailExaminableComponent>(entity, out var detail)
           && detail.ERPStatus == EnumERPStatus.NO;

    public bool IsSurgeryRequirementSatisfied(EntityUid part, SurgeryProgressComponent? progress, EntProtoId requirement)
    {
        if (progress?.CompletedSurgeries.Contains(requirement) == true)
            return true;

        // Radiant sector: torso incisions are now tracked as independent
        // ribcage/abdomen/groin state flags. Follow-up surgeries must treat an
        // actually open cavity as satisfying its "open this area first"
        // requirement, even if the old surgery-progress list was cleared.
        if (!_entitySystem.TryGetSingleton(requirement, out var requirementEnt)
            || !TryComp<SurgeryCavityConditionComponent>(requirementEnt, out var cavityRequirement)
            || !TryComp<SurgicalCavityStateComponent>(part, out var cavities))
            return false;

        // The "open cavity" surgery itself is only valid while the cavity is
        // closed, so its condition has open: false. As a prerequisite, however,
        // that same surgery means the cavity must already be open.
        return cavities.IsOpen(cavityRequirement.Cavity);
    }

    protected List<EntityUid> GetTools(EntityUid surgeon) => [.. _hands.EnumerateHeld(surgeon)];

    public bool IsLyingDown(EntityUid entity)
    {
        if (_standing.IsDown(entity))
            return true;

        if (HasComp<ItemComponent>(entity))
            return true;

        if (TryComp(entity, out BuckleComponent? buckle) &&
            TryComp(buckle.BuckledTo, out StrapComponent? strap))
        {
            var rotation = strap.Rotation;
            if (rotation.GetCardinalDir() is Direction.West or Direction.East)
                return true;
        }

        return false;
    }

    protected virtual void RefreshUI(EntityUid body)
    {
    }

    /// <summary>
    /// Records a component type installed by a functional organ.
    /// </summary>
    /// <remarks>
    /// FunctionalOrganComponent restricts write/execute access to this class; OrganSystem
    /// (server) goes through this instead of touching Installed directly.
    /// </remarks>
    /// <param name="ent">The organ that installed the component.</param>
    /// <param name="type">The installed component's type.</param>
    public void AddInstalledComponent(Entity<FunctionalOrganComponent?> ent, Type type)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Installed.Add(type);
    }

    /// <summary>
    /// Clears the component types tracked for a functional organ.
    /// </summary>
    /// <remarks>
    /// FunctionalOrganComponent restricts write/execute access to this class; OrganSystem
    /// (server) goes through this instead of touching Installed directly.
    /// </remarks>
    /// <param name="ent">The organ whose tracked components should be cleared.</param>
    public void ClearInstalledComponents(Entity<FunctionalOrganComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Installed.Clear();
    }
}
