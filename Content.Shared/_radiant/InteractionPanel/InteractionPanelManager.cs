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
    [Dependency] private readonly ISerializationManager _serManager = default!;
    private ISawmill _sawmill = default!;

    public void PostInject()
    {
        _sawmill = Logger.GetSawmill("interaction_import");
    }

    /// <summary>
    /// !@#%*#%*#$!#@$
    /// </summary>
    /// <param name="stream">Stream</param>
    /// <returns></returns>
    /// <exception cref="InvalidDataException">:P</exception>
    public List<InteractionPrototype> FromStream(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, EncodingHelpers.UTF8);
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            if (yamlStream.Documents.Count == 0)
            {
                throw new InvalidDataException("YAML file is empty.");
            }

            var root = yamlStream.Documents[0].RootNode;
            var prototypes = _serManager.Read<List<InteractionPrototype>>(root.ToDataNode(), notNullableOverride: true);

            if (prototypes == null || prototypes.Count == 0)
            {
                throw new InvalidDataException("No valid prototypes found in the file.");
            }

            var validPrototypes = FilterValidPrototypes(prototypes);
            if (validPrototypes.Count == 0)
                return new List<InteractionPrototype>();

            return validPrototypes;
        }
        catch (Exception ex)
        {
            _sawmill = Logger.GetSawmill("interaction_import");
            _sawmill.Error($"Error while reading YAML file: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// List validation
    /// </summary>
    /// <param name="prototypes">The prototype sheet itself</param>
    /// <returns>The corrected list</returns>
    private List<InteractionPrototype> FilterValidPrototypes(List<InteractionPrototype> prototypes)
    {
        var validPrototypes = new List<InteractionPrototype>();
        var idSet = new HashSet<string>();

        var pathRegex = new Regex(@"^(/Audio/Voice/|/Audio/Effects/|/Audio/_radiant/Voice/).+\.ogg$", RegexOptions.Compiled);
        foreach (var prototype in prototypes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prototype.ID) || string.IsNullOrWhiteSpace(prototype.Name))
                {
                    _sawmill.Warning($"Prototype ID or Name is null or whitespace. Skipping prototype");
                    continue;
                }
                if (!IsValidId(prototype.ID))
                {
                    _sawmill.Warning($"Prototype ID '{prototype.ID}' contains invalid characters. Skipping prototype: {prototype.Name}");
                    continue;
                }
                if (prototype.Abstract)
                {
                    _sawmill.Warning($"Abstract types are not allowed in prototype ID '{prototype.ID}'. Skipping prototype: {prototype.Name}");
                    continue;
                }
                if (prototype.Parents?.Length > 0)
                {
                    _sawmill.Warning($"Prototype ID '{prototype.ID}' has parents. Skipping prototype: {prototype.Name}");
                    continue;
                }
                if (!idSet.Add(prototype.ID))
                {
                    _sawmill.Warning($"Duplicate prototype ID found: '{prototype.ID}'. Skipping prototype: {prototype.Name}");
                    continue;
                }
                if (!prototype.UserMessages.Any())
                {
                    _sawmill.Warning($"No messages found: '{prototype.ID}'. Skipping prototype: {prototype.Name}");
                    continue;
                }
                if (prototype.InteractSound is SoundPathSpecifier pathSpecifier && !pathRegex.IsMatch(pathSpecifier.Path.ToString()))
                {
                    _sawmill.Warning($"Invalid path format for prototype ID '{prototype.ID}'. Path must start with '/Audio/Voice/', '/Audio/Effects/', or '/Audio/_radiant/Voice/' and end with '.ogg'. Skipping prototype: {prototype.Name}");
                    continue;
                }

                validPrototypes.Add(prototype);
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Error while validating prototype '{prototype.ID}': {ex.Message}");
            }
        }

        return validPrototypes;
    }

    /// <summary>
    /// Collects the prototype data into a file with the correct formatting
    /// </summary>
    /// <param name="prototype">The resulting prototype</param>
    /// <returns>Formatted Node</returns>
    public DataNode ToDataNode(InteractionPrototype prototype)
    {
        var sequenceNode = new SequenceDataNode();
        var mapping = new MappingDataNode();

        mapping.Add("type", new ValueDataNode("interaction"));
        mapping.Add("id", new ValueDataNode(prototype.ID));
        mapping.Add("name", new ValueDataNode(prototype.Name));
        mapping.Add("erp", new ValueDataNode(prototype.ERP.ToString().ToLower()));

        if (prototype.Icon != null)
        {
            mapping.Add("icon", new ValueDataNode(prototype.Icon.ToString()));
        }

        if (prototype.UserMessages != null && prototype.UserMessages.Count > 0)
        {
            var userMessagesNode = new SequenceDataNode();
            foreach (var message in prototype.UserMessages)
            {
                userMessagesNode.Add(new ValueDataNode(message));
            }
            mapping.Add("userMessages", userMessagesNode);
        }

        if (prototype.AllowedSpecies != null && prototype.AllowedSpecies.Count > 0)
        {
            var allowedSpeciesNode = new SequenceDataNode();
            foreach (var species in prototype.AllowedSpecies)
            {
                allowedSpeciesNode.Add(new ValueDataNode(species));
            }
            mapping.Add("allowedSpecies", allowedSpeciesNode);
        }

        if (prototype.AllowedGenders != null && prototype.AllowedGenders.Count > 0)
        {
            var allowedGendersNode = new SequenceDataNode();
            foreach (var gender in prototype.AllowedGenders)
            {
                allowedGendersNode.Add(new ValueDataNode(gender));
            }
            mapping.Add("allowedGenders", allowedGendersNode);
        }

        if (prototype.NearestAllowedSpecies != null && prototype.NearestAllowedSpecies.Count > 0)
        {
            var nearestAllowedSpeciesNode = new SequenceDataNode();
            foreach (var species in prototype.NearestAllowedSpecies)
            {
                nearestAllowedSpeciesNode.Add(new ValueDataNode(species));
            }
            mapping.Add("nearestAllowedSpecies", nearestAllowedSpeciesNode);
        }

        if (prototype.NearestAllowedGenders != null && prototype.NearestAllowedGenders.Count > 0)
        {
            var nearestAllowedGendersNode = new SequenceDataNode();
            foreach (var gender in prototype.NearestAllowedGenders)
            {
                nearestAllowedGendersNode.Add(new ValueDataNode(gender));
            }
            mapping.Add("nearestAllowedGenders", nearestAllowedGendersNode);
        }

        if (prototype.BlackListSpecies != null && prototype.BlackListSpecies.Count > 0)
        {
            var blackListSpeciesNode = new SequenceDataNode();
            foreach (var species in prototype.BlackListSpecies)
            {
                blackListSpeciesNode.Add(new ValueDataNode(species));
            }
            mapping.Add("blackListSpecies", blackListSpeciesNode);
        }

        if (prototype.InteractSound != null)
        {
            if (prototype.InteractSound is SoundCollectionSpecifier collectionSpecifier)
            {
                var soundNode = new MappingDataNode().Add("collection", new ValueDataNode(collectionSpecifier.Collection));
                mapping.Add("interactSound", soundNode);
            }
            else if (prototype.InteractSound is SoundPathSpecifier pathSpecifier)
            {
                mapping.Add("interactSound", new ValueDataNode(pathSpecifier.Path.ToString()));
            }
        }

        mapping.Add("soundPerceivedByOthers", new ValueDataNode(prototype.SoundPerceivedByOthers.ToString().ToLower()));

        sequenceNode.Add(mapping);

        return sequenceNode;
    }

    /// <summary>
    /// ID validation
    /// </summary>
    /// <param name="id">Id prototype</param>
    /// <returns>true/false?</returns>
    private bool IsValidId(string id)
    {
        var regex = new Regex(@"^[a-zA-Z0-9]+$");
        return regex.IsMatch(id);
    }
}