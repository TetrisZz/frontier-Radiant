using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Social;

[RegisterComponent]
public sealed partial class BouquetNoteComponent : Component;

[RegisterComponent]
public sealed partial class HairAccessoryComponent : Component
{
    [DataField] public string Style = "HumanHairPonytail";
    [DataField] public List<string> Styles = new()
    {
        "HumanHairPonytail", "HumanHairPonytail2", "HumanHairPonytail3", "HumanHairPonytail4",
        "HumanHairPonytail5", "HumanHairPonytail6", "HumanHairPonytail7", "HumanHairTwintail",
        "HumanHairBun", "HumanHairBunhead2", "HumanHairBun3", "HumanHairLargebun",
        "HumanHairBraid", "HumanHairBraided", "HumanHairBraid2", "HumanHairBraidtail",
        "HumanHairLongStraightTwintailsRS", "HumanHairSpikyPonytailAltRS", "HumanHairTriBunRS",
    };
    [DataField] public EntityUid? Wearer;
    [DataField] public List<Marking>? Original;
    [DataField] public string? AppliedStyle;
}

[Serializable, NetSerializable]
public enum HairAccessoryUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class HairAccessoryUiState(List<string> styles, string selected) : BoundUserInterfaceState
{
    public readonly List<string> Styles = styles;
    public readonly string Selected = selected;
}

[Serializable, NetSerializable]
public sealed class HairAccessorySelectMessage(string style) : BoundUserInterfaceMessage
{
    public readonly string Style = style;
}

[RegisterComponent]
public sealed partial class StoryRelicComponent : Component
{
    [DataField(required: true)] public LocId Story;
}
