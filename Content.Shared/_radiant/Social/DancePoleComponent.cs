using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Social;

[RegisterComponent]
public sealed partial class DancePoleComponent : Component
{
    public EntityUid? Dancer;
    [DataField] public int Finish;
    [DataField] public bool LightEnabled;
    [DataField] public int LightColor;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class PoleDancerComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid Pole;
    [DataField, AutoNetworkedField] public PoleDanceMove Move;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartedAt;
}

[Serializable, NetSerializable]
public enum PoleDanceMove : byte { Sway, Turn, Lean }

[Serializable, NetSerializable]
public enum DancePoleAction : byte { Dance, Stop, Finish, LightColor, LightToggle }

[Serializable, NetSerializable]
public enum DancePoleUiKey : byte { Key }

[Serializable, NetSerializable]
public enum DancePoleVisuals : byte { Finish }

[Serializable, NetSerializable]
public sealed class DancePoleUiState(NetEntity? dancer, PoleDanceMove move, int finish, bool light, int lightColor)
    : BoundUserInterfaceState
{
    public readonly NetEntity? Dancer = dancer;
    public readonly PoleDanceMove Move = move;
    public readonly int Finish = finish;
    public readonly bool Light = light;
    public readonly int LightColor = lightColor;
}

[Serializable, NetSerializable]
public sealed class DancePoleMessage(DancePoleAction action, int value = 0) : BoundUserInterfaceMessage
{
    public readonly DancePoleAction Action = action;
    public readonly int Value = value;
}

public static class DancePolePalette
{
    public static readonly Color[] Finishes = [Color.White, Color.FromHex("#E3C36F"), Color.FromHex("#748D9E")];
    public static readonly Color[] Lights = [Color.FromHex("#FF7598"), Color.FromHex("#72D9EC"),
        Color.FromHex("#E9C968"), Color.FromHex("#8BE0AD")];
}
