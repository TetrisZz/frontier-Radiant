using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Damage;
using Robust.Shared.Timing;

namespace Content.Shared._radiant.Fishing.Systems;

public sealed class FishRespirationSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FishComponent, BeforeDamageChangedEvent>(OnDamage);
    }

    public bool CanBreathe(FishComponent fish) => fish.InAquariumWater || _timing.CurTime < fish.AirGraceEnds;

    private void OnDamage(Entity<FishComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // This must also run on the client, before passive damage can predict a false critical state.
        if (args.Origin == null && args.Damage.DamageDict.Count == 1 &&
            args.Damage.DamageDict.TryGetValue("Asphyxiation", out var amount) && amount > 0 && CanBreathe(ent.Comp))
            args.Cancelled = true;
    }
}
