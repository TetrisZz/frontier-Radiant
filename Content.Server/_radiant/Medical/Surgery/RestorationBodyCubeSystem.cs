using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Medical.Surgery;

[RegisterComponent]
public sealed partial class RestorationBodyCubeComponent : Component
{
    [DataField] public string Species = "Human";
    [DataField] public Sex Sex = Sex.Male;
}

[RegisterComponent]
public sealed partial class RestorationBodyCubeBoxComponent : Component
{
    [DataField] public bool Filled;
}

public sealed partial class RestorationBodyCubeSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private RehydratableSystem _rehydratable = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RestorationBodyCubeBoxComponent, MapInitEvent>(OnBoxInit);
        SubscribeLocalEvent<RestorationBodyCubeComponent, GotRehydratedEvent>(OnHydrated);
    }

    private void OnBoxInit(Entity<RestorationBodyCubeBoxComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Filled)
            return;
        ent.Comp.Filled = true;
        foreach (var species in _prototypes.EnumeratePrototypes<SpeciesPrototype>()
                     .Where(s => s.RoundStart).OrderBy(s => s.ID))
        {
            foreach (var sex in new[] { Sex.Male, Sex.Female })
            {
                var cube = Spawn("RestorationBodyCube", Transform(ent).Coordinates);
                var data = Comp<RestorationBodyCubeComponent>(cube);
                data.Species = species.ID;
                data.Sex = sex;
                _rehydratable.SetSpawn(cube, species.Prototype);
                _metadata.SetEntityName(cube, Loc.GetString("restoration-cube-name",
                    ("species", Loc.GetString(species.Name)),
                    ("sex", Loc.GetString("restoration-sex-" + sex.ToString().ToLowerInvariant()))));
                if (!_storage.Insert(ent, cube, out _, playSound: false))
                    throw new InvalidOperationException("Restoration cube box is too small for lobby species.");
            }
        }
    }

    private void OnHydrated(Entity<RestorationBodyCubeComponent> ent, ref GotRehydratedEvent args)
    {
        // Species.Prototype is the same blank humanoid used for lobby spawning:
        // no ghost role, equipment, job or player mind is created.
        if (TryComp<HumanoidAppearanceComponent>(args.Target, out var appearance))
        {
            _appearance.SetSex(args.Target, ent.Comp.Sex, humanoid: appearance);
            _appearance.SetGender((args.Target, appearance), ent.Comp.Sex == Sex.Female
                ? Robust.Shared.Enums.Gender.Female : ent.Comp.Sex == Sex.Male
                    ? Robust.Shared.Enums.Gender.Male : Robust.Shared.Enums.Gender.Epicene);
        }
        _metadata.SetEntityName(args.Target, Loc.GetString("restoration-body-name"));
    }
}
