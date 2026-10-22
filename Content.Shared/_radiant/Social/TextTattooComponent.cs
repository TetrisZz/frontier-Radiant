using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Social;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class TextTattoo
{
    [DataField] public string Text { get; set; } = "";
    [DataField] public int Zone { get; set; }
    [DataField] public int Color { get; set; }
    public TextTattoo Clone() => new() { Text = Text, Zone = Zone, Color = Color };
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TextTattooComponent : Component
{
    [DataField, AutoNetworkedField] public List<TextTattoo> Tattoos = new();
}

[RegisterComponent]
public sealed partial class TattooMachineComponent : Component
{
    [DataField] public int Ink = 20;
    public EntityUid? Artist;
    public EntityUid? Patient;
    public TextTattoo? Pending;
    public bool Accepted;
    public int RequestId;
    public TimeSpan ExpiresAt;
}

public static class TextTattooRules
{
    public const int MaximumLength = 150;
    public const int MaximumTattoos = 12;
    public static readonly Color[] Colors = [Color.FromHex("#707070"), Color.FromHex("#B24D4D"),
        Color.FromHex("#4273B4"), Color.FromHex("#4E8B64"), Color.FromHex("#A15BAC"), Color.FromHex("#BE9644")];
    public static bool Valid(TextTattoo tattoo)
        => tattoo.Zone is >= 0 and < 6 && tattoo.Color >= 0 && tattoo.Color < Colors.Length &&
           !string.IsNullOrWhiteSpace(tattoo.Text) && tattoo.Text.Length <= MaximumLength;
    public static bool Matches(TextTattoo tattoo, BodyPartComponent part) => tattoo.Zone switch
    {
        0 => part.PartType == BodyPartType.Head,
        1 => part.PartType == BodyPartType.Torso,
        2 => part.PartType == BodyPartType.Arm && part.Symmetry == BodyPartSymmetry.Left,
        3 => part.PartType == BodyPartType.Arm && part.Symmetry == BodyPartSymmetry.Right,
        4 => part.PartType == BodyPartType.Leg && part.Symmetry == BodyPartSymmetry.Left,
        5 => part.PartType == BodyPartType.Leg && part.Symmetry == BodyPartSymmetry.Right,
        _ => false,
    };
}

[Serializable, NetSerializable]
public enum TattooUiKey : byte { Editor, Consent }

[Serializable, NetSerializable]
public sealed class TattooUiState(string patient, int ink, TextTattoo? tattoo, bool accepted) : BoundUserInterfaceState
{
    public readonly string Patient = patient;
    public readonly int Ink = ink;
    public readonly TextTattoo? Tattoo = tattoo;
    public readonly bool Accepted = accepted;
}

[Serializable, NetSerializable]
public sealed class TattooProposeMessage(string text, int zone, int color) : BoundUserInterfaceMessage
{
    public readonly string Text = text;
    public readonly int Zone = zone;
    public readonly int Color = color;
}

[Serializable, NetSerializable]
public sealed class TattooConsentMessage(bool accepted) : BoundUserInterfaceMessage
{
    public readonly bool Accepted = accepted;
}

[Serializable, NetSerializable]
public sealed partial class TattooApplyEvent : SimpleDoAfterEvent
{
    public int RequestId;
}
