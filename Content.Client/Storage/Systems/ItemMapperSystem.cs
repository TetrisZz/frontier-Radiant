using System.Collections.Generic;
using System.Linq;
using Content.Client.Items.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client.Storage.Systems;

public sealed class ItemMapperSystem : SharedItemMapperSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly ItemSystem _item = default!; //Radiant
    [Dependency] private readonly IResourceCache _resourceCache = default!; //Radiant

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemMapperComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemMapperComponent, AppearanceChangeEvent>(OnAppearance);
    }

    private void OnStartup(EntityUid uid, ItemMapperComponent component, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            component.RSIPath ??= sprite.BaseRSI!.Path;
        }
    }

    private void OnAppearance(EntityUid uid, ItemMapperComponent component, ref AppearanceChangeEvent args)
    {
        if (TryComp<SpriteComponent>(uid, out var spriteComponent))
        {
            if (component.SpriteLayers.Count == 0)
            {
                InitLayers((uid, component, spriteComponent, args.Component));
            }

            EnableLayers((uid, component, spriteComponent, args.Component));
            _item.VisualsChanged(uid); //Radiant
        }
    }

    private void InitLayers(Entity<ItemMapperComponent, SpriteComponent, AppearanceComponent> ent)
    {
        var (owner, component, spriteComponent, appearance) = ent;
        if (!_appearance.TryGetData<ShowLayerData>(owner, StorageMapVisuals.InitLayers, out var wrapper, appearance))
            return;

        component.SpriteLayers.AddRange(wrapper.QueuedEntities);

        foreach (var sprite in component.SpriteLayers)
        {//Radiant start
            _sprite.LayerMapReserve((owner, spriteComponent), sprite);
            ResPath spriteRsiPath = wrapper.RsiPaths.TryGetValue(sprite, out var path)
                ? GetValidLayerPath(component, spriteComponent, sprite, new ResPath(path))
                : component.RSIPath ?? throw new InvalidOperationException("ItemMapperComponent RSIPath must be set.");
            _sprite.LayerSetSprite((owner, spriteComponent), sprite, new SpriteSpecifier.Rsi(spriteRsiPath, sprite));
            _sprite.LayerSetVisible((owner, spriteComponent), sprite, false);
        }
    }

    private ResPath GetValidLayerPath(ItemMapperComponent component, SpriteComponent spriteComponent, string layerName, ResPath candidatePath)
    {
        if (TryGetValidPath(candidatePath, layerName, out var validPath))
            return validPath;

        if (component.RSIPath is { } rsiPath && TryGetValidPath(rsiPath, layerName, out validPath))
            return validPath;

        if (spriteComponent.BaseRSI?.TryGetState(layerName, out _) == true)
            return spriteComponent.BaseRSI.Path;

        return component.RSIPath ?? candidatePath;
    }

    private bool TryGetValidPath(ResPath path, string layerName, out ResPath validPath)
    {
        var resourcePath = SpriteSpecifierSerializer.TextureRoot / path;
        if (!_resourceCache.TryGetResource<RSIResource>(resourcePath, out var resource) || resource?.RSI == null)
        {
            validPath = path;
            return false;
        }

        if (resource.RSI.TryGetState(layerName, out _))
        {
            validPath = path;
            return true;
        }

        validPath = path;
        return false;
    }//Radiant end

    private void EnableLayers(Entity<ItemMapperComponent, SpriteComponent, AppearanceComponent> ent)
    {
        var (owner, component, spriteComponent, appearance) = ent;
        if (!_appearance.TryGetData<ShowLayerData>(owner, StorageMapVisuals.LayerChanged, out var wrapper, appearance))
            return;

        foreach (var layerName in component.SpriteLayers)
        {
            var show = wrapper.QueuedEntities.Contains(layerName);
            //Radiant start
            ResPath spriteRsiPath = wrapper.RsiPaths.TryGetValue(layerName, out var path)
                ? GetValidLayerPath(component, spriteComponent, layerName, new ResPath(path))
                : component.RSIPath ?? throw new InvalidOperationException("ItemMapperComponent RSIPath must be set.");

            _sprite.LayerSetSprite((owner, spriteComponent), layerName, new SpriteSpecifier.Rsi(spriteRsiPath, layerName));
            //Radiant end
            _sprite.LayerSetVisible((owner, spriteComponent), layerName, show);
        }
    }
}
