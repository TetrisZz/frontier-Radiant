using System.Collections.Generic;
using System.Globalization;
using Content.Shared.DeviceLinking;
using Content.Shared.Lathe;
using Content.Shared.StationRecords;
using Content.Shared.Storage;
using Content.Shared.Store.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Server._Triad.Shipyard;

/// <summary>Export-only writers: never reset fields on live components to make them serializable.</summary>
internal sealed class ShipSaveSerializationWriters :
    ITypeWriter<StoreComponent>,
    ITypeWriter<StationRecordsFilter>,
    ITypeWriter<LinkedList<LatheRecipeBatch>>,
    ITypeWriter<Dictionary<EntityUid, ItemStorageLocation>>,
    ITypeWriter<Dictionary<EntityUid, HashSet<(ProtoId<SourcePortPrototype> Source, ProtoId<SinkPortPrototype> Sink)>>>
{
    public DataNode Write(ISerializationManager manager, StoreComponent value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        var snapshot = manager.CreateCopy(value, notNullableOverride: true);
        snapshot.AccountOwner = null;
        snapshot.StartingMap = null;
        snapshot.BoughtEntities.Clear();
        snapshot.BalanceSpent.Clear();
        foreach (var listing in snapshot.FullListingsCatalog)
            listing.ProductActionEntity = null;

        // No entity references remain. Use the default writer without recursively invoking this one.
        return manager.WriteValue(snapshot, alwaysWrite, context: null, notNullableOverride: true);
    }

    public DataNode Write(ISerializationManager manager, StationRecordsFilter value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        => ValueDataNode.Null();

    public DataNode Write(ISerializationManager manager, LinkedList<LatheRecipeBatch> value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        => new SequenceDataNode();

    public DataNode Write(ISerializationManager manager, Dictionary<EntityUid, ItemStorageLocation> value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        var result = new MappingDataNode();
        var serializer = (EntitySerializer) context!;
        foreach (var (item, location) in value)
        {
            // Excluded contents have no YAML UID; emitting them would duplicate the "invalid" key.
            if (!serializer.YamlUidMap.TryGetValue(item, out var yamlUid))
                continue;
            result.Add(yamlUid.ToString(CultureInfo.InvariantCulture),
                manager.WriteValue(location, alwaysWrite, context));
        }
        return result;
    }

    public DataNode Write(ISerializationManager manager,
        Dictionary<EntityUid, HashSet<(ProtoId<SourcePortPrototype> Source, ProtoId<SinkPortPrototype> Sink)>> value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        var result = new MappingDataNode();
        var serializer = (EntitySerializer) context!;
        foreach (var (sink, ports) in value)
        {
            // Missing sinks must not all become the same "invalid" dictionary key.
            if (!serializer.YamlUidMap.TryGetValue(sink, out var yamlUid))
                continue;
            result.Add(yamlUid.ToString(CultureInfo.InvariantCulture),
                manager.WriteValue(ports, alwaysWrite, context, notNullableOverride: true));
        }
        return result;
    }
}
