using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._radiant.Careers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._radiant.Careers;

[RegisterComponent]
public sealed partial class CareerSpawnPointComponent : Component
{
    [DataField(required: true)] public ProtoId<CareerProfessionPrototype> Profession;
}

public sealed class CareerSpawnPointSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawningEvent>(OnSpawning,
            before: [typeof(SpawnPointSystem), typeof(ContainerSpawnPointSystem)]);
    }

    private void OnSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || args.Job is not { } job
            || args.HumanoidCharacterProfile?.Career is not { } career
            || !_prototypes.TryIndex<CareerProfessionPrototype>(career.Profession, out var profession)
            || !profession.Jobs.Contains(job.Id) && profession.InterviewJob?.Id != job.Id)
            return;
        var positions = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<CareerSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (marker.Profession.Id != career.Profession || xform.MapUid == null
                || args.Station != null && _stations.GetOwningStation(uid, xform) != args.Station)
                continue;
            positions.Add(xform.Coordinates);
        }
        if (positions.Count > 0)
            args.SpawnResult = _spawning.SpawnPlayerMob(_random.Pick(positions), job,
                args.HumanoidCharacterProfile, args.Station, session: args.Session);
    }
}
