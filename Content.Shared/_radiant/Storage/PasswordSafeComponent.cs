using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Storage;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PasswordSafeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Broken;

    [ViewVariables]
    public TimeSpan AnimationUntil;
}

[Serializable, NetSerializable]
public enum PasswordSafeUiKey : byte { Key }

[Serializable, NetSerializable]
public enum PasswordSafeVisuals : byte { State }

[Serializable, NetSerializable]
public enum PasswordSafeState : byte { Closed, Open, Locking, Sparking }

[Serializable, NetSerializable]
public sealed class PasswordSafeUiState(bool configured, bool open, bool broken) : BoundUserInterfaceState
{
    public readonly bool Configured = configured;
    public readonly bool Open = open;
    public readonly bool Broken = broken;
}

[Serializable, NetSerializable]
public sealed class PasswordSafeSubmitMessage(string code, string confirmation, bool configure) : BoundUserInterfaceMessage
{
    public readonly string Code = code;
    public readonly string Confirmation = confirmation;
    public readonly bool Configure = configure;
}

[Serializable, NetSerializable]
public sealed class PasswordSafeCloseMessage : BoundUserInterfaceMessage;

[ByRefEvent]
public readonly record struct PasswordSafeChangedEvent;

public static class PasswordSafeCode
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    public static bool IsNumeric(string code)
    {
        foreach (var digit in code)
            if (digit is < '0' or > '9')
                return false;
        return true;
    }

    public static bool IsValid(string code)
        => code.Length is >= MinLength and <= MaxLength && IsNumeric(code);
}
