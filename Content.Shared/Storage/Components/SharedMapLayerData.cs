using System.Collections.ObjectModel;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization;

namespace Content.Shared.Storage.Components
{
    [Serializable, NetSerializable]
    public enum StorageMapVisuals : sbyte
    {
        InitLayers,
        LayerChanged,
    }

    [Serializable]
    [DataDefinition]
    public sealed partial class SharedMapLayerData
    {
        public string Layer = string.Empty;

        [DataField(required: true)]
        public EntityWhitelist? Whitelist { get; set; }

        /// <summary>
        ///     Minimal amount of entities that are valid for whitelist.
        ///     If it's smaller than minimal amount, layer will be hidden.
        /// </summary>
        [DataField]
        public int MinCount = 1;

        /// <summary>
        ///     Max amount of entities that are valid for whitelist.
        ///     If it's bigger than max amount, layer will be hidden.
        /// </summary>
        [DataField]
        public int MaxCount = int.MaxValue;
    }

    [Serializable, NetSerializable]
    public sealed class ShowLayerData : ICloneable
    {
        public readonly IReadOnlyList<string> QueuedEntities;
        public readonly Dictionary<string, string> RsiPaths;

        public ShowLayerData()
        {
            QueuedEntities = new List<string>();
            RsiPaths = new Dictionary<string, string>();
        }

        public ShowLayerData(IReadOnlyList<string> other)
        {
            QueuedEntities = other;
            RsiPaths = new Dictionary<string, string>();
        }

        public ShowLayerData(IReadOnlyList<string> other, Dictionary<string, string> rsiPaths)
        {
            QueuedEntities = other;
            RsiPaths = rsiPaths;
        }

        public object Clone()
        {
            // QueuedEntities should never be getting modified after this object is created.
            return new ShowLayerData(new List<string>(QueuedEntities), new Dictionary<string, string>(RsiPaths));
        }
    }
}
