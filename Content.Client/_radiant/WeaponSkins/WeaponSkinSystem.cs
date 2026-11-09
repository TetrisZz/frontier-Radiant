using Content.Shared._radiant.WeaponSkins;
using Content.Shared.Clothing.Components;
using Content.Shared.Item;
using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Content.Client.Weapons.Ranged.Components;
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
        {
            var skinRsi = new ResPath(skin.Sprite);
            if (_sprites.LayerMapTryGet((ent.Owner, sprite), GunVisualLayers.Base, out var baseLayer, false))
                _sprites.LayerSetSprite((ent.Owner, sprite), baseLayer, new SpriteSpecifier.Rsi(skinRsi, skin.State));
            else
                _sprites.LayerSetSprite((ent.Owner, sprite), 0, new SpriteSpecifier.Rsi(skinRsi, skin.State));

            // Magazine appearance is a separate visual layer. Swap its RSI too, but leave its
            // state alone so the gun's normal ammo/loaded-magazine visual updates still work.
            if (_sprites.LayerMapTryGet((ent.Owner, sprite), GunVisualLayers.Mag, out var magLayer, false))
                _sprites.LayerSetRsi((ent.Owner, sprite), magLayer, skinRsi);

            if (_sprites.LayerMapTryGet((ent.Owner, sprite), GunVisualLayers.MagUnshaded, out var magUnshadedLayer, false))
                _sprites.LayerSetRsi((ent.Owner, sprite), magUnshadedLayer, skinRsi);
        }

        if (HasComp<ItemComponent>(ent))
            _items.SetRsiPath(ent, skin.ItemSprite);

        if (HasComp<ClothingComponent>(ent))
            _clothing.SetRsiPath(ent, skin.ClothingSprite ?? skin.Sprite);
    }
}
