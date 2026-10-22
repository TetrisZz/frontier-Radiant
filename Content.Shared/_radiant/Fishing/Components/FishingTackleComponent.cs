// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Fishing.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FishingHookComponent : Component
{
    [DataField, AutoNetworkedField]
    public float EfficiencyMultiplier = 1f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FishingLineComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Durability = 20;

    [DataField, AutoNetworkedField]
    public int MaxDurability = 20;

    [DataField, AutoNetworkedField]
    public int WearPerCast = 1;

    /// <summary>Chance of losing the line and hook when a fish escapes or the line is overstretched.</summary>
    [DataField, AutoNetworkedField]
    public float SnapChance = 0.25f;
}
