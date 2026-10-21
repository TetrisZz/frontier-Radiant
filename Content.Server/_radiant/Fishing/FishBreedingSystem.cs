using System.Linq;
using Content.Server._radiant.Decorations;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Fishing;

public sealed class FishBreedingSystem : EntitySystem
{
    [Dependency] private readonly AquariumSystem _aquariums = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<FishBreedingComponent, MapInitEvent>(OnInit);
    }

    private void OnInit(Entity<FishBreedingComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Initialized)
            return;
        ent.Comp.Initialized = true;
        ent.Comp.Female = _random.Prob(0.5f);
        ent.Comp.FirstAllele = _random.Next(3);
        ent.Comp.SecondAllele = _random.Next(3);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<AquariumComponent, AquariumBreedingComponent>();
        while (query.MoveNext(out var uid, out var aquarium, out var breeding))
        {
            if (!_aquariums.HasWater(uid) || aquarium.Food <= 0 ||
                !_containers.TryGetContainer(uid, "storagebase", out var container))
            {
                breeding.NextCheck = _timing.CurTime + TimeSpan.FromSeconds(breeding.Interval);
                continue;
            }
            var fish = container.ContainedEntities.Where(f => HasComp<FishBreedingComponent>(f) && _mobs.IsAlive(f)).ToArray();
            foreach (var occupant in fish)
            {
                var genes = Comp<FishBreedingComponent>(occupant);
                if (!genes.Juvenile)
                    continue;
                genes.GrowthSeconds++;
                if (genes.GrowthSeconds < FishBreedingRules.GrowthDuration)
                    continue;
                genes.Juvenile = false;
                Dirty(occupant, genes);
                _aquariums.UpdateContents(uid);
            }
            if (breeding.NextCheck == TimeSpan.Zero)
                breeding.NextCheck = _timing.CurTime + TimeSpan.FromSeconds(breeding.Interval);
            if (_timing.CurTime < breeding.NextCheck ||
                container.ContainedEntities.Count(HasComp<FishComponent>) >= aquarium.Capacity)
                continue;
            breeding.NextCheck = _timing.CurTime + TimeSpan.FromSeconds(breeding.Interval);
            var adults = fish.Where(f => !Comp<FishBreedingComponent>(f).Juvenile && Comp<FishBreedingComponent>(f).ReadyAt <= _timing.CurTime).ToArray();
            foreach (var mother in adults)
            {
                var maternal = Comp<FishBreedingComponent>(mother);
                if (!maternal.Female)
                    continue;
                var prototype = MetaData(mother).EntityPrototype;
                if (prototype == null)
                    continue;
                var father = adults.FirstOrDefault(f => f != mother &&
                    FishBreedingRules.Compatible(prototype.ID, MetaData(f).EntityPrototype?.ID ?? "",
                        maternal.Female, Comp<FishBreedingComponent>(f).Female,
                        maternal.Juvenile, Comp<FishBreedingComponent>(f).Juvenile));
                if (father == default)
                    continue;
                var paternal = Comp<FishBreedingComponent>(father);
                var fry = Spawn(prototype.ID, Transform(uid).Coordinates);
                var child = Comp<FishBreedingComponent>(fry);
                child.Initialized = true;
                child.Juvenile = true;
                child.GrowthSeconds = 0;
                child.FirstAllele = _random.Prob(0.5f) ? maternal.FirstAllele : maternal.SecondAllele;
                child.SecondAllele = _random.Prob(0.5f) ? paternal.FirstAllele : paternal.SecondAllele;
                Dirty(fry, child);
                if (!_storage.Insert(uid, fry, out _))
                {
                    QueueDel(fry);
                    break;
                }
                maternal.ReadyAt = paternal.ReadyAt = _timing.CurTime + TimeSpan.FromSeconds(breeding.Interval);
                aquarium.Food--;
                _aquariums.UpdateContents(uid);
                break;
            }
        }
    }

}
