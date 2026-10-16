using Content.Shared._radiant.Skills;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Medical;

[RegisterComponent]
public sealed partial class RestorativeSynthesizerComponent : Component
{
    [DataField] public float DamageLimit = 100;
    [DataField] public DamageSpecifier Healing = new()
    {
        DamageDict = new()
        {
            ["Blunt"] = -0.25,
            ["Slash"] = -0.25,
            ["Piercing"] = -0.1,
        },
    };
}

public sealed class RestorativeSynthesizerSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextPulse;
    private readonly HashSet<EntityUid> _healed = new();
    private static readonly string[] Slots = ["back", "belt", "suitstorage"];

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextPulse)
            return;
        _nextPulse = _timing.CurTime + TimeSpan.FromSeconds(5);
        _healed.Clear();
        var query = EntityQueryEnumerator<RestorativeSynthesizerComponent>();
        while (query.MoveNext(out var uid, out var synthesizer))
        {
            if (MetaData(uid).EntityPaused)
                continue;
            var wearer = Transform(uid).ParentUid;
            if (!_healed.Contains(wearer) && TryHealWearer(uid, synthesizer))
                _healed.Add(wearer);
        }
    }

    public bool TryHealWearer(EntityUid uid, RestorativeSynthesizerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;
        var wearer = Transform(uid).ParentUid;
        if (!TryComp<MobStateComponent>(wearer, out var mob) || mob.CurrentState != MobState.Alive
            || !HasComp<BloodstreamComponent>(wearer)
            || !TryComp<DamageableComponent>(wearer, out var damage) || damage.TotalDamage >= comp.DamageLimit
            || _skills.Level(wearer, ProfessionalSkill.Medicine) < 1)
            return false;
        var equipped = false;
        foreach (var slot in Slots)
            equipped |= _inventory.TryGetSlotEntity(wearer, slot, out var item) && item == uid;
        if (!equipped)
            return false;
        var healed = _damage.TryChangeDamage(wearer, comp.Healing, ignoreResistances: true, interruptsDoAfters: false);
        return healed != null && healed.GetTotal() < 0;
    }
}
