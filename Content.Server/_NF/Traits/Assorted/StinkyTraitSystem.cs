using Robust.Shared.Random;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Robust.Shared.Network;
using Content.Shared.Inventory;
using Content.Shared._NF.AirFreshener.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._NF.Traits.Assorted;

/// <summary>
/// This handles stink, causing the affected to stink uncontrollably at a random interval.
/// </summary>
public sealed class StinkyTraitSystem : EntitySystem
{
    private static readonly string[] AirFreshenerSlots = ["neck", "pocket1", "pocket2", "pocket3", "pocket4"];

    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<StinkyTraitComponent, ComponentStartup>(SetupStinkyTrait);
        SubscribeLocalEvent<StinkyTraitComponent, ExaminedEvent>(OnExamined);
    }

    private void SetupStinkyTrait(EntityUid uid, StinkyTraitComponent component, ComponentStartup args)
    {
        component.NextIncidentTime = RollIncidentDelay(component);
    }

    internal float RollIncidentDelay(StinkyTraitComponent? trait = null)
    {
        var interval = trait?.TimeBetweenIncidents ?? StinkyTraitComponent.DefaultIncidentInterval;
        return _random.NextFloat(interval.X, interval.Y);
    }

    public void AdjustStinkyTraitTimer(EntityUid uid, int timerReset, StinkyTraitComponent? stinky = null)
    {
        if (!Resolve(uid, ref stinky, false))
            return;

        stinky.NextIncidentTime = timerReset;
    }

    private void OnExamined(EntityUid uid, StinkyTraitComponent component, ExaminedEvent args)
    {
        if (args.IsInDetailsRange && !_net.IsClient && component.IsActive)
            DescribeStench(uid, args);
    }

    internal void DescribeStench(EntityUid uid, ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("trait-stinky-examined", ("target", Identity.Entity(uid, EntityManager))));

    internal bool HasAirFreshener(EntityUid uid)
    {
        foreach (var slot in AirFreshenerSlots)
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out var item) && HasComp<AirFreshenerComponent>(item))
                return true;
        }

        return false;
    }

    internal void AnnounceStench(EntityUid uid)
    {
        var othersMessage = Loc.GetString("trait-stinky-in-range-others", ("target", uid));
        _popup.PopupEntity(othersMessage, uid, Filter.PvsExcept(uid), true);

        var selfMessage = Loc.GetString("trait-stinky-in-range-self");
        _popup.PopupEntity(selfMessage, uid, uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StinkyTraitComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            component.NextIncidentTime -= frameTime;

            if (component.NextIncidentTime >= 0)
                continue;

            component.IsActive = !HasAirFreshener(uid);
            component.NextIncidentTime += RollIncidentDelay(component);
            if (component.IsActive)
                AnnounceStench(uid);
        }
    }
}
