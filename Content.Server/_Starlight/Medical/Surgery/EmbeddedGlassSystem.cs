using System.Linq;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Slippery;
using Content.Shared.Standing;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed class EmbeddedGlassSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SurgerySystem _surgery = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    private HashSet<EntityUid> _alerted = new();
    private float _elapsed;

    public override void Initialize()
    {
        // Record the fragment before the shard's normal damage/delete trigger runs.
        SubscribeLocalEvent<GlassShardEmbedComponent, StepTriggeredOffEvent>(OnStep,
            before: new[] { typeof(TriggerSystem), typeof(SlipperySystem) });
        SubscribeLocalEvent<SurgeryExtractGlassComponent, SurgeryStepCompleteEvent>(OnExtract);
    }

    private void OnStep(Entity<GlassShardEmbedComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (TerminatingOrDeleted(ent) || ent.Comp.Embedded || _standing.IsDown(args.Tripper)
            || HasComp<Content.Shared.Mech.Components.MechPilotComponent>(args.Tripper)
            || HasComp<Content.Shared.Mech.Components.MechComponent>(args.Tripper)
            || _inventory.TryGetSlotEntity(args.Tripper, "shoes", out _)
            || MetaData(ent).EntityPrototype is not { } prototype)
            return;

        var patient = args.Tripper;
        var feet = _body.GetBodyChildren(patient)
            .Where(part => part.Component.PartType == BodyPartType.Foot
                && _surgery.UsesSurgicalSterility(patient, part.Id)
                && !HasComp<SurgicalDeadLimbComponent>(part.Id)).ToArray();
        if (feet.Length == 0)
            return;
        ent.Comp.Embedded = true;
        var foot = _random.Pick(feet).Id;
        var embedded = EnsureComp<EmbeddedGlassComponent>(foot);
        embedded.Fragments.Add(prototype.ID);
        Dirty(foot, embedded);
        var wound = EnsureComp<SurgicalSterilityComponent>(foot);
        SurgicalSterilityRules.AddContamination(wound, SurgicalSite.Surface, 15);
        Dirty(foot, wound);
        _alerts.ShowAlert(args.Tripper, "EmbeddedGlass");
        _alerted.Add(args.Tripper);
        _popup.PopupEntity(Loc.GetString("surgical-glass-embedded"), args.Tripper, args.Tripper, PopupType.MediumCaution);
    }

    private void OnExtract(Entity<SurgeryExtractGlassComponent> ent, ref SurgeryStepCompleteEvent args)
    {
        if (!TryComp<EmbeddedGlassComponent>(args.Part, out var embedded) || embedded.Fragments.Count == 0)
            return;
        var shard = Spawn(embedded.Fragments[0], Transform(args.User).Coordinates);
        _hands.TryPickupAnyHand(args.User, shard);
        embedded.Fragments.RemoveAt(0);
        Dirty(args.Part, embedded);
        var dirty = EnsureComp<SurgicalItemSterilityComponent>(shard);
        dirty.Dirty = true;
        dirty.Used = true;
        dirty.LastPatient = args.Body;
        Dirty(shard, dirty);
        _popup.PopupEntity(Loc.GetString("surgical-glass-removed"), args.Body, args.User);
        RefreshAlerts();
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1f)
            return;
        _elapsed = 0;
        RefreshAlerts();
    }

    private void RefreshAlerts()
    {
        var current = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<EmbeddedGlassComponent, BodyPartComponent>();
        while (query.MoveNext(out _, out var glass, out var part))
        {
            if (glass.Fragments.Count > 0 && part.Body is { } body && !TerminatingOrDeleted(body))
                current.Add(body);
        }
        foreach (var body in _alerted)
            if (!current.Contains(body) && !TerminatingOrDeleted(body))
                _alerts.ClearAlert(body, "EmbeddedGlass");
        foreach (var body in current)
            _alerts.ShowAlert(body, "EmbeddedGlass");
        _alerted = current;
    }
}
