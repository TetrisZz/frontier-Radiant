using Content.Shared.Humanoid;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Surgery;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryChangeSexComponent : Component
{
    [DataField(required: true)] public Sex Sex;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryFinalizeSexComponent : Component
{
    [DataField(required: true)] public Sex Sex;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryChangeVoiceComponent : Component;

[Serializable, NetSerializable]
public enum SurgicalVoiceUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class SurgicalVoiceState(Sex sex, string currentVoice, string? originalVoice = null) : BoundUserInterfaceState
{
    public readonly Sex Sex = sex;
    public readonly string CurrentVoice = currentVoice;
    public readonly string? OriginalVoice = originalVoice;
}

[Serializable, NetSerializable]
public sealed class SurgicalVoiceMessage(string voice) : BoundUserInterfaceMessage
{
    public readonly string Voice = voice;
}
