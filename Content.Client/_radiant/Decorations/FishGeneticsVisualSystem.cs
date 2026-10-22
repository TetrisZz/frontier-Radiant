using System.Numerics;
using Content.Shared._radiant.Fishing.Components;
using Robust.Client.GameObjects;

namespace Content.Client._radiant.Decorations;

public sealed class FishGeneticsVisualSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<FishBreedingComponent, ComponentStartup>((Entity<FishBreedingComponent> ent, ref ComponentStartup _) => Apply(ent));
        SubscribeLocalEvent<FishBreedingComponent, AfterAutoHandleStateEvent>((Entity<FishBreedingComponent> ent, ref AfterAutoHandleStateEvent _) => Apply(ent));
    }
    private void Apply(Entity<FishBreedingComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;
        _sprites.SetColor((ent, sprite), ent.Comp.Tint);
        _sprites.SetScale((ent, sprite), new Vector2(ent.Comp.Juvenile ? 0.65f : 1f));
    }
}
