using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Shared.Interaction.Panel;

public sealed class InteractionPanelManager : IPostInjectInit
{
    public const int MaxFileBytes = 262144;
    public const int MaxActions = 128;
    [Dependency] private ISerializationManager _serManager = default!;

    public void PostInject() { }

    public List<InteractionPrototype> FromStream(Stream stream)
    {
        // Bound input before invoking the YAML parser, including non-seekable file dialogs.
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes)
                throw new InvalidDataException("interaction-panel-file-too-large");
            buffer.Write(bytes, 0, read);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, EncodingHelpers.UTF8);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
            throw new InvalidDataException("interaction-panel-file-format");

        var root = yaml.Documents[0].RootNode;
        var nodeBudget = 20000;
        ValidateShape(root, 0, ref nodeBudget);
        if (root is YamlMappingNode package)
        {
            if (!TryField(package, "version", out var version)
                || version is not YamlScalarNode { Value: "1" }
                || !TryField(package, "actions", out root))
                throw new InvalidDataException("interaction-panel-file-version");
        }
        if (root is not YamlSequenceNode sequence || sequence.Children.Count > MaxActions)
            throw new InvalidDataException("interaction-panel-file-count");
        foreach (var node in sequence.Children)
        {
            if (node is not YamlMappingNode action
                || TryField(action, "type", out var type)
                && type is not YamlScalarNode { Value: "interaction" })
                throw new InvalidDataException("interaction-panel-file-type");
        }

        var prototypes = _serManager.Read<List<InteractionPrototype>>(sequence.ToDataNode(), notNullableOverride: true);
        Validate(prototypes);
        return prototypes;
    }

    public DataNode ToDataNode(InteractionPrototype prototype) => ToDataNode(new[] { prototype });

    public DataNode ToDataNode(IEnumerable<InteractionPrototype> prototypes)
    {
        var actions = prototypes.ToList();
        Validate(actions);
        var sequence = new SequenceDataNode();
        foreach (var action in actions)
        {
            // Use the same field serializers as import, so no editable fields are lost.
            var mapping = (MappingDataNode) _serManager.WriteValue(action, alwaysWrite: true, notNullableOverride: true);
            mapping.Add("type", new ValueDataNode("interaction"));
            sequence.Add(mapping);
        }
        return new MappingDataNode
        {
            { "version", new ValueDataNode("1") },
            { "actions", sequence }
        };
    }

    private static void Validate(IReadOnlyList<InteractionPrototype> actions)
    {
        if (actions.Count > MaxActions)
            throw new InvalidDataException("interaction-panel-file-count");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.ID) || action.ID.Length > 128
                || !Regex.IsMatch(action.ID, "^[a-zA-Z0-9_-]+$") || !ids.Add(action.ID)
                || string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 256
                || action.Abstract || action.Parents?.Length > 0)
                throw new InvalidDataException("interaction-panel-file-identity");
            if (action.UserMessages.Count == 0
                || new[] { action.UserMessages, action.TargetMessages, action.OtherMessages }
                    .Any(messages => messages.Count > 32 || messages.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 4096)))
                throw new InvalidDataException("interaction-panel-file-messages");
            if (!float.IsFinite(action.DoAfterDelay) || action.DoAfterDelay < 0
                || action.UseDelay < TimeSpan.Zero || action.UseDelay > TimeSpan.FromHours(1))
                throw new InvalidDataException("interaction-panel-file-delay");
            if (action.InteractSound is SoundPathSpecifier sound)
            {
                var path = sound.Path.ToString();
                if (path.Contains("..", StringComparison.Ordinal)
                    || !Regex.IsMatch(path, @"^(/Audio/Voice/|/Audio/Effects/|/Audio/_radiant/Voice/).+\.ogg$"))
                    throw new InvalidDataException("interaction-panel-file-sound");
            }
        }
    }

    private static void ValidateShape(YamlNode node, int depth, ref int budget)
    {
        if (depth > 16 || --budget < 0)
            throw new InvalidDataException("interaction-panel-file-complex");
        if (node is YamlMappingNode mapping)
        {
            foreach (var pair in mapping)
            {
                ValidateShape(pair.Key, depth + 1, ref budget);
                ValidateShape(pair.Value, depth + 1, ref budget);
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            foreach (var child in sequence.Children)
                ValidateShape(child, depth + 1, ref budget);
        }
    }

    private static bool TryField(YamlMappingNode mapping, string name, out YamlNode value)
    {
        // Mapping enumeration is sandbox-safe across supported YamlDotNet versions.
        foreach (var pair in mapping)
        {
            if (pair.Key is not YamlScalarNode key || key.Value != name)
                continue;
            value = pair.Value;
            return true;
        }
        value = default!;
        return false;
    }
}
