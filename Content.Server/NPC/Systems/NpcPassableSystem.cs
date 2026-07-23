using Content.Shared.Physics;
using Content.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Makes marked structures non-blocking for active NPCs without changing their collision for players.
/// </summary>
public sealed class NpcPassableSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private const CollisionGroup MobBlockingLayers =
        CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    public override void Initialize()
    {
        SubscribeLocalEvent<NpcPassableComponent, MapInitEvent>(OnPassableMapInit);
    }

    private void OnPassableMapInit(EntityUid uid, NpcPassableComponent component, MapInitEvent args)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return;

        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            var layer = fixture.CollisionLayer & ~(int) MobBlockingLayers;
            _physics.SetCollisionLayer(uid, id, fixture, layer | (int) CollisionGroup.NpcPassable, fixtures);
        }
    }

    public void SetNpcCollision(EntityUid uid, bool collideWithPassableStructures)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return;

        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            var mask = collideWithPassableStructures
                ? fixture.CollisionMask | (int) CollisionGroup.NpcPassable
                : fixture.CollisionMask & ~(int) CollisionGroup.NpcPassable;
            _physics.SetCollisionMask(uid, id, fixture, mask, fixtures);
        }
    }
}
