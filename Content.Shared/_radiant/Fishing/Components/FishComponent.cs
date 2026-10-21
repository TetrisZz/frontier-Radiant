// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Fishing.Components;
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class FishComponent : Component
{
    public const float DefaultDifficulty = 0.021f;
    [DataField("difficulty")]
    public float FishDifficulty = DefaultDifficulty;

    [DataField]
    public string AquariumState = "bass";

    [DataField]
    public TimeSpan AirGracePeriod = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, AutoNetworkedField]
    public TimeSpan AirGraceEnds;

    [DataField, AutoNetworkedField]
    public bool InAquariumWater;
}
