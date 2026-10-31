using Content.Shared._radiant.Artisan;
using Robust.Client.GameObjects;

namespace Content.Client._radiant.Artisan;

public sealed class ArtisanVisualSystem : SharedArtisanSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArtisanProductComponent, ComponentStartup>((Entity<ArtisanProductComponent> ent, ref ComponentStartup args) => UpdateProduct(ent));
        SubscribeLocalEvent<ArtisanProductComponent, AfterAutoHandleStateEvent>((Entity<ArtisanProductComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateProduct(ent));
    }
    private void UpdateProduct(Entity<ArtisanProductComponent> ent)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite)) sprite.Color = ent.Comp.Glaze;
    }
}
