using System.Collections.Generic;
using System.Linq;
using Content.Shared.Clothing.Components;
using Content.Shared.Item;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.Containers;

namespace Content.Shared.Storage.EntitySystems;

/// <summary>
/// <c>ItemMapperSystem</c> is a system that on each initialization, insertion, removal of an entity from
/// given <see cref="ItemMapperComponent"/> (with appropriate storage attached) will check each stored item to see
/// if its tags/component, and overall quantity match <see cref="ItemMapperComponent.MapLayers"/>.
/// </summary>
[UsedImplicitly]
public abstract class SharedItemMapperSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemMapperComponent, ComponentInit>(InitLayers);
        SubscribeLocalEvent<ItemMapperComponent, EntInsertedIntoContainerMessage>(MapperEntityInserted);
        SubscribeLocalEvent<ItemMapperComponent, EntRemovedFromContainerMessage>(MapperEntityRemoved);
    }

    private void InitLayers(EntityUid uid, ItemMapperComponent component, ComponentInit args)
    {
        foreach (var (layerName, val) in component.MapLayers)
        {
            val.Layer = layerName;
        }

        if (TryComp(uid, out AppearanceComponent? appearanceComponent))
        {
            var list = new List<string>(component.MapLayers.Keys);
            _appearance.SetData(uid, StorageMapVisuals.InitLayers, new ShowLayerData(list), appearanceComponent);
        }

        // Ensure appearance is correct with current contained entities.
        UpdateAppearance(uid, component);
    }

    private void MapperEntityRemoved(EntityUid uid, ItemMapperComponent itemMapper, EntRemovedFromContainerMessage args)
    {
        if (itemMapper.ContainerWhitelist != null && !itemMapper.ContainerWhitelist.Contains(args.Container.ID))
            return;

        UpdateAppearance(uid, itemMapper);
    }

    private void MapperEntityInserted(EntityUid uid,
        ItemMapperComponent itemMapper,
        EntInsertedIntoContainerMessage args)
    {
        if (itemMapper.ContainerWhitelist != null && !itemMapper.ContainerWhitelist.Contains(args.Container.ID))
            return;

        UpdateAppearance(uid, itemMapper);
    }

    private void UpdateAppearance(EntityUid uid, ItemMapperComponent? itemMapper = null)
    {
        if (!Resolve(uid, ref itemMapper))
            return;

        if (TryComp(uid, out AppearanceComponent? appearanceComponent)
            && TryGetLayers(uid, itemMapper, out var containedLayers, out var rsiPaths)) //Radiant sector "rsiPaths"
        {
            _appearance.SetData(uid,
                StorageMapVisuals.LayerChanged,
                new ShowLayerData(containedLayers, rsiPaths), //Radiant sector "rsiPaths"
                appearanceComponent);
        }
    }

    /// <summary>
    /// Method that iterates over storage of the entity in <paramref name="uid"/> and sets <paramref name="showLayers"/>
    /// according to <paramref name="itemMapper"/> definition. It will have O(n*m) time behavior
    /// (n - number of entities in container, and m - number of definitions in <paramref name="showLayers"/>).
    /// </summary>
    /// <param name="uid">EntityUid used to search the storage</param>
    /// <param name="itemMapper">component that contains definition used to map
    /// <see cref="EntityWhitelist">Whitelist</see> in <see cref="ItemMapperComponent.MapLayers"/> to string.
    /// </param>
    /// <param name="showLayers">list of <paramref name="itemMapper"/> layers that should be visible</param>
    /// <returns>false if <c>msg.Container.Owner</c> is not a storage, true otherwise.</returns>
    private bool TryGetLayers(EntityUid uid, ItemMapperComponent itemMapper, out List<string> showLayers,
        out Dictionary<string, string> rsiPaths) //Radiant sector "rsiPaths"
    {
        var containedLayers = _container.GetAllContainers(uid)
            .Where(c => itemMapper.ContainerWhitelist?.Contains(c.ID) ?? true)
            .SelectMany(cont => cont.ContainedEntities)
            .ToArray();

        var list = new List<string>();
        var paths = new Dictionary<string, string>(); //Radiant sector
        foreach (var mapLayerData in itemMapper.MapLayers.Values)
        {
            //Radiant sector start
            var matchingEntities = containedLayers
                .Where(ent => _whitelistSystem.IsWhitelistPassOrNull(mapLayerData.Whitelist, ent))
                .ToArray();

            var count = matchingEntities.Length;
            if (count >= mapLayerData.MinCount && count <= mapLayerData.MaxCount)
            {
                list.Add(mapLayerData.Layer);

                if (matchingEntities.Length > 0)
                {
                    var firstMatch = matchingEntities[0];

                    if (TryComp<ClothingComponent>(firstMatch, out var clothingComponent) && clothingComponent.RsiPath != null)
                    {
                        paths[mapLayerData.Layer] = clothingComponent.RsiPath;
                    }
                    else if (TryComp<ItemComponent>(firstMatch, out var itemComponent) && itemComponent.RsiPath != null)
                    {
                        paths[mapLayerData.Layer] = itemComponent.RsiPath;
                    }
                }
            }
        }

        showLayers = list;
        rsiPaths = paths;
        //Radiant sector end
        return true;
    }
}
