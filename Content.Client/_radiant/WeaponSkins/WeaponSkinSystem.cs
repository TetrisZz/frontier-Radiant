using Content.Shared._radiant.WeaponSkins;
using Content.Shared.Clothing.Components;
using Content.Shared.Item;
using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._radiant.WeaponSkins;

public sealed class WeaponSkinSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly ItemSystem _items = default!;
    [Dependency] private readonly ClientClothingSystem _clothing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeaponSkinAppliedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WeaponSkinAppliedComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<WeaponSkinAppliedComponent> ent, ref ComponentStartup args) => Apply(ent);
    private void OnState(Entity<WeaponSkinAppliedComponent> ent, ref AfterAutoHandleStateEvent args) => Apply(ent);

    private void Apply(Entity<WeaponSkinAppliedComponent> ent)
    {
        // ComponentStartup can run before the first replicated state has populated Skin.
        // AfterAutoHandleStateEvent will call this again once the server value arrives.
        if (string.IsNullOrEmpty(ent.Comp.Skin.Id)
            || !_prototypes.TryIndex(ent.Comp.Skin, out WeaponSkinPrototype? skin))
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprites.LayerSetSprite((ent, sprite), 0, new SpriteSpecifier.Rsi(new ResPath(skin.Sprite), skin.State));

        if (HasComp<ItemComponent>(ent))
            _items.SetRsiPath(ent, skin.ItemSprite);

        if (HasComp<ClothingComponent>(ent))
            _clothing.SetRsiPath(ent, skin.ClothingSprite ?? skin.Sprite);
    }
}
