using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Nutrition;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CoconutDrinkComponent : Component
{
    [DataField, AutoNetworkedField]
    public CoconutDrinkState State;
}

[Serializable, NetSerializable]
public enum CoconutDrinkState : byte { Closed, Carved, Chopped, CarvedStraw, ChoppedStraw }

[Serializable, NetSerializable]
public enum CoconutDrinkVisuals : byte { State }

[Serializable, NetSerializable]
public sealed partial class CoconutPrepareDoAfterEvent : SimpleDoAfterEvent;
