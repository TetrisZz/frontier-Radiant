using Content.Server.Chat.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._radiant.Medical.Virology;

/// <summary>
/// Selects occasional infected expedition zombies based on the number of players that encounter them.
/// Their bite and cough transmit ordinary, diagnosable virology strains, not zombification.
/// </summary>
public sealed partial class VirologyZombieSourceSystem : EntitySystem
{
    private const float CrowdRadius = 8f;
    private const float CoughRadius = 2f;
    private const float BiteTransmissionChance = 0.2f;
    private const float CoughTransmissionChance = 0.2f;
    private const float CheckInterval = 5f;
    private const float CoughInterval = 45f;
    private const string BiteDisease = "Hematic";
    private const string CoughDisease = "StationFever";
    private static readonly ProtoId<TagPrototype> HardsuitTag = "Hardsuit";

    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VirologyZombieSourceComponent, MeleeHitEvent>(OnMeleeHit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<VirologyZombieSourceComponent>();
        while (query.MoveNext(out var uid, out var source))
        {
            if (MetaData(uid).EntityPaused || _mobs.IsDead(uid))
                continue;

            source.Accumulator += frameTime;
            if (source.Accumulator < CheckInterval)
                continue;
            source.Accumulator = 0;

            if (source.Disease.Length == 0)
            {
                TrySelectCarrier(uid, source);
                continue;
            }

            if (source.Disease != CoughDisease)
                continue;
            source.CoughClock += CheckInterval;
            if (source.CoughClock < CoughInterval)
                continue;
            source.CoughClock = 0;
            Cough(uid);
        }
    }

    private void TrySelectCarrier(EntityUid uid, VirologyZombieSourceComponent source)
    {
        var players = 0;
        foreach (var (target, _) in _lookup.GetEntitiesInRange<ActorComponent>(Transform(uid).Coordinates, CrowdRadius))
        {
            if (HasComp<MobStateComponent>(target) && !_mobs.IsDead(target))
                players++;
        }

        var tier = players >= 7 ? 3 : players >= 5 ? 2 : players >= 3 ? 1 : 0;
        if (tier <= source.CheckedCrowdTier)
            return;

        source.CheckedCrowdTier = tier;
        var chance = tier == 1 ? 0.1f : tier == 2 ? 0.25f : 0.4f;
        if (!_random.Prob(chance))
            return;

        source.Disease = _random.Prob(0.5f) ? BiteDisease : CoughDisease;
        var disease = _prototypes.Index<RadiantDiseasePrototype>(source.Disease);
        EnsureComp<VirologyCarrierComponent>(uid).Infections[source.Disease] = disease.Incubation;
    }

    private void OnMeleeHit(Entity<VirologyZombieSourceComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || ent.Comp.Disease != BiteDisease)
            return;

        var disease = _prototypes.Index<RadiantDiseasePrototype>(BiteDisease);
        foreach (var target in args.HitEntities)
        {
            if (target == ent.Owner ||
                _inventory.TryGetSlotEntity(target, "outerClothing", out var suit) && _tags.HasTag(suit.Value, HardsuitTag))
                continue;

            if (_random.Prob(BiteTransmissionChance))
                _virology.Expose(target, BiteDisease, disease.ExposureThreshold);
        }
    }

    private void Cough(EntityUid source)
    {
        _chat.TryEmoteWithChat(source, "Cough", ChatTransmitRange.NoGhosts,
            hideLog: true, ignoreActionBlocker: true, forceEmote: true);
        var disease = _prototypes.Index<RadiantDiseasePrototype>(CoughDisease);
        foreach (var (target, _) in _lookup.GetEntitiesInRange<BloodstreamComponent>(Transform(source).Coordinates, CoughRadius))
        {
            if (target == source || !_interaction.InRangeUnobstructed(source, target, CoughRadius))
                continue;

            var chance = CoughTransmissionChance * _virology.DropletProtection(target);
            if (_random.Prob(chance))
                _virology.Expose(target, CoughDisease, disease.ExposureThreshold);
        }
    }
}
