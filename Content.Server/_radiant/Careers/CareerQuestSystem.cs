using System.Linq;
using Content.Server._NF.Cargo.Components;
using Content.Server.Afk;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._radiant.Careers;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Careers;

public sealed class CareerQuestSystem : EntitySystem
{
    [Dependency] private readonly CareerSystem _careers = default!;
    [Dependency] private readonly CareerMedicalSystem _medical = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IAfkManager _afk = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private static readonly TimeSpan DutyInterval = TimeSpan.FromSeconds(1);
    private readonly Dictionary<EntityUid, DutySample> _duty = new();
    private readonly HashSet<EntityUid> _eligible = new();
    private TimeSpan _nextDuty;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CareerSaleCompletedEvent>(OnSaleCompleted);
        // MobState must still describe the target before this hit, including a killing hit.
        SubscribeLocalEvent<MobStateComponent, DamageChangedEvent>(OnDamageChanged,
            before: new[] { typeof(MobThresholdSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<CareerStageChangedEvent>(OnStageChanged);
    }

    private void OnStageChanged(CareerStageChangedEvent args) => _duty.Remove(args.Player);

    /// <summary>Called with the exact successful sale set, immediately before its entities are deleted.</summary>
    public CareerSaleSnapshot CaptureSale(EntityUid console, IReadOnlyCollection<EntityUid> sold)
    {
        var items = new List<CareerSaleItem>();
        var visited = new HashSet<EntityUid>();
        var roots = sold.ToHashSet();
        var modifier = TryComp<MarketModifierComponent>(console, out var market) ? market.Mod : 1.0;

        foreach (var root in sold)
        {
            // If both a container and its descendant occur in the sale set, count the subtree once.
            var parent = Transform(root).ParentUid;
            var nested = false;
            while (TryComp<TransformComponent>(parent, out var transform))
            {
                if (roots.Contains(parent))
                {
                    nested = true;
                    break;
                }
                parent = transform.ParentUid;
            }
            if (nested)
                continue;

            // Cargo applies immunity to the sold root's entire appraised contents.
            var effectiveModifier = HasComp<IgnoreMarketModifierComponent>(root) ? 1.0 : modifier;
            CaptureItem(root, effectiveModifier, visited, items);
        }

        return new CareerSaleSnapshot(items.ToArray(), GetSaleLocation(console));
    }

    private void CaptureItem(EntityUid uid, double modifier, HashSet<EntityUid> visited, List<CareerSaleItem> items)
    {
        if (!visited.Add(uid))
            return;

        // Pricing already multiplies stack prices by Count. Only quantity needs multiplication here.
        var value = _pricing.GetPrice(uid, includeContents: false) * modifier;
        items.Add(new CareerSaleItem(GetPrototypeIds(uid), GetTags(uid),
            TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1,
            double.IsFinite(value) ? Math.Max(0, value) : 0));

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
                CaptureItem(child, modifier, visited, items);
        }
    }

    private string? GetSaleLocation(EntityUid console)
    {
        if (TryComp<CareerSalePointComponent>(console, out var point))
            return point.Location;
        if (TryComp<CareerSalePointComponent>(Transform(console).GridUid, out point))
            return point.Location;
        return TryComp<CareerSalePointComponent>(_stations.GetOwningStation(console), out point)
            ? point.Location
            : null;
    }

    private void OnSaleCompleted(ref CareerSaleCompletedEvent args)
    {
        var participants = _careers.GetProgressParticipants(args.Seller).Distinct().ToArray();
        var totalValue = args.Snapshot.Items.Sum(item => item.Value);
        // Scale individual item values to the integer cash actually paid after all taxes and rounding.
        var payoutScale = totalValue > 0 && double.IsFinite(totalValue)
            ? Math.Max(0, args.Payout) / totalValue
            : 0;

        foreach (var item in args.Snapshot.Items)
        {
            foreach (var player in participants)
            {
                if (item.Quantity > 0)
                    _careers.RecordActivity(player, CareerObjectiveType.SaleQuantity, item.Quantity,
                        item.PrototypeIds, item.Tags, args.Snapshot.Location);
                if (item.Value > 0 && payoutScale > 0)
                    _careers.RecordActivity(player, CareerObjectiveType.SaleValue, item.Value * payoutScale,
                        item.PrototypeIds, item.Tags, args.Snapshot.Location);
            }
        }
    }

    private void OnDamageChanged(EntityUid uid, MobStateComponent mob, DamageChangedEvent args)
    {
        _medical.RecordHealing(uid, args);

        if (mob.CurrentState != MobState.Alive || args.DamageDelta == null ||
            args.Origin is not { } attacker || attacker == uid || !_careers.IsWorking(attacker))
            return;

        var damage = args.DamageDelta.DamageDict.Values
            .Where(delta => delta > 0)
            .Sum(delta => delta.Double());
        if (damage <= 0)
            return;

        var participants = _careers.GetProgressParticipants(attacker).Distinct().ToArray();
        if (participants.Length == 0)
            return;

        if (!TryComp<CareerDamageBudgetComponent>(uid, out var budget))
        {
            var prototype = MetaData(uid).EntityPrototype;
            if (prototype == null || !prototype.TryGetComponent<MobThresholdsComponent>(out var thresholds,
                    EntityManager.ComponentFactory))
                return;

            // 0: Alive is a state marker. The first Critical/Dead threshold ends the Alive budget.
            var originalBudget = 0.0;
            foreach (var (threshold, state) in thresholds.Thresholds)
            {
                if (state is not (MobState.Critical or MobState.Dead))
                    continue;
                originalBudget = threshold.Double();
                break;
            }
            if (originalBudget <= 0)
                return;

            budget = EnsureComp<CareerDamageBudgetComponent>(uid);
            budget.OriginalBudget = originalBudget;
        }

        var credited = Math.Min(damage, Math.Max(0, budget.OriginalBudget - budget.CreditedDamage));
        if (credited <= 0)
            return;

        // Consume once per target, before awarding the same credit to the attacker's crew.
        // Working attackers spend budget even without an active damage quest.
        budget.CreditedDamage += credited;
        var prototypeIds = GetPrototypeIds(uid);
        var tags = GetTags(uid);
        foreach (var player in participants)
            _careers.RecordActivity(player, CareerObjectiveType.Damage, credited, prototypeIds, tags);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            _duty.Clear();
            _nextDuty = TimeSpan.Zero;
            return;
        }

        var now = _timing.CurTime;
        var flush = now >= _nextDuty;
        if (flush)
            _nextDuty = now + DutyInterval;

        _eligible.Clear();
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var actor, out var mob))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame || actor.PlayerSession.AttachedEntity != uid ||
                mob.CurrentState != MobState.Alive || _afk.IsAfk(actor.PlayerSession) || !_careers.IsWorking(uid))
                continue;

            _eligible.Add(uid);
            if (!_duty.TryGetValue(uid, out var sample))
            {
                _duty[uid] = new DutySample(now, 0);
                continue;
            }

            var seconds = sample.Seconds;
            if (sample.LastSample is { } lastSample)
                seconds += Math.Max(0, (now - lastSample).TotalSeconds);
            if (flush && seconds > 0)
            {
                // Duty belongs to this online character; it is not shared with crew or tied to account age.
                _careers.RecordActivity(uid, CareerObjectiveType.DutyTime, seconds,
                    GetPrototypeIds(uid), GetTags(uid));
                seconds = 0;
            }
            _duty[uid] = new DutySample(now, seconds);
        }

        foreach (var uid in _duty.Keys.ToArray())
        {
            if (Deleted(uid))
                _duty.Remove(uid);
            else if (!_eligible.Contains(uid))
                // Preserve earned partial intervals, but never bridge an offline/AFK/dead/off-duty gap.
                _duty[uid] = new DutySample(null, _duty[uid].Seconds);
        }
    }

    private string[] GetPrototypeIds(EntityUid uid)
    {
        var ids = new HashSet<string>();
        if (MetaData(uid).EntityPrototype is { } prototype)
            AddPrototype(prototype);
        return ids.ToArray();

        void AddPrototype(EntityPrototype proto)
        {
            if (!ids.Add(proto.ID) || proto.Parents == null)
                return;
            foreach (var parent in proto.Parents)
            {
                if (_prototypes.TryIndex<EntityPrototype>(parent, out var parentPrototype))
                    AddPrototype(parentPrototype);
            }
        }
    }

    private string[] GetTags(EntityUid uid) => TryComp<TagComponent>(uid, out var tags)
        ? tags.Tags.Select(tag => tag.Id).ToArray()
        : Array.Empty<string>();

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _duty.Clear();
        _eligible.Clear();
        _nextDuty = TimeSpan.Zero;
    }

    private readonly record struct DutySample(TimeSpan? LastSample, double Seconds);
}
